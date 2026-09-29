# ShareHider

Windows 11 has no setting to keep a running app off the taskbar. That's a problem
when you share your screen: everyone in the meeting can see every app you have open.

ShareHider shows you a copy of your taskbar. Click any running app in it and that
app's button disappears from the real taskbar; click it again and the button comes
back. You can also save **groups** of apps. ShareHider notices when you start sharing
your screen, hides the apps in the active group, and puts them back when the share
ends. Any group can also be hidden by hand, from the tray menu, or with its own hotkey.
It can minimize hidden apps too, and restore them afterwards.

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
  - Zoom: its "Screen sharing meeting controls" toolbar

  Detection is checked once a second. Sharing counts as over after 3 seconds with no
  sharing window, so a toolbar that blinks doesn't bring every app back.
- **Hiding.** The chosen apps' taskbar buttons are removed through the shell's own
  taskbar API (`ITaskbarList::DeleteTab`), and put back with `AddTab`. While hiding
  is on, ShareHider re-hides immediately when one of those apps opens a new window,
  and again every second in case Explorer adds a button back.
- **Tray icons.** A hidden app's notification-area icon moves into the overflow (the
  **^** flyout), using the same per-icon switch as **Settings → Personalization →
  Taskbar → Other system tray icons**, and moves back when the app is shown. An icon
  you had already put in the overflow is left alone.
- **Keeping each button's place.** Windows always puts a re-added button at the end
  of the taskbar, and has no way to place one. So before hiding, ShareHider reads the
  taskbar's order (through UI Automation, the same interface screen readers use).
  After restoring, it takes the buttons that belong after the restored one off the
  taskbar and adds them back in order, so everything lands where it was. Those buttons
  blink once while that happens.
- **Hiding on demand.** Clicking an app in the taskbar mockup hides or shows it
  straight away, whether or not you're sharing. A hide you choose this way lasts
  until you click the app again or quit ShareHider. A show you choose during a share
  (to let one app from the active group stay visible) lasts until that share ends.
- **Groups.** A group is a saved set of apps, including apps that aren't running yet.
  One group is the active one: it is hidden automatically while you share. **Hide this
  group now**, the tray's **Groups** menu, or the group's own hotkey hides every app in
  a group at once, or shows them all again if they're all hidden. These work like
  clicks in the mockup, so they last until you show the group again.
- **The hotkey.** Press **Ctrl+Alt+H** (you can change it) to hide or show the active
  group right now, whatever detection says. That choice holds until
  detection sees a share start or end. **Resume automatic hiding** drops the hotkey
  choice and every on-demand click at once.
- **Meeting reminder.** Detection can miss a share, for example after a meeting app
  update. So while a Zoom meeting window is open and no share is detected, the window
  shows a reminder with a **Hide now** button, and the tray icon's tooltip says to
  press the hotkey if you share. It is never a pop-up, since that would appear in
  your share.

### Limits worth knowing

- **Hiding the taskbar button doesn't hide the window.** If a hidden app's window is
  on the screen you're sharing, people can still see it. Turn on **Also minimize** to
  get it off the screen as well.
- **Alt+Tab still lists hidden apps.**
- **A restored button goes on the end** when ShareHider can't read the taskbar's order
  (older taskbars), and when it restores everything at sign-out or after an unexpected
  error, where Explorer may not answer. Pinned apps always keep their pinned place.
- **A running app you dragged in among your pinned apps comes back at the end.** A
  re-added button always lands after the last pin, and nothing can move it back in
  among them. Nothing else moves on its account. Pin the app if you want it to keep
  its place, but then its pinned icon stays on the taskbar while it's hidden, the
  way every pinned app's does. Telling pinned buttons apart relies on the English
  taskbar; with another display language, restoring may move those buttons too.
- **A pinned app's icon stays on the taskbar while it's hidden.** Only its running
  windows go, so it looks like the app isn't open. The window warns you when a pinned
  app is chosen for hiding (English taskbar only, like pin detection above).
