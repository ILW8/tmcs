using System.Runtime.InteropServices;

namespace GpuTouch;

/// <summary>
/// The soak's GPU churner: creates a Direct3D 11 device on the default adapter (adapter 0), holds it for 500 ms, releases it and
/// exits with 0, or with 1 if the device could not be created. It opens no window. Each run is a short-lived GPU process, so the
/// <c>GPU Engine</c> PDH instances come and go as they do for real GPU-using processes; tools/churn.ps1 -GpuParallel keeps
/// several running.
/// </summary>
internal static partial class Program
{
    /// <summary>D3D_DRIVER_TYPE_HARDWARE.</summary>
    const int DriverTypeHardware = 1;

    /// <summary>D3D11_SDK_VERSION.</summary>
    const uint SdkVersion = 7;

    const int HoldMs = 500;

    static int Main()
    {
        // A real device: a probe with a null device pointer creates no GPU Engine instances. A device adds one per engine, 13 on
        // the owner's GPU.
        var hr = D3D11CreateDevice(0, DriverTypeHardware, 0, 0, 0, 0, SdkVersion, out var device, out _, out var context);
        try
        {
            if (hr < 0 || device == 0) return 1;
            Thread.Sleep(HoldMs);
            return 0;
        }
        finally
        {
            if (context != 0) Marshal.Release(context);
            if (device != 0) Marshal.Release(device);
        }
    }

    /// <param name="adapter">IDXGIAdapter*; null for the default adapter, as the hardware driver type requires.</param>
    /// <param name="featureLevels">D3D_FEATURE_LEVEL*; null for the default levels.</param>
    [LibraryImport("d3d11.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int D3D11CreateDevice(nint adapter, int driverType, nint software, uint flags, nint featureLevels,
        uint featureLevelCount, uint sdkVersion, out nint device, out int featureLevel, out nint immediateContext);
}
