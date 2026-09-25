using System.Runtime.InteropServices;

// P/Invoke targets (user32, kernel32) resolve from System32 only, never the
// current directory or PATH, so a planted DLL next to the exe can't be loaded.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
