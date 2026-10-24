using System.Runtime.InteropServices;

namespace Tmcs.Tests.Support;

/// <summary>
/// The test host has no comctl32 v6 manifest, so its windows would come from comctl32 5.82. This activates a context built from
/// <c>comctl6.manifest</c> (the same dependency as the app's manifest) so the test windows match the app.
/// </summary>
internal static class CommonControls6
{
    private static readonly Lazy<nint> Context = new(Create);

    /// <summary>Activates the context on the calling thread until the returned scope is disposed.</summary>
    public static IDisposable Activate()
    {
        if (!ActivateActCtx(Context.Value, out var cookie)) throw new System.ComponentModel.Win32Exception();
        return new Scope(cookie);
    }

    private static nint Create()
    {
        var actCtx = new ACTCTXW
        {
            cbSize = (uint)Marshal.SizeOf<ACTCTXW>(),
            lpSource = Path.Combine(AppContext.BaseDirectory, "Support", "comctl6.manifest"),
        };
        var handle = CreateActCtxW(ref actCtx);
        if (handle == -1) throw new System.ComponentModel.Win32Exception();
        return handle; // never released: it lives as long as the test process
    }

    private sealed class Scope(nint cookie) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DeactivateActCtx(0, cookie);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ACTCTXW
    {
        public uint cbSize;
        public uint dwFlags;
        public string lpSource;
        public ushort wProcessorArchitecture;
        public ushort wLangId;
        public string? lpAssemblyDirectory;
        public string? lpResourceName;
        public string? lpApplicationName;
        public nint hModule;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateActCtxW(ref ACTCTXW actCtx);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ActivateActCtx(nint hActCtx, out nint cookie);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeactivateActCtx(uint flags, nint cookie);
}
