using System.Runtime.InteropServices;

namespace Tmcs.Native;

internal static unsafe partial class ComCtl32
{
    public const uint IlcMask = 0x1;
    public const uint IlcColor32 = 0x20;

    /// <returns>The image list handle, or 0 on failure. The caller must <see cref="ImageList_Destroy"/> it.</returns>
    [LibraryImport("comctl32.dll")]
    public static partial nint ImageList_Create(int cx, int cy, uint flags, int initial, int grow);

    /// <summary>The image list copies the icon, so the caller still owns <paramref name="icon"/>.</summary>
    /// <param name="index">The image to replace, or -1 to append one.</param>
    /// <returns>The index of the image, or -1 on failure.</returns>
    [LibraryImport("comctl32.dll")]
    public static partial int ImageList_ReplaceIcon(nint imageList, int index, nint icon);

    [LibraryImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImageList_Destroy(nint imageList);

    [LibraryImport("comctl32.dll")]
    public static partial int ImageList_GetImageCount(nint imageList);

    /// <summary><c>TDCBF_OK_BUTTON</c>.</summary>
    public const uint TdcbfOkButton = 0x1;

    /// <summary><c>TDCBF_CANCEL_BUTTON</c>.</summary>
    public const uint TdcbfCancelButton = 0x8;

    /// <summary><c>TD_ERROR_ICON</c>: <c>MAKEINTRESOURCEW(-2)</c>, the low word only.</summary>
    public const nint TdErrorIcon = 0xFFFE;

    /// <summary><c>TDN_CREATED</c>: the dialog has been created and is about to be shown. wParam and lParam are unused.</summary>
    public const uint TdnCreated = 0;

    /// <summary><c>TDN_VERIFICATION_CLICKED</c>: wParam is 1 when the verification box is now checked, else 0.</summary>
    public const uint TdnVerificationClicked = 8;

    /// <summary><c>TDM_ENABLE_BUTTON</c> (WM_USER + 111): wParam is the button id, lParam nonzero to enable it.</summary>
    public const uint TdmEnableButton = 0x0400 + 111;

    /// <summary><c>TASKDIALOG_BUTTON</c>. commctrl.h packs the task dialog structs to 1 byte, so this is 12 bytes on x64.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TaskDialogButton
    {
        public int Id;
        public char* Text;
    }

    /// <summary>
    /// <c>TASKDIALOGCONFIG</c>, packed to 1 byte as commctrl.h has it: 0xA0 bytes on x64. The unions (main and footer icon) are
    /// handles here; a string field is null when unused.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TaskDialogConfig
    {
        public uint CbSize;
        public nint Owner;
        public nint Instance;
        /// <summary><c>TASKDIALOG_FLAGS</c>.</summary>
        public uint Flags;
        /// <summary><c>TDCBF_*</c>.</summary>
        public uint CommonButtons;
        public char* WindowTitle;
        public nint MainIcon;
        public char* MainInstruction;
        public char* Content;
        public uint ButtonCount;
        public TaskDialogButton* Buttons;
        public int DefaultButton;
        public uint RadioButtonCount;
        public TaskDialogButton* RadioButtons;
        public int DefaultRadioButton;
        public char* VerificationText;
        public char* ExpandedInformation;
        public char* ExpandedControlText;
        public char* CollapsedControlText;
        public nint FooterIcon;
        public char* Footer;
        /// <summary><c>PFTASKDIALOGCALLBACK</c>(hwnd, notification, wParam, lParam, refData), returning an HRESULT; null for none.</summary>
        public delegate* unmanaged<nint, uint, nint, nint, nint, int> Callback;
        public nint CallbackData;
        public uint Width;
    }

    /// <summary>Shows a modal task dialog. Exists in comctl32 v6 only.</summary>
    /// <param name="button">Receives the id of the button that closed the dialog.</param>
    /// <param name="radioButton">Receives the chosen radio button's id, or null.</param>
    /// <param name="verificationChecked">Receives whether the verification box was checked (a BOOL), or null.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("comctl32.dll")]
    public static partial int TaskDialogIndirect(in TaskDialogConfig config, out int button, int* radioButton, int* verificationChecked);

    /// <summary>Shows a modal task dialog with common buttons only. Exists in comctl32 v6 only.</summary>
    /// <param name="instance">The module <paramref name="title"/> and the other strings are loaded from when they are resource ids; 0 for plain strings.</param>
    /// <param name="buttons"><c>TDCBF_*</c>.</param>
    /// <param name="icon">A <c>TD_*_ICON</c> value, or 0 for none.</param>
    /// <param name="button">Receives the id of the button that closed the dialog.</param>
    /// <returns>An HRESULT.</returns>
    [LibraryImport("comctl32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int TaskDialog(nint owner, nint instance, string? title, string? mainInstruction, string? content, uint buttons, nint icon, out int button);
}
