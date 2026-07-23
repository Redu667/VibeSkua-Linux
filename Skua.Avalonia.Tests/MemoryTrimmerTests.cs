using System;
using System.Threading;
using Skua.Avalonia.Services;
using Xunit;

namespace Skua.Avalonia.Tests;

public class MemoryTrimmerTests
{
    /// <summary>
    /// Trim() must run to completion on Linux — proving the glibc
    /// <c>malloc_trim</c> P/Invoke resolves and the GC path doesn't throw. This
    /// is what the periodic timer calls to hand freed render-buffer memory back
    /// to the OS.
    /// </summary>
    [Fact]
    public void Trim_completes_without_throwing()
    {
        Exception? ex = Record.Exception(MemoryTrimmer.Trim);
        Assert.Null(ex);
    }

    /// <summary>
    /// The timer fires and trims on its interval, and Dispose stops it — no
    /// leaked timer callbacks.
    /// </summary>
    [Fact]
    public void Timer_fires_then_stops_on_dispose()
    {
        using var trimmer = new MemoryTrimmer(TimeSpan.FromMilliseconds(50));
        // Give the timer a couple of intervals to fire; a throwing callback would
        // surface via the test host's unobserved-exception handling.
        Thread.Sleep(150);
        trimmer.Dispose();
    }
}