- **Detection depends on how each meeting app names its windows**, and an update to
  the app can change that. If your meeting app isn't detected, see
  [Adding detection for another app](#adding-detection-for-another-app), or use the hotkey.
- **Apps running as administrator** can't be minimized from a normal (non-admin)
  ShareHider. Their taskbar buttons may still hide.
- **Only running apps can be hidden.** Windows 11 has no supported way to hide a
  pinned icon for an app that isn't running.
- **A hidden app's tray icon is still in the ^ flyout.** Anyone who opens the flyout
  during your share can see it. Windows has no way to remove another app's tray icon
  without breaking it.
- **If ShareHider is killed** (for example from Task Manager) while apps are hidden,
  their buttons stay hidden until you restore that app's window or restart Explorer.
  A normal exit, logoff, shutdown or unexpected error always restores them. Their tray
  icons stay in the overflow until ShareHider next starts, which puts them back.

## Install

1. Download `ShareHider-<version>-win-x64.exe` from the
   [latest release](https://github.com/darthrater78/win_hide_app/releases/latest).
   It is self-contained, so you don't need to install .NET.
2. Check it against the matching `.sha256` file:
   `Get-FileHash .\ShareHider-<version>-win-x64.exe -Algorithm SHA256`
   Optionally, with the [GitHub CLI](https://cli.github.com/), confirm it was built by this
   repository's release workflow:
   `gh attestation verify .\ShareHider-<version>-win-x64.exe --repo darthrater78/win_hide_app`
3. Run it. It needs no admin rights. Closing the window keeps it running in the tray.
4. Optional: turn on **Start ShareHider with Windows** in its settings. It then opens its
   window when you sign in; close the window to leave it in the tray.

The exe isn't code-signed yet, so SmartScreen may warn about it the first time you run it.

## Use

The main window has four parts:

- **Status**: what's hidden and why, plus the **Hide / show active group**
  button (same as the hotkey) and **Resume automatic hiding**.
- **Your taskbar**: every app with a taskbar button right now, grouped per app the
  way Windows groups them, with its real icon.
  - **Click** an app to hide or show its button immediately. A hidden app is faded
    and gets a red badge.
  - **Right-click** it to add it to a group or remove it from one. Apps in the active
    group get a blue badge.
  - Apps **pinned** to your taskbar get a small yellow pin badge. When a pinned app is
    hidden or in a group, a warning above the mockup explains that its icon will stay:
    hiding only hides that it's running. Unpin an app you don't want seen at all.
- **Groups**: pick a group to edit, or make a **New group**. Rename it, give it an
  optional hotkey (for example `Ctrl+Alt+1`), make it the group hidden while sharing,
  and add apps by exe name (for example `outlook.exe`) or remove them with ×. The last
  group can't be deleted.
- **Settings**: automatic share detection, minimizing hidden apps, starting with
  Windows, and the hotkey.

The footer links to this repository, the release notes, the log folder and
**Save window list** (see [Adding detection for another app](#adding-detection-for-another-app)).

Closing the window keeps ShareHider running in the tray. Click the tray icon to open
the window again, or right-click it for quick controls, the **Groups** menu and **Exit**. Starting the exe a
second time also brings the window back. The tray icon turns amber while any app is hidden.

ShareHider never shows a pop-up notification when sharing starts or stops, because
that pop-up would appear on the screen you're sharing.

## Settings file

Settings live in `%APPDATA%\ShareHider\settings.json`:

```json
{
  "Groups": [
    { "Name": "While sharing", "Apps": ["outlook.exe", "spotify.exe"], "Hotkey": null },
    { "Name": "Chat", "Apps": ["slack.exe", "teams.exe"], "Hotkey": "Ctrl+Alt+1" }
  ],
  "ActiveGroup": "While sharing",
  "AutoDetect": true,
  "MinimizeWindows": true,
  "Hotkey": "Ctrl+Alt+H",
  "CustomSignatures": [],
  "CustomMeetingSignatures": []
}
```

`ActiveGroup` names the group hidden while sharing. A group hotkey that is already
taken (by the main hotkey or another group) is dropped. An older settings file with a
`HiddenApps` list becomes one group called **While sharing**. On-demand hides from the
mockup and the group buttons aren't saved, so they end when ShareHider exits.

If the file is malformed or larger than 256 KB, ShareHider ignores it, uses the
defaults, and writes a note to the log. Invalid entries are dropped when the file loads.

### Adding detection for another app

1. Start a screen share in the app.
2. In ShareHider's window, click **Save window list** in the footer. The log folder opens.
3. In `window-list.txt`, find the window that appeared for the share. Each line reads
   `process | class | title`.
4. Add an entry to `CustomSignatures`, then restart ShareHider:

```json
"CustomSignatures": [
  { "Name": "Slack huddle", "ProcessNames": ["slack.exe"], "TitleContains": "is sharing" }
]
```

A signature needs at least one process, plus a `TitleContains` (a case-insensitive
substring) and/or a `ClassName` (an exact match). The class is the safer choice,
because apps translate their window titles.

`CustomMeetingSignatures` takes the same format and matches a window that is open
for the whole meeting, which turns on the meeting reminder for that app.

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
  - `window-list.txt`: written only when you click **Save window list**, and
    overwritten each time. It holds window titles, which can include document
    names or email subjects, and the names of your taskbar buttons. Delete it
    whenever you like.
  - `tray-icons.json`: while apps are hidden, which tray icons ShareHider moved into
    the overflow and each one's original setting, so a restart after a crash can put
    them back. Deleted once they're all back.
  - **Start with Windows** writes one value, `ShareHider`, under
    `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Turning the setting off
    deletes it.
- **What it reads from other apps:** window titles, class names, and each app's exe
  path. It reads the path only to show that app's icon and product name.
- **What it changes in other apps:** it removes and restores their taskbar buttons,
  minimizes and restores their windows, and switches their tray icons between shown
  and the overflow (`IsPromoted` under `HKCU\Control Panel\NotifyIconSettings`). It doesn't inject code into them or
  change their window styles.

## Build from source

You need the .NET 10 SDK. It builds on Windows or Linux, but the exe only runs on Windows.

```bash
scripts/build.sh           # locked restore (with NuGet vulnerability audit), build, test
scripts/build.sh publish   # ...and produce dist/ShareHider-<version>-win-x64.exe + .sha256
```

On Windows, `pwsh scripts/smoke-test.ps1 -Exe dist/ShareHider-<version>-win-x64.exe`
launches the exe next to Notepad, checks that it starts, clicks Notepad's button in the
mockup to hide it from the taskbar and again to bring it back, and saves a screenshot of
each state. The app icons are
generated by `python3 scripts/make_icons.py`, which needs only the standard library.

| Project | Purpose |
|---|---|
| `src/ShareHider.Core` | Platform-neutral logic: settings, hotkey parsing, share signatures, which apps to hide, button order |
| `src/ShareHider` | The Windows app: Win32 interop, taskbar control, the WPF window (Windows 11 Fluent theme) and the tray icon |
| `tests/ShareHider.Core.Tests` | xUnit tests for the core, which run on any OS |

### CI and releases

| Workflow | When it runs | What it does |
|---|---|---|
| `ci.yml` | Every branch push and PR (not Markdown-only changes), or by hand | Runs `scripts/build.sh publish` on Windows, then the smoke test, and uploads the test exe and a screenshot as build artifacts (kept 14 days) |
| `release.yml` | A `v*` tag | Checks that the tag is on the default branch and that CI passed on that commit, then builds the exe with `scripts/build.sh package` in a read-only job. A separate publish job, which runs no project code, re-checks the checksum, signs a build provenance attestation, and creates a GitHub release with the exe, its `.sha256` and notes from `CHANGELOG.md` |
| `dependency-review.yml` | Every PR | Fails if the PR adds a package with any known security advisory |
| `audit.yml` | Mondays, or by hand | Restores with the NuGet vulnerability audit and lists any vulnerable package, to catch advisories published after merge |
| `lint-workflows.yml` | Changes under `.github/workflows/` | Runs actionlint, with shellcheck over the `run:` blocks |
| `lint-scripts.yml` | Changes under `scripts/` | Runs shellcheck on the shell scripts and PSScriptAnalyzer on the PowerShell ones |

Pre-release tags (`v0.2.0-dev.1`, `-alpha.N`, `-beta.N`, `-rc.N`) can be pushed from
any branch. They create a GitHub pre-release with a test exe, and their release
notes come from the changelog's `[Unreleased]` section. Final tags must be on the
default branch and match `<Version>` in `Directory.Build.props`.
