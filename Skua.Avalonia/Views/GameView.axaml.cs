using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Avalonia.Services;

namespace Skua.Avalonia.Views;

/// <summary>
/// The in-window AQW game surface. It does NOT own the game — the process-wide
/// <see cref="GameSession"/> does — it only *displays* the session: a ~30fps
/// timer pulls the latest RGBA frame and blits it into a <see cref="WriteableBitmap"/>,
/// and pointer/keyboard input is forwarded to the session's live player. Because
/// the game lives in the session, switching sidebar tabs (which detaches this
/// view) stops the display but leaves the bot running and the engine bound; the
/// display resumes when the Game tab is shown again.
/// </summary>
public partial class GameView : UserControl
{
    private readonly Button _start;
    private readonly Button _stop;
    private readonly TextBlock _status;
    private readonly Image _image;

    private readonly GameSession _session;
    private WriteableBitmap? _bitmap;
    private byte[] _buffer = Array.Empty<byte>();
    private DispatcherTimer? _timer;
    private Window? _window;

    public GameView()
    {
        InitializeComponent();
        _start = this.FindControl<Button>("StartButton")!;
        _stop = this.FindControl<Button>("StopButton")!;
        _status = this.FindControl<TextBlock>("StatusText")!;
        _image = this.FindControl<Image>("GameImage")!;

        _session = Ioc.Default.GetService(typeof(GameSession)) as GameSession ?? new GameSession();

        _start.Click += (_, _) => Start();
        _stop.Click += (_, _) => StopGame();

        _image.PointerMoved += OnPointerMoved;
        _image.PointerPressed += OnPointerPressed;
        _image.PointerReleased += OnPointerReleased;
        TextInput += OnTextInput;

        DataContextChanged += (_, _) => OnAttachedOrContext();
        AttachedToVisualTree += (_, _) => OnAttachedOrContext();
    }

    public string? InstanceLabel { get; set; }

    /// <summary>Start the game from outside the control (client auto-start).</summary>
    public void StartGame() => Start();

    /// <summary>
    /// On attach / DataContext set: auto-start in client mode, and — crucially —
    /// resume displaying an already-running session (i.e. after tabbing back to
    /// the Game tab), so the game reappears instead of being restarted.
    /// </summary>
    private void OnAttachedOrContext()
    {
        if (DataContext is ViewModels.GameViewModel vm)
        {
            InstanceLabel ??= vm.InstanceLabel;
            _session.InstanceLabel ??= vm.InstanceLabel;
            if (vm.AutoStart && !_session.IsRunning && _timer is null)
            {
                Start();
                return;
            }
        }

        if (_session.IsRunning && _timer is null)
            StartDisplay(); // resume showing the persistent session after a tab switch
    }

    private void Start()
    {
        if (_session.IsRunning)
        {
            StartDisplay();
            return;
        }

        _status.Text = "Starting game… (fetching AQW loader)";
        _start.IsEnabled = false;
        _session.StartAsync(ok => Dispatcher.UIThread.Post(() =>
        {
            if (!ok)
            {
                _status.Text = "Could not start the game renderer. Your GPU/Vulkan drivers may be " +
                               "missing, the build may lack render support, or the loader couldn't be " +
                               "fetched. See ~/.config/Skua/vibeskua-crash.log.";
                _start.IsEnabled = true;
                return;
            }
            StartDisplay();
        }));
    }

    // --- display lifecycle (does NOT touch the game session) ----------------

    private void StartDisplay()
    {
        if (_timer is not null)
            return; // already displaying

        _buffer = new byte[GameSession.Width * GameSession.Height * 4];
        _bitmap = new WriteableBitmap(
            new PixelSize(GameSession.Width, GameSession.Height),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Premul);
        _image.Source = _bitmap;

        _stop.IsEnabled = true;
        _start.IsEnabled = false;
        SetRunningStatus();
        Focus();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => RenderFrame();
        _timer.Start();

        // Army lag control: only the FOCUSED window pays to render. Pause capture
        // when the window is inactive; the bot keeps running (native self-tick).
        if (this.GetVisualRoot() is Window w)
        {
            _window = w;
            _window.Activated += OnWindowActivated;
            _window.Deactivated += OnWindowDeactivated;
        }
    }

