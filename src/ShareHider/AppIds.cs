using System.Runtime.InteropServices;
using System.Text;
using ShareHider.Core;

namespace ShareHider;

/// <summary>
/// Matches windows to taskbar buttons by app id, the id the taskbar groups windows by.
/// </summary>
/// <remarks>
/// A button's id (its UI Automation id, after "Appid: ") is either an id the app set
/// itself (Chrome, Store apps, Terminal), or the exe's path, with a known-folder id
/// in place of folders such as System: <c>{1AC14E77-…}\notepad.exe</c>.
/// </remarks>
internal static class AppIds
{
    private const int ErrorInsufficientBuffer = 122;
    private static readonly Guid PropertyStoreIid = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly Native.PropertyKey AppUserModelIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
    private static readonly Dictionary<Guid, string?> KnownFolders = [];

    /// <summary>The app id the window or its packaged app declares, or null when it relies on its exe path.</summary>
    public static string? Declared(nint hwnd) => FromPropertyStore(hwnd) ?? FromPackage(hwnd);

    /// <summary>Whether a taskbar button id belongs to a window with this declared id and exe path.</summary>
    public static bool Matches(string buttonId, string? declaredId, string exePath)
    {
        if (declaredId is not null)
        {
            return string.Equals(buttonId, declaredId, StringComparison.OrdinalIgnoreCase);
        }

        return exePath.Length > 0
            && (string.Equals(buttonId, exePath, StringComparison.OrdinalIgnoreCase)
                || (TryExpandKnownFolder(buttonId, out var path) && string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>For each window, the first button id that belongs to it, if any.</summary>
    public static Dictionary<nint, string> MatchButtons(IEnumerable<WindowInfo> windows, IReadOnlyList<string> buttonIds)
    {
        var result = new Dictionary<nint, string>();
        foreach (var window in windows)
        {
            var declared = Declared(window.Handle);
            if (buttonIds.FirstOrDefault(id => Matches(id, declared, window.ExePath)) is { } id)
            {
                result[window.Handle] = id;
            }
        }

        return result;
    }

    private static string? FromPropertyStore(nint hwnd)
    {
        if (Native.SHGetPropertyStoreForWindow(hwnd, PropertyStoreIid, out var store) != 0 || store is null)
        {
            return null;
        }

        try
        {
            if (store.GetValue(AppUserModelIdKey, out var value) != 0)
            {
                return null;
            }

            try
            {
                return value.VarType == Native.VT_LPWSTR && Marshal.PtrToStringUni(value.Pointer) is { Length: > 0 } id ? id : null;
            }
            finally
            {
                Native.PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    private static string? FromPackage(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        var process = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            return null;
        }

        try
        {
            uint length = 0;
            if (Native.GetApplicationUserModelId(process, ref length, null) != ErrorInsufficientBuffer || length is 0 or > 1024)
            {
                return null; // not a packaged app
            }

            var buffer = new StringBuilder((int)length);
            return Native.GetApplicationUserModelId(process, ref length, buffer) == 0 ? buffer.ToString() : null;
        }
        finally
        {
            Native.CloseHandle(process);
        }
    }

    /// <summary>Turns "{known-folder-id}\rest" into a full path.</summary>
    private static bool TryExpandKnownFolder(string buttonId, out string path)
    {
        path = "";
        var close = buttonId.IndexOf("}\\", StringComparison.Ordinal);
        if (!buttonId.StartsWith('{') || close < 0 || !Guid.TryParse(buttonId[..(close + 1)], out var folderId))
        {
            return false;
        }

        if (!KnownFolders.TryGetValue(folderId, out var folder))
        {
            folder = null;
            if (Native.SHGetKnownFolderPath(folderId, 0, 0, out var pointer) == 0)
            {
                folder = Marshal.PtrToStringUni(pointer);
            }

            Marshal.FreeCoTaskMem(pointer);
            KnownFolders[folderId] = folder;
        }

        if (folder is null)
        {
            return false;
        }

        path = Path.Combine(folder, buttonId[(close + 2)..]);
        return true;
    }
}
