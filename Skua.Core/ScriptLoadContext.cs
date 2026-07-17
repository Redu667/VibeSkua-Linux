using Skua.Core.Models;
using System.Reflection;
using System.Runtime.Loader;

namespace Skua.Core;

public class ScriptLoadContext : AssemblyLoadContext
{
    // MUST match the compiler's versioned cache dir. This resolver is the
    // runtime safety net for inter-script assembly references (a script's
    // ScriptMain touching a type from an included script triggers a by-name
    // load); when the cache moved to Cached-Scripts-{version} this still probed
    // the dead unversioned dir, and scripts crashed at runtime with
    // FileNotFoundException for their own includes (e.g. '1InTheFiendsShadow').
    private static readonly string _cacheDirectory =
        Path.Combine(ClientFileSources.SkuaScriptsDIR, $"Cached-Scripts-{Compiler.CacheVersion}");
    private volatile bool _isUnloading;

    public ScriptLoadContext() : base(isCollectible: true)
    {
        Unloading += context => _isUnloading = true;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
            return null;

        if (!Directory.Exists(_cacheDirectory))
            return null;

        if (_isUnloading)
            return null;

        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch
        {
        }

        if (_isUnloading)
            return null;

        string[] matchingFiles = Directory.GetFiles(_cacheDirectory, $"*-{assemblyName.Name}.dll");

        if (matchingFiles.Length > 0)
        {
            string latestFile = matchingFiles.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();

            if (!File.Exists(latestFile))
                return null;

            try
            {
                using FileStream stream = File.OpenRead(latestFile);
                return LoadFromStream(stream);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}