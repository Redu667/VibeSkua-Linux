using System;
using System.IO;
using System.Linq;

namespace Skua.Avalonia.Services;

/// <summary>
/// Per-launch log routing. Previously every process (manager + each client)
/// appended forever to the same four files in ~/.config/Skua, so they grew
/// without bound (hundreds of thousands of lines). Now each launch gets its own
/// timestamped set of files under ~/.config/Skua/logs/, named by role + date +
/// time + pid, e.g.:
///
///   logs/manager-2026-07-18_031422-12345.client.log
///   logs/client-Bob-2026-07-18_031422-12345.game.log
///
/// The paths are published as environment variables so the native Rust bridge
/// (game + ruffle logs) writes into the same per-launch set. Old logs are
/// pruned on startup. Call <see cref="Init"/> once, first thing in Main.
/// </summary>
public static class SessionLog
{
    public const string DirVar = "SKUA_LOG_DIR";
    public const string ClientVar = "SKUA_CLIENT_LOG";
    public const string GameVar = "SKUA_GAME_LOG";
    public const string RuffleVar = "SKUA_RUFFLE_LOG";
    public const string CrashVar = "SKUA_CRASH_LOG";

    private const int RetentionDays = 14;

    /// <summary>Full path to this launch's C#-side client log (or null before Init).</summary>
    public static string? ClientLogPath { get; private set; }

    /// <summary>Full path to this launch's crash log (or null before Init).</summary>
    public static string? CrashLogPath { get; private set; }

    public static void Init(string[] args, DateTime nowLocal)
    {
        try
        {
            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua", "logs");
            Directory.CreateDirectory(logsDir);

            string role = HasFlag(args, "--client") ? "client" : "manager";
            string? instance = GetOption(args, "--instance") ?? GetOption(args, "--account");
            string label = string.IsNullOrWhiteSpace(instance) ? role : $"{role}-{Sanitize(instance!)}";
            string stem = $"{label}-{nowLocal:yyyy-MM-dd_HHmmss}-{Environment.ProcessId}";

            string client = Path.Combine(logsDir, $"{stem}.client.log");
            string game = Path.Combine(logsDir, $"{stem}.game.log");
            string ruffle = Path.Combine(logsDir, $"{stem}.ruffle.log");
            string crash = Path.Combine(logsDir, $"{stem}.crash.log");

            // Publish for this process (C#) and the native bridge (Rust reads
            // these via std::env::var; SetEnvironmentVariable -> setenv on Linux).
            Environment.SetEnvironmentVariable(DirVar, logsDir);
            Environment.SetEnvironmentVariable(ClientVar, client);
            Environment.SetEnvironmentVariable(GameVar, game);
            Environment.SetEnvironmentVariable(RuffleVar, ruffle);
            Environment.SetEnvironmentVariable(CrashVar, crash);

            ClientLogPath = client;
            CrashLogPath = crash;

            PruneOldLogs(logsDir, nowLocal);
        }
        catch
        {
            // Logging setup must never block startup. Writers fall back to the
            // legacy single-file paths when the env vars are unset.
        }
    }

    /// <summary>Resolve the client-log path (this launch's file if Init ran,
    /// else the legacy single file so console/tests still log).</summary>
    public static string ResolveClientLog()
        => Environment.GetEnvironmentVariable(ClientVar)
           ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua", "vibeskua-client.log");

    private static void PruneOldLogs(string logsDir, DateTime nowLocal)
    {
        try
        {
            DateTime cutoff = nowLocal.AddDays(-RetentionDays);
            foreach (string file in Directory.EnumerateFiles(logsDir, "*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
                catch { }
            }
        }
        catch { }
    }

    private static string Sanitize(string s)
        => new string(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

    private static bool HasFlag(string[] args, string flag)
        => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] args, string option)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
