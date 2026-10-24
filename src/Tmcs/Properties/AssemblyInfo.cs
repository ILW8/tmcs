using System.Runtime.InteropServices;

// tmcs runs elevated, so its P/Invokes load system DLLs from System32 only, never from the exe's folder, where anyone who can
// write to that folder could plant one. The manifest's side-by-side redirection still applies, so comctl32 stays version 6.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
