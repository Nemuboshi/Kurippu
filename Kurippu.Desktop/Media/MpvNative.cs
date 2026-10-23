using System.Reflection;
using System.Runtime.InteropServices;

namespace Kurippu.Desktop.Media;

public static class MpvNative
{
    private const string Library = "kurippu-mpv";

    static MpvNative() => NativeLibrary.SetDllImportResolver(typeof(MpvNative).Assembly, Resolve);

    public static string LibraryFileName(string platform) => platform switch
    {
        "win" => "libmpv-2.dll",
        "osx" => "libmpv.dylib",
        "linux" => "libmpv.so",
        _ => throw new PlatformNotSupportedException(platform)
    };

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (!string.Equals(name, Library, StringComparison.Ordinal)) return IntPtr.Zero;
        var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var filename = LibraryFileName(platform);
        var local = Path.Combine(AppContext.BaseDirectory, filename);
        if (NativeLibrary.TryLoad(local, out var loaded)) return loaded;
        if (NativeLibrary.TryLoad(filename, assembly, path, out loaded)) return loaded;
        if (string.Equals(platform, "win", StringComparison.Ordinal) && NativeLibrary.TryLoad("mpv-2.dll", assembly, path, out loaded)) return loaded;
        throw new DllNotFoundException($"libmpv not found: {local}");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RenderParameter(int type, IntPtr data)
    {
        public int Type = type;
        public IntPtr Data = data;
    }

    [DllImport(Library, EntryPoint = "mpv_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr Create();
    [DllImport(Library, EntryPoint = "mpv_initialize", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Initialize(IntPtr handle);
    [DllImport(Library, EntryPoint = "mpv_terminate_destroy", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void TerminateDestroy(IntPtr handle);
    [DllImport(Library, EntryPoint = "mpv_set_option_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetOption(IntPtr handle, byte[] name, byte[] value);
    [DllImport(Library, EntryPoint = "mpv_set_property_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetProperty(IntPtr handle, byte[] name, byte[] value);
    [DllImport(Library, EntryPoint = "mpv_get_property_string", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr GetProperty(IntPtr handle, byte[] name);
    [DllImport(Library, EntryPoint = "mpv_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Free(IntPtr pointer);
    [DllImport(Library, EntryPoint = "mpv_command", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Command(IntPtr handle, IntPtr args);
    [DllImport(Library, EntryPoint = "mpv_wait_event", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr WaitEvent(IntPtr handle, double timeout);
    [DllImport(Library, EntryPoint = "mpv_render_context_create", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int RenderCreate(out IntPtr context, IntPtr handle, [In] RenderParameter[] parameters);
    [DllImport(Library, EntryPoint = "mpv_render_context_free", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void RenderFree(IntPtr context);
    [DllImport(Library, EntryPoint = "mpv_render_context_update", CallingConvention = CallingConvention.Cdecl)]
    internal static extern ulong RenderUpdate(IntPtr context);
    [DllImport(Library, EntryPoint = "mpv_render_context_render", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Render(IntPtr context, [In] RenderParameter[] parameters);
}
