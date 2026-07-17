using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Messaging;
using Skua.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Skua.Core.Scripts;

public partial class ScriptManager : ObservableObject, IScriptManager, IDisposable
{
    private static readonly Regex _versionRegex = new(@"^/\*[\s\S]*?version:\s*(\d+\.\d+\.\d+\.\d+)[\s\S]*?\*/", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex _accessibilityRegex = new(@"\b(public\s+|internal\s+|private\s+)?class\s+\w+", RegexOptions.Multiline | RegexOptions.Compiled);
    // Must match Compiler._cacheDirectory (same versioned folder) so the compiled
    // include DLLs the compiler writes are the ones this manager looks for.
    private static readonly string _cacheScriptsDir = Path.Combine(ClientFileSources.SkuaScriptsDIR, $"Cached-Scripts-{Compiler.CacheVersion}");
    public ScriptManager(
        ILogService logger,
        Lazy<IScriptInterface> scriptInterface,
        Lazy<IScriptHandlers> handlers,
        Lazy<IScriptSkill> skills,
        Lazy<IScriptDrop> drops,
        Lazy<IScriptWait> wait,
        Lazy<IAuraMonitorService> auraMonitorService)
    {
        _lazyBot = scriptInterface;
        _lazyHandlers = handlers;
        _lazySkills = skills;
        _lazyDrops = drops;
        _lazyWait = wait;
        _lazyAuraMonitor = auraMonitorService;
        _logger = logger;
    }

    private readonly Lazy<IScriptInterface> _lazyBot;
    private readonly Lazy<IScriptHandlers> _lazyHandlers;
    private readonly Lazy<IScriptSkill> _lazySkills;
    private readonly Lazy<IScriptDrop> _lazyDrops;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IAuraMonitorService> _lazyAuraMonitor;
    private readonly ILogService _logger;

    private IScriptHandlers Handlers => _lazyHandlers.Value;
    private IScriptSkill Skills => _lazySkills.Value;
    private IScriptDrop Drops => _lazyDrops.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IAuraMonitorService AuraMonitor => _lazyAuraMonitor.Value;

    private Thread? _currentScriptThread;
    private readonly object _threadLock = new();
    private readonly object _stateLock = new();
    private bool _stoppedByScript;
    private bool _runScriptStoppingBool;
    private readonly object _configuredLock = new();
    private readonly Dictionary<string, bool> _configured = new();
    private readonly object _refCacheLock = new();
    private readonly List<string> _refCache = new();
    private readonly ReaderWriterLockSlim _includedFilesLock = new();
    private readonly List<string> _includedFiles = new();
    private ScriptLoadContext? _currentLoadContext;

    [ObservableProperty]
    private bool _scriptRunning = false;

    [ObservableProperty]
    private bool _scriptPaused = false;

    public ManualResetEventSlim ScriptPauseEvent { get; } = new(true);

    partial void OnScriptPausedChanged(bool value)
    {
        if (value)
            ScriptPauseEvent.Reset();
        else
            ScriptPauseEvent.Set();
    }

    [ObservableProperty]
    private string _loadedScript = string.Empty;

    [ObservableProperty]
    private string _compiledScript = string.Empty;

    public IScriptOptionContainer? Config { get; set; }

    public string? OverrideStorage { get; set; }

    public bool SilentConfig { get; set; }

    public CancellationTokenSource? ScriptCts { get; private set; }

    public bool ShouldExit => ScriptCts?.IsCancellationRequested ?? false;

    public async Task<Exception?> StartScript()
    {
        lock (_threadLock)
        {
            if (ScriptRunning)
            {
                _logger.ScriptLog("Script already running.");
                return new Exception("Script already running.");
            }
            ScriptRunning = true;
        }

        try
        {
            await _lazyBot.Value.Auto.StopAsync();

            UnloadPreviousScript();

            string scriptContent = File.ReadAllText(LoadedScript);
            object? script = await Task.Run(() => Compile(scriptContent));

            LoadScriptConfig(script);

            bool needsConfig;
            lock (_configuredLock)
            {
                needsConfig = _configured.TryGetValue(Config!.Storage, out bool b) && !b;
            }

            ManualResetEventSlim scriptReady = new(false);

            Handlers.Clear();

            lock (_stateLock)
            {
                _runScriptStoppingBool = false;
            }

            Thread scriptThread = new(() =>
            {
                Exception? exception = null;
                ScriptCts = new();
                scriptReady.Set();

                try
                {
                    script?.GetType().GetMethod("ScriptMain")?.Invoke(script, new object[] { _lazyBot.Value });
                }
                catch (Exception e)
                {
                    Exception actualException = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;

                    bool stoppedByScript;
                    lock (_stateLock)
                    {
                        stoppedByScript = _stoppedByScript;
                    }

                    if ((actualException is not OperationCanceledException || !stoppedByScript) && (e is not TargetInvocationException || !stoppedByScript))
                    {
                        exception = actualException;
                        Trace.WriteLine($"Error while running script:\r\nMessage: {actualException.Message}\r\nStackTrace: {actualException.StackTrace}");

                        StrongReferenceMessenger.Default.Send<ScriptErrorMessage, int>(new(actualException), (int)MessageChannels.ScriptStatus);

                        lock (_stateLock)
                        {
                            _runScriptStoppingBool = true;
                        }
                    }
                }
                finally
                {
                    bool shouldSendStoppingMessage;
                    lock (_stateLock)
                    {
                        _stoppedByScript = false;
                        shouldSendStoppingMessage = _runScriptStoppingBool;
                    }

                    if (shouldSendStoppingMessage)
                    {
                        StrongReferenceMessenger.Default.Send<ScriptStoppingMessage, int>((int)MessageChannels.ScriptStatus);
                        try
                        {
                            Task<bool?> messageTask = Task.Run(async () => await StrongReferenceMessenger.Default.Send<ScriptStoppingRequestMessage, int>(new(exception), (int)MessageChannels.ScriptStatus));
                            if (messageTask.Wait(TimeSpan.FromSeconds(2)))
                            {
                                switch (messageTask.Result)
                                {
                                    case true:
                                        Trace.WriteLine("Script finished successfully.");
                                        break;

                                    case false:
                                        Trace.WriteLine("Script finished early or with errors.");
                                        break;

                                    default:
                                        break;
                                }
                            }
                            else
                            {
                                Trace.WriteLine("Script stopping message timed out.");
                            }
                        }
                        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Ignored Exception: {ex.Message}"); }
                    }

                    script = null;
                    Skills.Stop();
                    Drops.Stop();

                    AuraMonitor.StopMonitoring();
                    UnloadPreviousScript();
                    ScriptCts?.Dispose();
                    ScriptCts = null;
                    StrongReferenceMessenger.Default.Send<ScriptStoppedMessage, int>((int)MessageChannels.ScriptStatus);
                    ScriptRunning = false;
                }
            })
            {
                Name = "Script Thread",
                IsBackground = true
            };

            lock (_threadLock)
            {
                _currentScriptThread = scriptThread;
                scriptThread.Start();
            }

            StrongReferenceMessenger.Default.Send<ScriptStartedMessage, int>((int)MessageChannels.ScriptStatus);

            if (needsConfig)
            {
                if (SilentConfig)
                {
                    lock (_configuredLock)
                    {
                        if (Config != null)
                            _configured[Config.Storage] = true;
                    }
                    Config?.Save();
                }
                else
                {
                    _ = Task.Run(() =>
                    {
                        scriptReady.Wait();
                        Config!.Configure();
                        lock (_configuredLock)
                        {
                            _configured[Config!.Storage] = true;
                        }
                    });
                }
            }

            return null;
        }
        catch (Exception e)
        {
            lock (_threadLock)
            {
                ScriptRunning = false;
            }
            return e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e;
        }
    }

    public async Task RestartScriptAsync()
    {
        Trace.WriteLine("Restarting script");
        await StopScript(false);
        await Task.Run(async () =>
        {
            await Task.Delay(5000);
            await StartScript();
        });
    }

    public async ValueTask StopScript(bool runScriptStoppingEvent = true)
    {
        ScriptPaused = false;
        
        lock (_stateLock)
        {
            _runScriptStoppingBool = runScriptStoppingEvent;
            _stoppedByScript = true;
        }

        ScriptCts?.Cancel();

        if (Thread.CurrentThread.Name == "Script Thread")
        {
            ScriptCts?.Token.ThrowIfCancellationRequested();
            return;
        }

        await Wait.ForTrueAsync(() => ScriptCts == null, 30).ConfigureAwait(false);

        Thread? thread;
        lock (_threadLock)
        {
            thread = _currentScriptThread;
        }

        if (thread?.IsAlive == true)
        {
            await Task.Run(() =>
            {
                if (!thread.Join(TimeSpan.FromSeconds(5)))
                {
                    try
                    {
                        thread.Interrupt();
                        if (!thread.Join(TimeSpan.FromSeconds(3)))
                        {
                            // More aggressive termination attempts
                            try
                            {
                                // Mark the thread as background so it doesn't prevent app shutdown
                                if (!thread.IsBackground)
                                    thread.IsBackground = true;

                                // Give one final chance with a shorter timeout
                                if (!thread.Join(TimeSpan.FromSeconds(1)))
                                {
                                    _logger?.ScriptLog("Script thread is unresponsive and may continue running until process exit.");
                                }
                            }
                            catch (Exception termEx)
                            {
                                _logger?.ScriptLog($"Error during thread termination: {termEx.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.ScriptLog($"Error stopping script thread: {ex.Message}");
                    }
                }
            }).ConfigureAwait(false);
        }

        OnPropertyChanged(nameof(ScriptRunning));
    }

    [RequiresUnreferencedCode("This method may require code that cannot be statically analyzed for trimming. Use with caution.")]
    public object? Compile(string source)
    {
        CheckScriptVersionRequirement(source);

        Stopwatch sw = Stopwatch.StartNew();

        _includedFilesLock.EnterWriteLock();
        try
        {
            _includedFiles.Clear();
        }
        finally
        {
            _includedFilesLock.ExitWriteLock();
        }

        HashSet<string> references = GetReferences();
        string final = ProcessSources(source, LoadedScript, ref references);

        // Debug: Check if final source is empty or contains no classes (disabled for performance)
#if DEBUG_VERBOSE

#endif

        // Check if the processed source contains any class definitions
        if (string.IsNullOrWhiteSpace(final))
        {
            throw new ScriptCompileException($"Script file '{LoadedScript}' is empty after processing includes and references.", final);
        }

        // Use regex to check for class definitions more robustly
        if (!_accessibilityRegex.IsMatch(final))
        {
            throw new ScriptCompileException($"Script file '{LoadedScript}' contains no class definitions after processing includes and references. Scripts must contain at least one class.", final);
        }

        // Recursively discover all transitive includes
        DiscoverAllIncludes(references);

        ScriptLoadContext loadContext = new();
        lock (_stateLock)
        {
            _currentLoadContext = loadContext;
        }

        List<string> compiledIncludes = CompileIncludedFiles(references, loadContext);

        references.UnionWith(compiledIncludes);

        // Same reasoning as the include path: neutralize before hashing so the
        // main-script cache key reflects the compiled (neutralized) bytes, not the
        // raw source. No-op on Windows.
        final = Compiler.NeutralizeWindowsPlatformGates(final);

        int cacheHash;
        _includedFilesLock.EnterReadLock();
        try
        {
            cacheHash = ComputeCacheHash(final, new List<string>(_includedFiles));
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }
        CompiledScript = final;
        string scriptName = Path.GetFileNameWithoutExtension(LoadedScript);

        Compiler compiler = Ioc.Default.GetRequiredService<Compiler>();
        compiler.AddDefaultReferencesAndNamespaces();
        compiler.AllowReferencesInCode = true;

        if (references.Count > 0)
            compiler.AddAssemblies(references.ToArray());

        dynamic? assembly = compiler.CompileClass(final, cacheHash, loadContext, scriptName);

        sw.Stop();
        Trace.WriteLine($"Script compilation took {sw.ElapsedMilliseconds}ms.");

        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);

        return compiler.Error
            ? throw new ScriptCompileException(compiler.ErrorMessage, compiler.GeneratedClassCodeWithLineNumbers)
            : (object?)assembly;
    }

    private HashSet<string> GetReferences()
    {
        HashSet<string> references = new();

        lock (_refCacheLock)
        {
            if (_refCache.Count == 0 && Directory.Exists(ClientFileSources.SkuaPluginsDIR))
            {
                foreach (string file in Directory.EnumerateFiles(ClientFileSources.SkuaPluginsDIR, "*.dll"))
                {
                    string path = Path.Combine(ClientFileSources.SkuaDIR, file);
                    if (CanLoadAssembly(path))
                    {
                        _refCache.Add(path);
                        references.Add(path);
                    }
                }
            }
            else
            {
                references.UnionWith(_refCache);
            }
        }

        return references;
    }

    private static readonly object _includeDownloadLock = new();

    /// <summary>
    /// Self-heal a missing <c>//cs_include</c>: bot scripts pull in shared
    /// libraries (CoreBots.cs, CoreAdvanced.cs, …) that aren't fetched when you
    /// download a single script, so on a fresh install the include resolves to a
    /// file that doesn't exist and the whole script fails to compile
    /// ("CoreBots could not be found"). If <paramref name="includeLocal"/> is
    /// missing, look it up in the script repo (auqw/Scripts) and download it to
    /// <c>SkuaScriptsDIR</c> so it's available for the compile. Best-effort and
    /// deadlock-safe (runs the async fetch on the thread pool regardless of the
    /// calling context); a genuinely-missing include still surfaces as a compile
    /// error.
    /// </summary>
    private void EnsureIncludeDownloaded(string includeLocal)
    {
        try
        {
            IGetScriptsService? svc = Ioc.Default.GetService<IGetScriptsService>();
            if (svc is null)
                return;

            // The catalog (sizes/hashes) is what lets us tell "missing" from
            // "outdated". Load it once if it hasn't been fetched yet — needed the
            // first time any script runs on a fresh install.
            if (svc.Scripts.Count == 0)
            {
                lock (_includeDownloadLock)
                {
                    if (svc.Scripts.Count == 0)
                        Task.Run(() => svc.RefreshScriptsAsync(null, CancellationToken.None)).GetAwaiter().GetResult();
                }
            }

            string target = Path.GetFullPath(includeLocal);
            Skua.Core.Models.GitHub.ScriptInfo? info = svc.Scripts.FirstOrDefault(s =>
                string.Equals(Path.GetFullPath(s.LocalFile), target, StringComparison.OrdinalIgnoreCase));
            // Not a repo script (e.g. a purely local include) — leave whatever is on disk.
            if (info is null)
                return;

            // Download when missing, AND re-download when the local copy differs from
            // the published one (size/sha mismatch). The update path is what pulls a
            // stale shared library — e.g. an old CoreBots.cs whose Windows-version gate
            // predates the neutralizer — up to the current version, so scripts stop
            // breaking after an upstream change instead of silently reusing old code.
            if (info.Downloaded && !info.Outdated)
                return;

            lock (_includeDownloadLock)
            {
                if (info.Downloaded && !info.Outdated)
                    return;

                // DownloadScriptAsync creates the correct-case parent from the catalog
                // path — don't pre-create one from the (possibly wrong-cased) directive
                // path, or Linux ends up with an empty phantom dir (Good/BLOD vs BLoD).
                _logger.ScriptLog($"{(info.Downloaded ? "Updating" : "Downloading")} include '{info.FilePath}'...");
                Task.Run(() => svc.DownloadScriptAsync(info)).GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            _logger.ScriptLog($"Auto-download of include '{Path.GetFileName(includeLocal)}' failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolve a script path to the real file on disk, tolerating case differences.
    /// AQW's <c>//cs_include</c> / <c>//cs_ref</c> directives are authored on
    /// Windows, whose filesystem is case-insensitive, so their casing often
    /// disagrees with the actual repo path — e.g. a script includes
    /// <c>Scripts/Good/BLOD/CoreBLOD.cs</c> while the file is really
    /// <c>Good/BLoD/CoreBLOD.cs</c>. On Linux the exact path doesn't exist, the
    /// include silently drops, and the script fails to compile with CS0246 for the
    /// missing Core type. This walks each path segment doing a case-insensitive
    /// directory match so the real file is found. Returns the actual on-disk path,
    /// or null if nothing matches. An exact hit short-circuits, so it's free on
    /// Windows and for correctly-cased paths.
    /// </summary>
    private static string? ResolveCaseInsensitivePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (File.Exists(path))
            return path;

        try
        {
            string full = Path.GetFullPath(path);
            string root = Path.GetPathRoot(full) ?? Path.DirectorySeparatorChar.ToString();
            if (!Directory.Exists(root))
                return null;

            string relative = Path.GetRelativePath(root, full);
            string[] segments = relative.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            return ResolveSegments(root, segments, 0);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Backtracking helper for <see cref="ResolveCaseInsensitivePath"/>. At each
    /// segment it tries the exact-case entry first, then every OTHER entry that
    /// matches case-insensitively, recursing into each until the full path lands on
    /// a real file. Backtracking matters because old builds left empty phantom
    /// directories in the wrong case (e.g. an empty <c>Good/BLOD/</c> beside the real
    /// <c>Good/BLoD/</c>): a non-backtracking walk descends into the empty one, fails
    /// to find the file, and gives up. This tries the sibling and finds it.
    /// </summary>
    private static string? ResolveSegments(string current, string[] segments, int index)
    {
        if (index == segments.Length)
            return File.Exists(current) ? current : null;

        string seg = segments[index];
        bool isLast = index == segments.Length - 1;

        // Exact-case first: fast, and the right answer when both cases exist on disk.
        string exact = Path.Combine(current, seg);
        if (isLast)
        {
            if (File.Exists(exact))
                return exact;
        }
        else if (Directory.Exists(exact))
        {
            string? hit = ResolveSegments(exact, segments, index + 1);
            if (hit is not null)
                return hit;
        }

        IEnumerable<string> entries;
        try
        {
            entries = isLast
                ? Directory.EnumerateFiles(current)
                : Directory.EnumerateDirectories(current);
        }
        catch
        {
            return null;
        }

        foreach (string entry in entries)
        {
            if (string.Equals(entry, exact, StringComparison.Ordinal))
                continue; // already tried the exact-case candidate above
            if (!string.Equals(Path.GetFileName(entry), seg, StringComparison.OrdinalIgnoreCase))
                continue;

            if (isLast)
            {
                if (File.Exists(entry))
                    return entry;
            }
            else
            {
                string? hit = ResolveSegments(entry, segments, index + 1);
                if (hit is not null)
                    return hit;
            }
        }

        return null;
    }

    private string ProcessSources(string source, string currentFile, ref HashSet<string> references)
    {
        int actualLineCount = source.AsSpan().Count('\n') + 1;
        Span<Range> lineRanges = actualLineCount <= 1024 ? stackalloc Range[actualLineCount] : new Range[actualLineCount];
        int lineCount = source.AsSpan().Split(lineRanges, '\n');

        List<string> linesToRemove = new();
        List<string> filesToInclude = new();
        List<string> foundRefs = new();
        ReadOnlySpan<char> sourceSpan = source.AsSpan();

        for (int i = 0; i < lineCount; i++)
        {
            ReadOnlySpan<char> line = sourceSpan[lineRanges[i]].Trim();

            if (!line.StartsWith("//cs_"))
                continue;

            string lineStr = new(line);
            string[] parts = lineStr.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            string cmd = parts[0][5..];
            switch (cmd)
            {
                case "ref":
                    string refPath = parts[1];
                    string refLocal = Path.Combine(ClientFileSources.SkuaScriptsDIR, refPath.Replace("Scripts/", ""));
                    string currentDirRef = string.IsNullOrEmpty(currentFile) ? "" : Path.Combine(Path.GetDirectoryName(currentFile) ?? "", refPath);
                    
                    string? resolvedRef = ResolveCaseInsensitivePath(currentDirRef)
                        ?? ResolveCaseInsensitivePath(refLocal)
                        ?? ResolveCaseInsensitivePath(refPath);
                    if (resolvedRef is not null)
                    {
                        references.Add(resolvedRef);
                        foundRefs.Add(resolvedRef);
                    }
                    break;

                case "include":
                    string includePath = parts[1];
                    string includeLocal = Path.Combine(ClientFileSources.SkuaScriptsDIR, includePath.Replace("Scripts/", ""));
                    string currentDirInclude = string.IsNullOrEmpty(currentFile) ? "" : Path.Combine(Path.GetDirectoryName(currentFile) ?? "", includePath);

                    string? resolvedInclude = ResolveCaseInsensitivePath(currentDirInclude);
                    if (resolvedInclude is null)
                    {
                        EnsureIncludeDownloaded(includeLocal); // fetch shared libs (CoreBots, …) if missing/outdated
                        // Resolve case-insensitively: directives are Windows-cased and
                        // often disagree with the real path (e.g. BLOD vs BLoD).
                        resolvedInclude = ResolveCaseInsensitivePath(includeLocal)
                            ?? ResolveCaseInsensitivePath(includePath);
                    }
                    if (resolvedInclude is not null)
                        filesToInclude.Add(resolvedInclude);
                    break;
            }
            linesToRemove.Add(lineStr);
        }

        if (filesToInclude.Count > 0)
        {
            _includedFilesLock.EnterWriteLock();
            try
            {
                _includedFiles.AddRange(filesToInclude);
            }
            finally
            {
                _includedFilesLock.ExitWriteLock();
            }
        }

        string resultStr;
        if (linesToRemove.Count == 0)
        {
            resultStr = source.Trim();
        }
        else
        {
            HashSet<string> linesToRemoveSet = new(linesToRemove);
            string[] sourceLines = source.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            List<string> filteredLines = new();

            foreach (string sourceLine in sourceLines)
            {
                string trimmedLine = sourceLine.Trim();
                if (!linesToRemoveSet.Contains(trimmedLine))
                {
                    filteredLines.Add(sourceLine);
                }
            }
            resultStr = string.Join(Environment.NewLine, filteredLines).Trim();
        }

        return resultStr;
    }

    private void DiscoverAllIncludes(HashSet<string> references)
    {
        bool foundNewFiles = true;
        int iterationCount = 0;
#if DEBUG_VERBOSE

#endif

        // Keep iterating until no new files are found
        while (foundNewFiles && iterationCount < 10) // Safety limit
        {
            foundNewFiles = false;
            iterationCount++;
#if DEBUG_VERBOSE

#endif

            List<string> currentFiles;
            _includedFilesLock.EnterReadLock();
            try
            {
                currentFiles = new List<string>(_includedFiles);
            }
            finally
            {
                _includedFilesLock.ExitReadLock();
            }

            foreach (string currentFile in currentFiles)
            {
                if (!File.Exists(currentFile))
                {

                    continue;
                }

                try
                {
                    string fileContent = File.ReadAllText(currentFile);
                    List<string> newIncludes = ExtractIncludeDirectivesFromSource(fileContent, currentFile, references);


                    if (newIncludes.Count > 0)
                    {
                        List<string> filesToAdd = new();
                        foreach (string include in newIncludes)
                        {
                            if (File.Exists(include))
                                filesToAdd.Add(include);
                        }

                        if (filesToAdd.Count > 0)
                        {
                            _includedFilesLock.EnterWriteLock();
                            try
                            {
                                HashSet<string> existingNames = new(StringComparer.OrdinalIgnoreCase);
                                foreach (string existing in _includedFiles)
                                {
                                    existingNames.Add(Path.GetFileName(existing));
                                }

                                foreach (string include in filesToAdd)
                                {
                                    if (existingNames.Add(Path.GetFileName(include)))
                                    {
                                        _includedFiles.Add(include);
                                        foundNewFiles = true;
                                    }
                                }
                            }
                            finally
                            {
                                _includedFilesLock.ExitWriteLock();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {

                }
            }
        }

    }

    private List<string> ExtractIncludeDirectivesFromSource(string source, string currentFile, HashSet<string> references)
    {
        List<string> includes = new();
        ReadOnlySpan<char> sourceSpan = source.AsSpan();

        int start = 0;
        int newlinePos;
        while ((newlinePos = sourceSpan[start..].IndexOf('\n')) >= 0)
        {
            ReadOnlySpan<char> line = sourceSpan[start..(start + newlinePos)].Trim();

            if (line.StartsWith("using"))
                break;

            if (line.StartsWith("//cs_"))
            {
                string lineStr = new(line);
                string[] parts = lineStr.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string cmd = parts[0][5..];
                    switch (cmd)
                    {
                        case "ref":
                            string refPath = parts[1];
                            string refLocal = Path.Combine(ClientFileSources.SkuaScriptsDIR, refPath.Replace("Scripts/", ""));
                            string currentDirRef = string.IsNullOrEmpty(currentFile) ? "" : Path.Combine(Path.GetDirectoryName(currentFile) ?? "", refPath);
                            
                            string? resolvedRef = ResolveCaseInsensitivePath(currentDirRef)
                                ?? ResolveCaseInsensitivePath(refLocal)
                                ?? ResolveCaseInsensitivePath(refPath);
                            if (resolvedRef is not null)
                                references.Add(resolvedRef);
                            break;

                        case "include":
                            string includePath = parts[1];
                            string includeLocal = Path.Combine(ClientFileSources.SkuaScriptsDIR, includePath.Replace("Scripts/", ""));
                            string currentDirInclude = string.IsNullOrEmpty(currentFile) ? "" : Path.Combine(Path.GetDirectoryName(currentFile) ?? "", includePath);

                            string? resolvedInclude = ResolveCaseInsensitivePath(currentDirInclude);
                            if (resolvedInclude is null)
                            {
                                EnsureIncludeDownloaded(includeLocal); // fetch transitive shared libs if missing/outdated
                                // Directives are Windows-cased and may not match the real
                                // path on a case-sensitive filesystem (e.g. BLOD vs BLoD).
                                resolvedInclude = ResolveCaseInsensitivePath(includeLocal)
                                    ?? ResolveCaseInsensitivePath(includePath);
                            }
                            if (resolvedInclude is not null)
                                includes.Add(resolvedInclude);
                            break;
                    }
                }
            }

            start += newlinePos + 1;
        }

        return includes;
    }

    public void LoadScriptConfig(object? script)
    {
        if (script is null)
            return;

        IScriptOptionContainer opts = Config = Ioc.Default.GetRequiredService<IScriptOptionContainer>();
        Type t = script.GetType();
        FieldInfo? storageField = t.GetField("OptionsStorage");
        FieldInfo? optsField = t.GetField("Options");
        FieldInfo? multiOptsField = t.GetField("MultiOptions");
        FieldInfo? dontPreconfField = t.GetField("DontPreconfigure");
        if (multiOptsField is not null)
        {
            string[] optFieldNames = (string[])multiOptsField.GetValue(script)!;
            List<FieldInfo> multiOpts = new(optFieldNames.Length);
            foreach (string optField in optFieldNames)
            {
                FieldInfo? field = t.GetField(optField);
                if (field != null)
                    multiOpts.Add(field);
            }
            foreach (FieldInfo opt in multiOpts)
            {
                List<IOption> parsedOpt = (List<IOption>)opt.GetValue(script)!;
                parsedOpt.ForEach(o => o.Category = opt.Name.Replace('_', ' '));
                opts.MultipleOptions.Add(opt.Name, parsedOpt);
            }
        }
        if (optsField is not null)
            opts.Options.AddRange((List<IOption>)optsField.GetValue(script)!);
        if (storageField is not null)
            opts.Storage = (string)storageField.GetValue(script)!;

        if (!string.IsNullOrEmpty(OverrideStorage))
            opts.Storage = OverrideStorage;
        lock (_configuredLock)
        {
            if (dontPreconfField is not null)
                _configured[opts.Storage] = (bool)dontPreconfField.GetValue(script)!;
            else if (optsField is not null)
                _configured[opts.Storage] = false;
        }

        opts.SetDefaults();
        opts.Load();
    }

    private static bool CanLoadAssembly(string path)
    {
        try
        {
            AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static int ComputeCacheHash(string source, List<string> includedFiles)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        byte[] sourceHash = sha256.ComputeHash(sourceBytes);
        using MemoryStream ms = new();
        ms.Write(sourceHash, 0, sourceHash.Length);

        foreach (string file in includedFiles.OrderBy(f => f))
        {
            if (File.Exists(file))
            {
                byte[] pathBytes = Encoding.UTF8.GetBytes(file);
                ms.Write(pathBytes, 0, pathBytes.Length);

                long ticks = File.GetLastWriteTimeUtc(file).Ticks;
                byte[] ticksBytes = BitConverter.GetBytes(ticks);
                ms.Write(ticksBytes, 0, ticksBytes.Length);
            }
        }

        byte[] combinedHash = sha256.ComputeHash(ms.ToArray());
        return BitConverter.ToInt32(combinedHash, 0);
    }

    private void CheckScriptVersionRequirement(string source)
    {
        // Check disabled to prevent 'update your client' popups.
        /*
        Match match = _versionRegex.Match(source);
        if (match.Success)
        {
            string requiredVersionStr = match.Groups[1].Value;
            Version? currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            if (Version.TryParse(requiredVersionStr, out Version? requiredVersion) && currentVersion != null && currentVersion < requiredVersion)
            {
                throw new ScriptVersionException(requiredVersionStr, currentVersion.ToString());
            }
        }
        */
    }

    private List<string> CompileIncludedFiles(HashSet<string> references, ScriptLoadContext loadContext)
    {
        ConcurrentDictionary<string, string> compiledPaths = new();
        ConcurrentDictionary<string, bool> compilationCompleted = new();
        object lockObj = new();

        ConcurrentDictionary<string, List<string>> dependencyGraph = new();
        ConcurrentDictionary<string, (string source, string fileName, int hash, string cachePath)> fileInfoCache = new();

        string cacheDir = _cacheScriptsDir;

        ConcurrentBag<string> validCachedFiles = new();

#if DEBUG_VERBOSE

#endif

        Dictionary<string, string> filenameLookup = new(StringComparer.OrdinalIgnoreCase);
        List<string> includedFilesSnapshot;
        _includedFilesLock.EnterReadLock();
        try
        {
            includedFilesSnapshot = new List<string>(_includedFiles);
            foreach (string file in _includedFiles)
            {
                string filename = Path.GetFileName(file);
                if (!filenameLookup.ContainsKey(filename))
                    filenameLookup[filename] = file;
            }
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }

        Parallel.ForEach(includedFilesSnapshot, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, includedFile =>
        {
            // Neutralize Windows-platform gates BEFORE hashing so the disk-cache key
            // reflects the bytes we actually compile. Otherwise the key is the raw
            // source (unchanged by the transform) and a stale non-neutralized DLL —
            // e.g. an old CoreBots.dll with its Windows-10 gate live — would be reused
            // forever, re-firing "Skua requires Windows 10" on Linux. On Windows this
            // is a no-op, so the cache key is identical to before there.
            string includeSource = Compiler.NeutralizeWindowsPlatformGates(File.ReadAllText(includedFile));
            CheckScriptVersionRequirement(includeSource);
            List<string> deps = ExtractIncludeDependencies(includeSource, includedFile);
            List<string> normalizedDeps = new();
            foreach (string dep in deps)
            {
                if (filenameLookup.TryGetValue(Path.GetFileName(dep), out string? matchingFile))
                {
                    normalizedDeps.Add(matchingFile);
                }
                else
                {
                    bool containsDep;
                    _includedFilesLock.EnterReadLock();
                    try
                    {
                        containsDep = _includedFiles.Contains(dep);
                    }
                    finally
                    {
                        _includedFilesLock.ExitReadLock();
                    }
                    if (containsDep)
                    {
                        normalizedDeps.Add(dep);
                    }
                }
            }
            dependencyGraph[includedFile] = normalizedDeps;

            string includeFileName = Path.GetFileNameWithoutExtension(includedFile);
            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(includeSource));
            int includeHash = BitConverter.ToInt32(hashBytes, 0);
            string compiledPath = Path.Combine(cacheDir, $"{includeHash}-{includeFileName}.dll");

            if (File.Exists(compiledPath))
            {
                try
                {
                    AssemblyName.GetAssemblyName(compiledPath);
                    validCachedFiles.Add(includedFile);
                    compiledPaths[includedFile] = compiledPath;
                    compilationCompleted[includedFile] = true;
                    Compiler.RegisterCompiled(includeHash, compiledPath);
                    fileInfoCache[includedFile] = (string.Empty, includeFileName, includeHash, compiledPath);
                    // A cache HIT skips CompileIncludeRecursive entirely, so without
                    // this the cached DLL is never loaded into THIS run's load
                    // context — and a script referencing the include's types crashes
                    // at runtime with FileNotFoundException (the resolver net in
                    // ScriptLoadContext.Load is the fallback, not the plan).
                    if (!Compiler.IsLoaded(compiledPath))
                    {
                        try
                        {
                            using FileStream stream = File.OpenRead(compiledPath);
                            Assembly loadedAssembly = loadContext.LoadFromStream(stream);
                            Compiler.RegisterLoaded(compiledPath, loadedAssembly);
                        }
                        catch
                        {
                        }
                    }
                    return;
                }
                catch
                {
                    try
                    {
                        File.Delete(compiledPath);
                    }
                    catch
                    {
                    }
                }
            }

            fileInfoCache[includedFile] = (includeSource, includeFileName, includeHash, compiledPath);
        });

        HashSet<string> processed = new(validCachedFiles);
        HashSet<string> includedFilesSet;
        _includedFilesLock.EnterReadLock();
        try
        {
            includedFilesSet = new HashSet<string>(_includedFiles);
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }

#if DEBUG_VERBOSE

        foreach (var kvp in dependencyGraph)
        {
            string fileName = Path.GetFileName(kvp.Key);
            string deps = kvp.Value.Count > 0 ? string.Join(", ", kvp.Value.Select(Path.GetFileName)) : "[none]";

        }
#endif

        HashSet<string> allReferencedFiles;
        _includedFilesLock.EnterReadLock();
        try
        {
            allReferencedFiles = new HashSet<string>(_includedFiles);
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }

        List<string> newlyAddedFiles = new();
        foreach (KeyValuePair<string, List<string>> kvp in dependencyGraph)
        {
            foreach (string dep in kvp.Value)
            {
                if (File.Exists(dep) && !allReferencedFiles.Contains(dep))
                {
                    allReferencedFiles.Add(dep);
                    newlyAddedFiles.Add(dep);
                }
            }
        }

        if (newlyAddedFiles.Count > 0)
        {
            _includedFilesLock.EnterWriteLock();
            try
            {
                _includedFiles.AddRange(newlyAddedFiles);
            }
            finally
            {
                _includedFilesLock.ExitWriteLock();
            }
        }

        foreach (string newFile in newlyAddedFiles)
        {
            // Neutralize before hashing here too (same reasoning as the direct-include
            // pass), so a transitively-discovered gated include hashes on its compiled
            // bytes and can't collide with a stale non-neutralized DLL. No-op on Windows.
            string includeSource = Compiler.NeutralizeWindowsPlatformGates(File.ReadAllText(newFile));
            CheckScriptVersionRequirement(includeSource);
            string includeFileName = Path.GetFileNameWithoutExtension(newFile);
            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(includeSource));
            int includeHash = BitConverter.ToInt32(hashBytes, 0);
            string compiledPath = Path.Combine(cacheDir, $"{includeHash}-{includeFileName}.dll");

            if (File.Exists(compiledPath))
            {
                try
                {
                    AssemblyName.GetAssemblyName(compiledPath);
                    validCachedFiles.Add(newFile);
                    compiledPaths[newFile] = compiledPath;
                    compilationCompleted[newFile] = true;
                    Compiler.RegisterCompiled(includeHash, compiledPath);
                    fileInfoCache[newFile] = (string.Empty, includeFileName, includeHash, compiledPath);
                    // Same as the primary cache-hit branch: load the cached DLL into
                    // THIS run's load context or runtime type references crash.
                    if (!Compiler.IsLoaded(compiledPath))
                    {
                        try
                        {
                            using FileStream stream = File.OpenRead(compiledPath);
                            Assembly loadedAssembly = loadContext.LoadFromStream(stream);
                            Compiler.RegisterLoaded(compiledPath, loadedAssembly);
                        }
                        catch
                        {
                        }
                    }
                }
                catch
                {
                    fileInfoCache[newFile] = (includeSource, includeFileName, includeHash, compiledPath);
                }
            }
            else
            {
                fileInfoCache[newFile] = (includeSource, includeFileName, includeHash, compiledPath);
            }
        }

        List<string> orderedFiles;
        _includedFilesLock.EnterReadLock();
        try
        {
            orderedFiles = SortByDependencyOrder(dependencyGraph, new List<string>(_includedFiles));
        }
        finally
        {
            _includedFilesLock.ExitReadLock();
        }

#if DEBUG_VERBOSE

        foreach (string file in orderedFiles)
        {

        }
#endif

        HashSet<string> compiledFiles = new(validCachedFiles);

        foreach (string cachedFile in validCachedFiles)
        {
            compilationCompleted[cachedFile] = true;
        }

        while (compiledFiles.Count < orderedFiles.Count)
        {
            List<string> readyBatch = new();

            foreach (string file in orderedFiles)
            {
                if (compiledFiles.Contains(file))
                    continue;

                bool allDepsReady = true;
                if (dependencyGraph.TryGetValue(file, out List<string>? fileDeps))
                {
                    foreach (string dep in fileDeps)
                    {
                        if (!compilationCompleted.GetValueOrDefault(dep, false))
                        {
                            allDepsReady = false;
                            break;
                        }
                    }
                }

                if (allDepsReady)
                    readyBatch.Add(file);
            }

            if (readyBatch.Count == 0)
                break;

#if DEBUG_VERBOSE

#endif

            Parallel.ForEach(readyBatch, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
            {
                try
                {
                    CompileIncludeRecursive(file, references, loadContext, compiledPaths, compilationCompleted, lockObj, fileInfoCache);
                }
                catch (Exception ex)
                {

                    throw;
                }
            });

            foreach (string file in readyBatch)
                compiledFiles.Add(file);
        }

        fileInfoCache.Clear();

        return compiledPaths.Values.ToList();
    }


    private void CompileIncludeRecursive(
        string includedFile,
        HashSet<string> references,
        ScriptLoadContext loadContext,
        ConcurrentDictionary<string, string> compiledPaths,
        ConcurrentDictionary<string, bool> compilationCompleted,
        object lockObj,
        ConcurrentDictionary<string, (string source, string fileName, int hash, string cachePath)> fileInfoCache)
    {
        if (compiledPaths.ContainsKey(includedFile))
            return;

        try
        {
            (string source, string fileName, int hash, string cachePath) info = fileInfoCache[includedFile];
            string includeSource = info.source;
            string includeFileName = info.fileName;
            int includeHash = info.hash;
            string compiledPath = info.cachePath;

            HashSet<string> includeReferences = new(references);

            lock (lockObj)
            {
                foreach (KeyValuePair<string, string> kvp in compiledPaths)
                {
                    if (File.Exists(kvp.Value))
                    {
                        try
                        {
                            AssemblyName.GetAssemblyName(kvp.Value);
                            includeReferences.Add(kvp.Value);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            string processedInclude = ProcessIncludeDirectives(includeSource, ref includeReferences);

            Compiler includeCompiler = Ioc.Default.GetRequiredService<Compiler>();
            includeCompiler.AddDefaultReferencesAndNamespaces();
            includeCompiler.AllowReferencesInCode = true;

            if (includeReferences.Count > 0)
                includeCompiler.AddAssemblies(includeReferences.ToArray());

            dynamic? assembly = includeCompiler.CompileClass(processedInclude, includeHash, loadContext, includeFileName);

            if (includeCompiler.Error)
            {
                throw new ScriptCompileException(
                    $"Error compiling included file '{includedFile}':\n{includeCompiler.ErrorMessage}",
                    includeCompiler.GeneratedClassCodeWithLineNumbers);
            }

            if (assembly != null && File.Exists(compiledPath))
            {
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        using FileStream fs = new(compiledPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        if (fs.Length > 0)
                            break;
                    }
                    catch
                    {
                        Thread.Sleep(10);
                    }
                }

                lock (lockObj)
                {
                    compiledPaths[includedFile] = compiledPath;
                    compilationCompleted[includedFile] = true;

                    if (!Compiler.IsLoaded(compiledPath))
                    {
                        try
                        {
                            using FileStream stream = File.OpenRead(compiledPath);
                            Assembly loadedAssembly = loadContext.LoadFromStream(stream);
                            Compiler.RegisterLoaded(compiledPath, loadedAssembly);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not ScriptCompileException)
        {
            throw new ScriptCompileException($"Failed to compile included file '{includedFile}': {ex.Message}", string.Empty);
        }
    }

    private List<string> SortByDependencyOrder(ConcurrentDictionary<string, List<string>> dependencyGraph, List<string> files)
    {
        List<string> result = new();
        HashSet<string> visited = new();
        HashSet<string> visiting = new();

        foreach (string file in files)
        {
            if (!visited.Contains(file))
            {
                VisitDependencies(file, dependencyGraph, visited, visiting, result, files);
            }
        }

        return result;
    }

    private void VisitDependencies(string file, ConcurrentDictionary<string, List<string>> dependencyGraph,
        HashSet<string> visited, HashSet<string> visiting, List<string> result, List<string> allFiles)
    {
        if (visiting.Contains(file))
            return;

        if (visited.Contains(file))
            return;

        visiting.Add(file);

        // Visit all dependencies first
        if (dependencyGraph.TryGetValue(file, out List<string>? dependencies))
        {
            foreach (string dependency in dependencies)
            {
                if (allFiles.Contains(dependency))
                {
                    VisitDependencies(dependency, dependencyGraph, visited, visiting, result, allFiles);
                }
            }
        }

        visiting.Remove(file);
        visited.Add(file);
        result.Add(file);
    }

    private List<string> GetCompilationOrder(ConcurrentDictionary<string, List<string>> dependencyGraph, List<string> files)
    {
        List<string> result = new();
        HashSet<string> visited = new();
        HashSet<string> visiting = new();

        foreach (string file in files)
        {
            if (!visited.Contains(file))
            {
                VisitFile(file, dependencyGraph, visited, visiting, result, files);
            }
        }

        return result;
    }

    private void VisitFile(string file, ConcurrentDictionary<string, List<string>> dependencyGraph,
        HashSet<string> visited, HashSet<string> visiting, List<string> result, List<string> allFiles)
    {
        if (visiting.Contains(file))
            return;

        if (visited.Contains(file))
            return;

        visiting.Add(file);

        if (dependencyGraph.TryGetValue(file, out List<string>? deps))
        {
            foreach (string dep in deps)
            {
                if (allFiles.Contains(dep) && !visited.Contains(dep))
                {
                    VisitFile(dep, dependencyGraph, visited, visiting, result, allFiles);
                }
            }
        }

        visiting.Remove(file);
        visited.Add(file);
        result.Add(file);
    }

    private void GetTransitiveDependencies(string file, ConcurrentDictionary<string, List<string>> dependencyGraph, HashSet<string> allDeps)
    {
        if (!dependencyGraph.TryGetValue(file, out List<string>? directDeps))
            return;

        foreach (string dep in directDeps)
        {
            if (allDeps.Add(dep))
            {
                GetTransitiveDependencies(dep, dependencyGraph, allDeps);
            }
        }
    }

    private List<string> ExtractIncludeDependencies(string source, string currentFile)
    {
        List<string> dependencies = new();
        ReadOnlySpan<char> sourceSpan = source.AsSpan();

        int start = 0;
        int newlinePos;
        while ((newlinePos = sourceSpan[start..].IndexOf('\n')) >= 0)
        {
            ReadOnlySpan<char> line = sourceSpan[start..(start + newlinePos)].Trim();

            if (line.StartsWith("using"))
                break;

            if (line.StartsWith("//cs_include "))
            {
                string lineStr = new(line);
                string[] parts = lineStr.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    string includePath = parts[1];
                    string localPath = Path.Combine(ClientFileSources.SkuaScriptsDIR, includePath.Replace("Scripts/", ""));
                    string currentDirInclude = string.IsNullOrEmpty(currentFile) ? "" : Path.Combine(Path.GetDirectoryName(currentFile) ?? "", includePath);

                    // Case-insensitive resolve so the dependency graph records the edge
                    // even when the directive's case differs from the real path (BLOD vs
                    // BLoD); otherwise the compile order can put a dependent before its
                    // Core and fail with CS0246 on a case-sensitive filesystem.
                    string? resolvedDep = ResolveCaseInsensitivePath(currentDirInclude)
                        ?? ResolveCaseInsensitivePath(localPath)
                        ?? ResolveCaseInsensitivePath(includePath);
                    if (resolvedDep is not null)
                        dependencies.Add(resolvedDep);
                }
            }

            start += newlinePos + 1;
        }

        return dependencies;
    }


    private string ProcessIncludeDirectives(string source, ref HashSet<string> references)
    {
        List<string> linesToRemove = new();
        ReadOnlySpan<char> sourceSpan = source.AsSpan();

        int start = 0;
        int newlinePos;
        while ((newlinePos = sourceSpan[start..].IndexOf('\n')) >= 0)
        {
            ReadOnlySpan<char> line = sourceSpan[start..(start + newlinePos)].Trim();

            if (line.StartsWith("using"))
                break;

            if (!line.StartsWith("//cs_"))
            {
                start += newlinePos + 1;
                continue;
            }

            string lineStr = new(line);
            string[] parts = lineStr.Split((char[])null!, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                start += newlinePos + 1;
                continue;
            }

            string cmd = parts[0][5..];
            if (cmd == "ref")
            {
                string local = Path.Combine(ClientFileSources.SkuaScriptsDIR, parts[1].Replace("Scripts/", ""));
                string? resolvedRef = ResolveCaseInsensitivePath(local) ?? ResolveCaseInsensitivePath(parts[1]);
                if (resolvedRef is not null)
                    references.Add(resolvedRef);
            }

            linesToRemove.Add(new string(sourceSpan[start..(start + newlinePos + 1)]));
            start += newlinePos + 1;
        }

        if (linesToRemove.Count == 0)
            return source.Trim();

        StringBuilder sb = new(source);
        foreach (string lineToRemove in linesToRemove)
        {
            sb.Replace(lineToRemove, "");
        }
        return sb.ToString().Trim();
    }

    private void UnloadPreviousScript()
    {
        ScriptLoadContext? context;
        lock (_stateLock)
        {
            context = _currentLoadContext;
            _currentLoadContext = null;
        }

        Compiler.ClearSessionRegistries();

        if (context is null)
            return;

        _ = Task.Run(() =>
        {
            try
            {
                WeakReference weak = new(context);
                context.Unload();

                for (int i = 0; i < 3 && weak.IsAlive; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);
                }
            }
            catch
            {
            }
        });
    }

    public void SetLoadedScript(string path)
    {
        LoadedScript = path;
    }

    public void Dispose()
    {
        Thread? thread;
        lock (_threadLock)
        {
            thread = _currentScriptThread;
        }

        if (thread?.IsAlive == true)
        {
            ScriptCts?.Cancel();
            if (!thread.Join(TimeSpan.FromSeconds(5)))
            {
                _logger?.ScriptLog("Script thread did not exit during disposal.");
            }
        }
        ScriptCts?.Dispose();
        _includedFilesLock?.Dispose();
    }
}
