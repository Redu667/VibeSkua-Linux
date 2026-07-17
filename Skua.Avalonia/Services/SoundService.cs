using Skua.Core.Interfaces;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux <see cref="ISoundService"/>. <see cref="Console.Beep()"/> is honoured
/// where the terminal supports it; the frequency/duration overload is not
/// available on Unix, so it falls back to the plain beep. Best-effort, never
/// throws.
/// </summary>
public sealed class SoundService : ISoundService
{
    public void Beep()
    {
        try { Console.Beep(); } catch { /* no console bell — ignore */ }
    }

    public void Beep(int frequency, int duration)
    {
        // Console.Beep(int, int) throws PlatformNotSupportedException on Unix.
        Beep();
    }
}
