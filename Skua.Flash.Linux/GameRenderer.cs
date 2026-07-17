using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Skua.Flash.Linux;

/// <summary>
/// Managed wrapper over the offscreen game renderer in <c>libskua_flash.so</c>
/// (the <c>skua_flash_render_*</c> C ABI, present only when the bridge is built
/// <c>--features ruffle-render</c>). Owns a native render handle that draws the
/// game to an offscreen wgpu texture; <see cref="Capture"/> pulls the latest
/// frame as RGBA8 for an Avalonia bitmap, and mouse/text are forwarded down.
///
/// If the loaded <c>.so</c> has no render support (a headless/mock build), the
/// render entry points are absent; <see cref="TryCreate"/> returns
/// <see langword="null"/> rather than throwing, so the app degrades gracefully.
/// </summary>
public sealed partial class GameRenderer : IDisposable
{
    private nint _handle;

    public int Width { get; }
    public int Height { get; }
    public bool IsValid => _handle != 0;

    private GameRenderer(nint handle, int width, int height)
    {
        _handle = handle;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Fetch the game loader at <paramref name="url"/> and start rendering it at
    /// <paramref name="width"/>x<paramref name="height"/>. Returns
    /// <see langword="null"/> if the native renderer is unavailable or init fails
    /// (e.g. no GPU/Vulkan, network error, or a non-render build).
    /// </summary>
    public static GameRenderer? TryCreate(string url, int width, int height)
    {
        try
        {
            nint handle = Native.skua_flash_render_create(url, (uint)width, (uint)height);
            return handle == 0 ? null : new GameRenderer(handle, width, height);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            // .so present but built without ruffle-render.
            return null;
        }
    }

    /// <summary>
    /// Start a renderer whose ROOT movie is the given SWF bytes (with
    /// <paramref name="url"/> as its nominal https origin). This is how the Skua
    /// client boots: <c>skua.swf</c> is the root movie — its ExternalInterface
    /// callbacks register on init, then the host calls <c>loadClient</c> and
    /// skua.swf loads the live game INTO itself, giving its API the <c>game</c>
    /// reference every bot call resolves through. Returns <see langword="null"/>
    /// if the native renderer is unavailable or init fails.
    /// </summary>
    public static GameRenderer? TryCreateFromBytes(byte[] swf, string url, int width, int height)
    {
        if (swf.Length == 0)
            return null;
        try
        {
            nint handle = Native.skua_flash_render_create_bytes(swf, (nuint)swf.Length, url, (uint)width, (uint)height);
            return handle == 0 ? null : new GameRenderer(handle, width, height);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Advance one frame and copy its RGBA8 pixels into <paramref name="buffer"/>.
    /// Returns the number of bytes written (<c>w*h*4</c>), 0 if no frame yet, or
    /// negative on error / insufficient buffer.
    /// </summary>
    public int Capture(byte[] buffer, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (_handle == 0)
            return -1;
        uint w = 0, h = 0;
        int written = Native.skua_flash_render_capture(_handle, buffer, (nuint)buffer.Length, ref w, ref h);
        width = (int)w;
        height = (int)h;
        return written;
    }

    /// <summary>0 = move, 1 = down, 2 = up.</summary>
    public void Mouse(int kind, double x, double y)
    {
        if (_handle != 0)
            Native.skua_flash_render_mouse(_handle, (uint)kind, x, y);
    }

    public void Text(int codepoint)
    {
        if (_handle != 0)
            Native.skua_flash_render_text(_handle, (uint)codepoint);
    }

    /// <summary>
    /// host → AS3 on the rendered player (the bot's GetGameObject / CallGameFunction
    /// path). <paramref name="invokeXml"/> is a Flash <c>&lt;invoke&gt;</c>; returns
    /// the response value XML, or <see langword="null"/>.
    /// </summary>
    public string? Call(string invokeXml)
    {
        if (_handle == 0)
            return null;
        nint ptr = Native.skua_flash_render_call(_handle, invokeXml);
        if (ptr == 0)
            return null;
        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            Native.skua_flash_string_free(ptr);
        }
    }

    /// <summary>
    /// Register the AS3 → host callback (the bot's FlashCall sink) on the rendered
    /// player. Keep the delegate alive for the handle's lifetime.
    /// </summary>
    public void SetCallback(FlashCallback callback, nint user = 0)
    {
        if (_handle != 0)
            Native.skua_flash_render_set_callback(_handle, callback, user);
    }

    /// <summary>
    /// Inject <c>skua.swf</c> into this live game player (same-domain with the
    /// game), so the bot can drive the very game shown here. Returns true on
    /// success.
    /// </summary>
    public bool LoadSwf(byte[] swf)
    {
        if (_handle == 0 || swf.Length == 0)
            return false;
        return Native.skua_flash_render_load_swf(_handle, swf, (nuint)swf.Length) == 0;
    }

    /// <summary>AS3 → host callback: <paramref name="invokeXml"/> is a UTF-8
    /// <c>&lt;invoke&gt;</c> valid only for the call.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FlashCallback(nint user, nint invokeXml);

    public void Dispose()
    {
        if (_handle != 0)
        {
            Native.skua_flash_render_destroy(_handle);
            _handle = 0;
        }
        GC.SuppressFinalize(this);
    }

    ~GameRenderer() => Dispose();

    private static partial class Native
    {
        private const string Lib = "skua_flash";

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial nint skua_flash_render_create(string url, uint width, uint height);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial nint skua_flash_render_create_bytes(byte[] bytes, nuint len, string url, uint width, uint height);

        [LibraryImport(Lib)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int skua_flash_render_capture(
            nint handle, byte[] buffer, nuint capacity, ref uint outWidth, ref uint outHeight);

        [LibraryImport(Lib)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void skua_flash_render_mouse(nint handle, uint kind, double x, double y);

        [LibraryImport(Lib)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void skua_flash_render_text(nint handle, uint codepoint);

        [LibraryImport(Lib)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void skua_flash_render_destroy(nint handle);

        [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial nint skua_flash_render_call(nint handle, string invokeXml);

        [LibraryImport(Lib)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void skua_flash_render_set_callback(nint handle, FlashCallback callback, nint user);

        [LibraryImport(Lib)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial int skua_flash_render_load_swf(nint handle, byte[] bytes, nuint len);

        [LibraryImport(Lib)]
        [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
        internal static partial void skua_flash_string_free(nint s);
    }
}
