using System.Reflection;
using Skua.Core;
using Xunit;

namespace Skua.Avalonia.Tests;

/// <summary>
/// The compiled-script cache folder (<c>Cached-Scripts-{CacheVersion}</c>) must be
/// scoped to the running assembly version. A cached include DLL embeds a hard
/// reference to the exact <c>Skua.Core.Models</c> version it built against, so a
/// cache shared across app versions produces "Could not load file or assembly
/// 'Skua.Core.Models, Version=1.8.3.0'" when an old-version DLL is hit by a newer
/// build (exactly the failure seen upgrading the 1.8.3 line to the 1.1.x line).
/// </summary>
public class CompilerCacheVersionTests
{
    [Fact]
    public void CacheVersion_is_scoped_to_the_running_assembly_version()
    {
        string? version = typeof(Compiler).Assembly.GetName().Version?.ToString();
        Assert.False(string.IsNullOrEmpty(version));

        // Includes the version, so a version bump changes the cache folder name and
        // PurgeStaleCacheDirectories drops the previous version's poisoned cache.
        Assert.Contains(version!, Compiler.CacheVersion);
        // Still carries the salt prefix from the neutralization-era cache key.
        Assert.StartsWith("l4-", Compiler.CacheVersion);
        // Not the old bare constant that was shared across every app version.
        Assert.NotEqual("l4", Compiler.CacheVersion);
    }
}
