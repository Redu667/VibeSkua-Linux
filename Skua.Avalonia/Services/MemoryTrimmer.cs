using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Skua.Avalonia.Services;

/// <summary>
/// Periodically returns freed memory to the OS on Linux.
/// <para>
/// The live game view reads back a ~2.2&#160;MB frame buffer ~30&#215;/second through
/// the native Ruffle bridge (<c>RenderPlayer::capture</c>). Those buffers are
/// freed each frame, but glibc's allocator keeps the freed pages in its arenas
/// instead of returning them to the kernel, so a long bot session sees RSS climb
/// and plateau high and never fall. WPF countered this with
/// <c>GameContainerUserControl</c>'s periodic <c>SetProcessWorkingSetSize</c>
/// timer; the Avalonia port dropped it and never replaced it. This is the Linux
/// twin: on a timer, run a blocking gen-2 GC (managed heap) then
/// <c>malloc_trim(0)</c> (glibc) to release the arena free pages back to the OS.
/// </para>
/// Best-effort and self-contained: on a non-glibc libc (musl) <c>malloc_trim</c>
/// is absent and the P/Invoke is swallowed, leaving just the managed GC.
/// </summary>
public sealed class MemoryTrimmer : IDisposable
{
    private readonly Timer _timer;

    public MemoryTrimmer(TimeSpan interval)
    {
        _timer = new Timer(static _ => Trim(), null, interval, interval);
    }

    /// <summary>Run one trim pass now. Safe to call from any thread.</summary>
    public static void Trim()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
        }
        catch { /* GC.Collect should never throw, but stay defensive */ }

        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            MallocTrim(UIntPtr.Zero);
        }
        catch
        {
            // malloc_trim is glibc-only; absent on musl. Managed GC above still ran.
        }
    }

    // glibc: release free heap memory at the top of every arena back to the OS.
    [DllImport("libc", EntryPoint = "malloc_trim")]
    private static extern int MallocTrim(UIntPtr pad);

    public void Dispose() => _timer.Dispose();
}
