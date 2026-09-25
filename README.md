# ShareHider

Windows 11 has no setting to keep a running app off the taskbar. That's a problem
when you share your screen: everyone in the meeting can see every app you have open.

ShareHider is a small tray app. It notices when you start sharing your screen and
removes the taskbar buttons of the apps you picked. When the share ends, it puts them
back. It can also minimize those apps while you share, and restore them afterwards.

[![CI](https://github.com/darthrater78/win_hide_app/actions/workflows/ci.yml/badge.svg)](https://github.com/darthrater78/win_hide_app/actions/workflows/ci.yml)

**[GitHub repository](https://github.com/darthrater78/win_hide_app)** ·
**[Latest release and release notes](https://github.com/darthrater78/win_hide_app/releases/latest)** ·
[Changelog](CHANGELOG.md)

## How it works

- **Detecting a share.** Windows has no API that says "a screen share is active", so
  ShareHider looks for the windows that meeting apps show only while you share:
  - Chrome, Edge, Brave, Vivaldi and Opera: the "*site* is sharing your screen / a
    window / this tab" bar (Google Meet, Teams on the web, and most browser-based meetings)
  - Microsoft Teams: the "Sharing control bar"
  - Zoom: its share toolbar windows

  Detection is checked once a second. Sharing counts as over after 3 seconds with no
  sharing window, so a toolbar that blinks doesn't bring every app back.
- **Hiding.** The chosen apps' taskbar buttons are removed through the shell's own
  taskbar API (`ITaskbarList::DeleteTab`), and put back with `AddTab`. While hiding
  is on, ShareHider re-hides immediately when one of those apps opens a new window,
  and again every second in case Explorer adds a button back.
- **Manual control.** Press **Ctrl+Alt+H** (you can change it), double-click the tray
  icon, or use the tray menu to hide or show right now. A manual choice holds until
  detection sees a share start or end. After that, detection is in charge again.

### Limits worth knowing

- **Hiding the taskbar button doesn't hide the window.** If a hidden app's window is
  on the screen you're sharing, people can still see it. Turn on **Also minimize** to
  get it off the screen as well.
- **Alt+Tab still lists hidden apps.**
- **Detection depends on how each meeting app names its windows**, and an update to
  the app can change that. If your meeting app isn't detected, see
  [Adding detection for another app](#adding-detection-for-another-app), or use the hotkey.
- **Apps running as administrator** can't be minimized from a normal (non-admin)
  ShareHider. Their taskbar buttons may still hide.
- **If ShareHider is killed** (Task Manager, a crash) while apps are hidden, their
  buttons stay hidden until you restore that app's window or restart Explorer. A
  normal exit, logoff or shutdown always restores them.

## Install

1. Download `ShareHider-<version>-win-x64.exe` from the
   [latest release](https://github.com/darthrater78/win_hide_app/releases/latest).
   It is self-contained, so you don't need to install .NET.
2. Check it against the matching `.sha256` file:
   `Get-FileHash .\ShareHider-<version>-win-x64.exe -Algorithm SHA256`
3. Run it. It sits in the tray and needs no admin rights.
4. Optional: to start it with Windows, press Win+R, type `shell:startup`, and put a
   shortcut to the exe in that folder.

The exe isn't code-signed yet, so SmartScreen may warn about it the first time you run it.

## Use

Right-click the tray icon:

| Menu item | What it does |
|---|---|
| Hide apps now | Hide or show right now (same as the hotkey or double-clicking the icon) |
| Auto-detect screen sharing | Turn automatic detection on or off |
| Resume automatic detection | Drop a manual choice and let detection decide |
| Settings… | Pick apps to hide, the minimize option and the hotkey |
| Write window list | Save every visible window to `window-list.txt`, for adding detection (below) |
| Open log folder | Open `%LOCALAPPDATA%\ShareHider` |

In **Settings**, add apps by exe name (for example `outlook.exe`) or pick one from the
list of running apps.

ShareHider never shows a pop-up notification when sharing starts or stops, because
that pop-up would appear on the screen you're sharing.

## Settings file

Settings live in `%APPDATA%\ShareHider\settings.json`:

```json
{
  "HiddenApps": ["outlook.exe", "spotify.exe"],
  "AutoDetect": true,
  "MinimizeWindows": true,
  "Hotkey": "Ctrl+Alt+H",
  "CustomSignatures": []
}
```

If the file is malformed or larger than 256 KB, ShareHider ignores it, uses the
defaults, and writes a note to the log. Invalid entries are dropped when the file loads.

### Adding detection for another app

1. Start a screen share in the app.
2. From the tray menu, choose **Write window list**. The log folder opens.
3. In `window-list.txt`, find the window that appeared for the share. Each line reads
   `process | class | title`.
4. Add an entry to `CustomSignatures`, then restart ShareHider:

```json
"CustomSignatures": [
  { "Name": "Slack huddle", "ProcessNames": ["slack.exe"], "TitleContains": "is sharing" }
]
```

A signature needs at least one process, plus a `TitleContains` (a case-insensitive
substring) and/or a `ClassName` (an exact match).

## Privacy and security

- **Nothing leaves your machine.** ShareHider makes no network connections.
- **It runs with normal user rights** (`asInvoker`). It never asks for elevation.
- **Win32 calls load only from System32**, never from the exe's folder or `PATH`.
- **What's stored on disk:**
  - `settings.json`: the exe names you chose, your options and the hotkey. It isn't
    encrypted, because it holds no secrets.
  - `log.txt`: app start and stop, the processes whose windows were hidden, and
    detection events. It never contains window titles, and it is capped at about
    2 MB (the current log plus one rotated file).
  - `window-list.txt`: written only when you choose **Write window list**, and
    overwritten each time. It holds window titles, which can include document
    names or email subjects. Delete it whenever you like.
- **What it changes in other apps:** it removes and restores their taskbar buttons,
  and minimizes and restores their windows. It doesn't inject code into them or
  change their window styles.

## Build from source

You need the .NET 10 SDK. It builds on Windows or Linux, but the exe only runs on Windows.

```bash
scripts/build.sh           # locked restore (with NuGet vulnerability audit), build, test
scripts/build.sh publish   # ...and produce dist/ShareHider-<version>-win-x64.exe + .sha256
```

| Project | Purpose |
|---|---|
| `src/ShareHider.Core` | Platform-neutral logic: settings, hotkey parsing, share signatures, hide state |
| `src/ShareHider` | The Windows tray app: Win32 interop, taskbar control, UI |
| `tests/ShareHider.Core.Tests` | xUnit tests for the core, which run on any OS |
