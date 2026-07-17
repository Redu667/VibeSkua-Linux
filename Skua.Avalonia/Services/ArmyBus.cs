using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.Avalonia.Services;

/// <summary>
/// Linux replacement for the Windows army IPC (EnumWindows + PostMessage WM
/// broadcast): every VibeSkua process (manager and each --client) listens on a
/// Unix domain socket in a shared per-user directory, and
/// <see cref="Broadcast(uint,int,int)"/> delivers the same (msg, wParam,
/// lParam) triple the WPF WndProc receives to every listener — self included,
/// matching the Windows broadcast which posts to its own windows too.
/// Wire format: one line per message, "MSG WPARAM LPARAM\n".
/// </summary>
public sealed class ArmyBus : IDisposable
{
    private readonly string _socketPath;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Raised (on a background thread) for every received message.</summary>
    public event Action<int, int, int>? MessageReceived;

    public ArmyBus(string? directory = null)
    {
        string dir = directory ?? DefaultDirectory();
        Directory.CreateDirectory(dir);
        try { File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        catch { }

        _socketPath = Path.Combine(dir, $"{Environment.ProcessId}.sock");
        File.Delete(_socketPath); // stale leftover from a recycled pid

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(16);
        _ = Task.Run(AcceptLoopAsync);
    }

    public static string DefaultDirectory()
    {
        string? runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        return string.IsNullOrEmpty(runtime)
            ? Path.Combine(Path.GetTempPath(), $"skua-army-{Environment.UserName}")
            : Path.Combine(runtime, "skua-army");
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(_cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }
            catch (ObjectDisposedException) { break; }

            _ = Task.Run(async () =>
            {
                try
                {
                    using NetworkStream stream = new(client, ownsSocket: true);
                    using StreamReader reader = new(stream, Encoding.UTF8);
                    string? line;
                    while ((line = await reader.ReadLineAsync(_cts.Token)) is not null)
                    {
                        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 3 &&
                            int.TryParse(parts[0], out int msg) &&
                            int.TryParse(parts[1], out int wParam) &&
                            int.TryParse(parts[2], out int lParam))
                        {
                            try { MessageReceived?.Invoke(msg, wParam, lParam); }
                            catch (Exception ex) { Console.Error.WriteLine($"army message handler failed: {ex}"); }
                        }
                    }
                }
                catch { /* connection torn down — broadcaster already moved on */ }
            });
        }
    }

    /// <summary>Broadcast to every listening VibeSkua process (self included).</summary>
    public void Broadcast(uint msg, int wParam, int lParam)
        => Broadcast(Path.GetDirectoryName(_socketPath)!, msg, wParam, lParam);

    public static void Broadcast(string directory, uint msg, int wParam, int lParam)
    {
        if (!Directory.Exists(directory))
            return;

        byte[] payload = Encoding.UTF8.GetBytes($"{msg} {wParam} {lParam}\n");
        foreach (string sock in Directory.GetFiles(directory, "*.sock"))
        {
            try
            {
                using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.SendTimeout = 1000;
                socket.Connect(new UnixDomainSocketEndPoint(sock));
                socket.Send(payload);
            }
            catch
            {
                // Dead listener (crashed/killed process) — clean up its socket
                // so the directory doesn't accumulate stale entries.
                try { File.Delete(sock); } catch { }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Dispose();
        try { File.Delete(_socketPath); } catch { }
        _cts.Dispose();
    }
}
