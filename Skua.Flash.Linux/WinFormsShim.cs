// A cross-platform shim for the slice of System.Windows.Forms (and the
// System.Drawing.Common-only bits) that AQW bot scripts use. On Windows those
// scripts compile against the real assemblies; Linux has neither, so the script
// compiler fails at `using System.Windows.Forms;` / `using System.Drawing;` and
// every dependent library (CoreBots, CoreAdvanced, …) cascades to "not found".
//
// CoreBots in particular builds a WinForms progress-bar splash (an April-Fools
// gag). None of that GUI is meaningful headless on Linux, so these types exist
// only to make such scripts COMPILE and behave as harmless no-ops at runtime
// (forms never show, Application.Run returns immediately, Graphics calls do
// nothing). Size/Point/PointF/Color/Rectangle come from the real
// System.Drawing.Primitives (referenced by the compiler) — only the
// System.Drawing.Common types are shimmed here, so there's no ambiguity.
//
// Referenced by the app, so it loads into the AppDomain and the compiler's
// reference scan picks it up. NOT compiled into the Windows build.

using System.Collections;
using System.Collections.Generic;
using System.Drawing;

// ---------------------------------------------------------------------------
// System.Windows.Forms
// ---------------------------------------------------------------------------
namespace System.Windows.Forms
{
    public enum DialogResult { None = 0, OK = 1, Cancel = 2, Abort = 3, Retry = 4, Ignore = 5, Yes = 6, No = 7 }

    [Flags]
    public enum MessageBoxButtons { OK = 0, OKCancel = 1, AbortRetryIgnore = 2, YesNoCancel = 3, YesNo = 4, RetryCancel = 5 }

    public enum MessageBoxIcon { None = 0, Error = 16, Question = 32, Warning = 48, Information = 64, Hand = 16, Stop = 16, Exclamation = 48, Asterisk = 64 }

    public enum MessageBoxDefaultButton { Button1 = 0, Button2 = 256, Button3 = 512 }

    public enum DockStyle { None = 0, Top = 1, Bottom = 2, Left = 3, Right = 4, Fill = 5 }

    [Flags]
    public enum AnchorStyles { None = 0, Top = 1, Bottom = 2, Left = 4, Right = 8 }

    public enum FormStartPosition { Manual = 0, CenterScreen = 1, WindowsDefaultLocation = 2, WindowsDefaultBounds = 3, CenterParent = 4 }

    public enum FormBorderStyle { None = 0, FixedSingle = 1, Fixed3D = 2, FixedDialog = 3, Sizable = 4, FixedToolWindow = 5, SizableToolWindow = 6 }

    public enum ProgressBarStyle { Blocks = 0, Continuous = 1, Marquee = 2 }

    public delegate void MethodInvoker();

    public class PaintEventArgs : EventArgs
    {
        public Graphics Graphics { get; } = new Graphics();
        public Rectangle ClipRectangle { get; }
    }

    public delegate void PaintEventHandler(object? sender, PaintEventArgs e);

