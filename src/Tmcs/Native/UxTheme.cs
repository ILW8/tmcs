using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static partial class UxTheme
{
    /// <summary>Draws the window with another application's visual style, e.g. <c>"Explorer"</c> for Explorer's list look.</summary>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SetWindowTheme(nint hwnd, string? subAppName, string? subIdList);
}