    private void StopDisplay()
    {
        _timer?.Stop();
        _timer = null;
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
            _window = null;
        }
        _image.Source = null;
        _bitmap = null;
    }

    /// <summary>Stop button: actually end the game (dispose the session).</summary>
    private void StopGame()
    {
        StopDisplay();
        _session.Stop();
        _stop.IsEnabled = false;
        _start.IsEnabled = true;
        _status.Text = "Stopped.";
    }

    private string _lastStatusText = string.Empty;

    /// <summary>Show the session's live boot/run status (skua.swf lifecycle:
    /// starting → requesting AQW → downloading → loaded — or a loud failure),
    /// so problems are visible right above the game instead of only in logs.
    /// Called from the ~30fps render loop, so it only touches the TextBlock when
    /// the text actually changed — no per-frame string churn.</summary>
    private void SetRunningStatus()
    {
        string s = string.IsNullOrEmpty(_session.Status) ? "Game running." : _session.Status;
        string text = string.IsNullOrEmpty(InstanceLabel) ? s : $"{s} — {InstanceLabel}";
        if (text == _lastStatusText)
            return;
        _lastStatusText = text;
        _status.Text = text;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (!_session.IsRunning)
            return;
        RenderFrame();      // refresh immediately so the view isn't stale
        _timer?.Start();
        SetRunningStatus();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        // Pause rendering (not the bot) while this window is in the background.
        _timer?.Stop();
        if (_session.IsRunning)
            _status.Text = "Rendering paused (window inactive) — bot still running.";
    }

    private void RenderFrame()
    {
        SetRunningStatus(); // keep the boot-progress line live (~30fps timer)
        if (_bitmap is null)
            return;

        int written = _session.Capture(_buffer, out int w, out int h);
        if (written <= 0 || w != GameSession.Width || h != GameSession.Height)
            return;

        using (ILockedFramebuffer fb = _bitmap.Lock())
        {
            int srcStride = GameSession.Width * 4;
            int dstStride = fb.RowBytes;
            if (dstStride == srcStride)
            {
                System.Runtime.InteropServices.Marshal.Copy(_buffer, 0, fb.Address, written);
            }
            else
            {
                for (int y = 0; y < GameSession.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        _buffer, y * srcStride, fb.Address + y * dstStride, srcStride);
                }
            }
        }

        _image.InvalidateVisual();
    }

    // --- input forwarding ---------------------------------------------------

    private (double x, double y) GamePoint(PointerEventArgs e)
    {
        Point p = e.GetPosition(_image);
        double sx = _image.Bounds.Width > 0 ? GameSession.Width / _image.Bounds.Width : 1;
        double sy = _image.Bounds.Height > 0 ? GameSession.Height / _image.Bounds.Height : 1;
        return (p.X * sx, p.Y * sy);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_session.IsRunning) return;
        var (x, y) = GamePoint(e);
        _session.Mouse(0, x, y);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_session.IsRunning) return;
        Focus();
        var (x, y) = GamePoint(e);
        _session.Mouse(1, x, y);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_session.IsRunning) return;
        var (x, y) = GamePoint(e);
        _session.Mouse(2, x, y);
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (!_session.IsRunning || string.IsNullOrEmpty(e.Text)) return;
        foreach (char c in e.Text)
            _session.Text(c);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Tab switched away / window closing: stop DISPLAYING only. The game keeps
        // running in the session so the bot doesn't die when you leave the tab.
        StopDisplay();
        base.OnDetachedFromVisualTree(e);
    }
}