    /// <summary>Base for the WinForms controls scripts touch. Everything here is
    /// a no-op stand-in — nothing is ever displayed.</summary>
    public class Control : IDisposable
    {
        public string Text { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int Top { get; set; }
        public int Left { get; set; }
        public bool Visible { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public Size Size { get; set; }
        public Size ClientSize { get; set; }
        public Point Location { get; set; }
        public Color BackColor { get; set; }
        public Color ForeColor { get; set; }
        public Font? Font { get; set; }
        public DockStyle Dock { get; set; }
        public AnchorStyles Anchor { get; set; }
        public int TabIndex { get; set; }
        public Control? Parent { get; set; }
        public ControlCollection Controls { get; }

        public event EventHandler? Click;
        public event EventHandler? Load;
        public event PaintEventHandler? Paint;

        public Control() => Controls = new ControlCollection(this);

        public void Show() { }
        public void Hide() { }
        public void Refresh() { }
        public void Invalidate() { }
        public void SuspendLayout() { }
        public void ResumeLayout() { }
        public void ResumeLayout(bool _) { }
        public void PerformLayout() { }
        public void BringToFront() { }

        // Run the delegate inline: there is no UI thread to marshal to, and
        // scripts sometimes rely on the side effects (cleanup, etc.).
        public object? Invoke(Delegate method) => method.DynamicInvoke();
        public object? Invoke(MethodInvoker method) { method(); return null; }
        public object? BeginInvoke(Delegate method) => method.DynamicInvoke();
        public object? BeginInvoke(MethodInvoker method) { method(); return null; }

        public virtual void Dispose() { GC.SuppressFinalize(this); }

        protected void OnClick() => Click?.Invoke(this, EventArgs.Empty);
        protected void OnPaint(PaintEventArgs e) => Paint?.Invoke(this, e);
        protected void OnLoad() => Load?.Invoke(this, EventArgs.Empty);

        public class ControlCollection : IEnumerable<Control>
        {
            private readonly List<Control> _items = new();
            private readonly Control _owner;
            public ControlCollection(Control owner) => _owner = owner;
            public void Add(Control c) { c.Parent = _owner; _items.Add(c); }
            public void AddRange(Control[] cs) { foreach (var c in cs) Add(c); }
            public void Remove(Control c) => _items.Remove(c);
            public void Clear() => _items.Clear();
            public int Count => _items.Count;
            public Control this[int i] => _items[i];
            public IEnumerator<Control> GetEnumerator() => _items.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
        }
    }

    public class ScrollableControl : Control { }
    public class ContainerControl : ScrollableControl { }

    public class Form : ContainerControl
    {
        public FormStartPosition StartPosition { get; set; }
        public FormBorderStyle FormBorderStyle { get; set; }
        public bool ControlBox { get; set; } = true;
        public bool MaximizeBox { get; set; } = true;
        public bool MinimizeBox { get; set; } = true;
        public bool TopMost { get; set; }
        public bool ShowInTaskbar { get; set; } = true;
        public object? Icon { get; set; }
        public DialogResult DialogResult { get; set; }

        public event EventHandler? Shown;
        public event EventHandler? FormClosing;
        public event EventHandler? FormClosed;

        // Never actually shown headlessly, so `Shown` deliberately does not fire
        // (its handlers in scripts sometimes Sleep for many seconds).
        public DialogResult ShowDialog() => DialogResult.OK;
        public DialogResult ShowDialog(object _) => DialogResult.OK;
        public new void Show() { }
        public void Close() { FormClosing?.Invoke(this, EventArgs.Empty); FormClosed?.Invoke(this, EventArgs.Empty); }

        internal void RaiseShown() => Shown?.Invoke(this, EventArgs.Empty);
    }

    public class Label : Control { public bool AutoSize { get; set; } public ContentAlignment TextAlign { get; set; } }
    public class Button : Control { public DialogResult DialogResult { get; set; } }
    public class Panel : ScrollableControl { }
    public class TextBox : Control { public bool Multiline { get; set; } public bool ReadOnly { get; set; } }
    public class PictureBox : Control { public object? Image { get; set; } }

    public enum ContentAlignment { TopLeft = 1, TopCenter = 2, TopRight = 4, MiddleLeft = 16, MiddleCenter = 32, MiddleRight = 64, BottomLeft = 256, BottomCenter = 512, BottomRight = 1024 }

    public class ProgressBar : Control
    {
        public int Value { get; set; }
        public int Minimum { get; set; }
        public int Maximum { get; set; } = 100;
        public int Step { get; set; } = 10;
        public ProgressBarStyle Style { get; set; }
        public void PerformStep() { }
    }

    public static class MessageBox
    {
        public static DialogResult Show(string? text) => Show(text, string.Empty);
        public static DialogResult Show(string? text, string? caption) => Show(text, caption, MessageBoxButtons.OK);
        public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons) => Show(text, caption, buttons, MessageBoxIcon.None);
        public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Show(text, caption, buttons, icon, MessageBoxDefaultButton.Button1);

        public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            Console.WriteLine($"[MessageBox] {(string.IsNullOrEmpty(caption) ? "Message" : caption)}: {text}");
            return buttons switch
            {
                MessageBoxButtons.OKCancel => DialogResult.OK,
                MessageBoxButtons.YesNo => DialogResult.Yes,
                MessageBoxButtons.YesNoCancel => DialogResult.Yes,
                MessageBoxButtons.RetryCancel => DialogResult.Retry,
                MessageBoxButtons.AbortRetryIgnore => DialogResult.Ignore,
                _ => DialogResult.OK,
            };
        }
    }

