using System.Runtime.InteropServices;

namespace Skua.Flash.Linux;

/// <summary>
/// P/Invoke bindings for <c>libskua_flash.so</c> (the Layer 3b Rust bridge in
/// <c>native/skua-flash-bridge</c>). The C ABI is documented in
/// <c>native/skua-flash-bridge/include/skua_flash.h</c>.
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// Base library name. The runtime loader resolves this to
    /// <c>libskua_flash.so</c> on Linux (and <c>skua_flash.dll</c> if ever built
    /// for Windows).
    /// </summary>
    internal const string Lib = "skua_flash";

    /// <summary>
    /// ABI version this binding expects. Must equal <see cref="AbiVersion"/> at
    /// runtime or the native library is incompatible.
    /// </summary>
    internal const uint ExpectedAbiVersion = 1;

    /// <summary>AS3 -> host callback. <paramref name="invokeXml"/> is a UTF-8
    /// <c>&lt;invoke&gt;</c> string valid only for the duration of the call.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SkuaFlashCallback(nint user, nint invokeXml);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial uint skua_flash_abi_version();

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial nint skua_flash_create();

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial void skua_flash_destroy(nint handle);

    // Delegate marshalling requires a classic DllImport (LibraryImport does not
    // support delegate parameters).
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void skua_flash_set_callback(nint handle, SkuaFlashCallback cb, nint user);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial int skua_flash_load_swf(nint handle, [In] byte[] bytes, nuint len);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial nint skua_flash_call(nint handle, string invokeXml);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    internal static partial void skua_flash_string_free(nint s);

    /// <summary>The ABI version reported by the loaded native library.</summary>
    internal static uint AbiVersion() => skua_flash_abi_version();

    /// <summary>
    /// Invoke the bridge and marshal the returned, caller-owned UTF-8 string,
    /// freeing the native allocation. Returns <see langword="null"/> if the
    /// bridge returned null.
    /// </summary>
    internal static string? Call(nint handle, string invokeXml)
    {
        nint ptr = skua_flash_call(handle, invokeXml);
        if (ptr == 0)
            return null;
        try
        {
            return Marshal.PtrToStringUTF8(ptr);
        }
        finally
        {
            skua_flash_string_free(ptr);
        }
    }
}