    public static class Clipboard
    {
        private static string _text = string.Empty;
        public static string GetText() => _text;
        public static void SetText(string text) => _text = text ?? string.Empty;
        public static bool ContainsText() => !string.IsNullOrEmpty(_text);
        public static void Clear() => _text = string.Empty;
    }

    public static class Application
    {
        // Headless: do not enter a message loop (a real Run would block forever
        // and, since forms never show, nothing would drive them anyway).
        public static void Run() { }
        public static void Run(Form _) { }
        public static void Exit() { }
        public static void DoEvents() { }
        public static string ExecutablePath => Environment.ProcessPath ?? string.Empty;
        public static string StartupPath => AppContext.BaseDirectory;
    }
}

// ---------------------------------------------------------------------------
// System.Drawing — only the System.Drawing.Common types (Size/Point/PointF/
// Color/Rectangle come from the real System.Drawing.Primitives).
// ---------------------------------------------------------------------------
namespace System.Drawing
{
    [Flags]
    public enum FontStyle { Regular = 0, Bold = 1, Italic = 2, Underline = 4, Strikeout = 8 }

    public class Font : IDisposable
    {
        public string Name { get; }
        public float Size { get; }
        public FontStyle Style { get; }
        public Font(string name, float size) : this(name, size, FontStyle.Regular) { }
        public Font(string name, float size, FontStyle style) { Name = name; Size = size; Style = style; }
        public void Dispose() { GC.SuppressFinalize(this); }
    }

    public class Brush : IDisposable { public virtual void Dispose() { GC.SuppressFinalize(this); } }
    public class SolidBrush : Brush { public Color Color { get; } public SolidBrush(Color color) => Color = color; }
    public class Pen : IDisposable { public Color Color { get; } public float Width { get; set; } public Pen(Color color) => Color = color; public Pen(Color color, float width) { Color = color; Width = width; } public void Dispose() { GC.SuppressFinalize(this); } }

    public static class Brushes
    {
        public static Brush White => new SolidBrush(Color.White);
        public static Brush Black => new SolidBrush(Color.Black);
        public static Brush Red => new SolidBrush(Color.Red);
        public static Brush Green => new SolidBrush(Color.Green);
        public static Brush Blue => new SolidBrush(Color.Blue);
        public static Brush Yellow => new SolidBrush(Color.Yellow);
        public static Brush Gray => new SolidBrush(Color.Gray);
    }

    public static class Pens
    {
        public static Pen White => new Pen(Color.White);
        public static Pen Black => new Pen(Color.Black);
        public static Pen Red => new Pen(Color.Red);
    }

    // NOTE: ColorTranslator lives in System.Drawing.Primitives (referenced by the
    // compiler), so it is intentionally NOT shimmed here — doing so collides.

    /// <summary>Headless drawing surface — every operation is a no-op.</summary>
    public class Graphics : IDisposable
    {
        public void DrawString(string? s, Font? font, Brush? brush, PointF point) { }
        public void DrawString(string? s, Font? font, Brush? brush, float x, float y) { }
        public void FillRectangle(Brush? brush, Rectangle rect) { }
        public void FillRectangle(Brush? brush, int x, int y, int w, int h) { }
        public void DrawRectangle(Pen? pen, Rectangle rect) { }
        public void DrawLine(Pen? pen, Point a, Point b) { }
        public void Clear(Color color) { }
        public void Dispose() { GC.SuppressFinalize(this); }
    }
}
