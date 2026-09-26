# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- A window with a live copy of your taskbar: click any running app to hide or show its
  taskbar button on demand, and right-click it to add it to a group.
- Saved groups of apps. The active group is hidden while you share your screen and
  restored afterwards; any group can be hidden by hand, from the tray's Groups menu,
  or with its own optional hotkey.
- Windows 11 look (WPF Fluent theme, follows light and dark mode), a new app and tray
  icon, and an amber tray icon while anything is hidden.
- Start with Windows, closing to the tray, and bringing the window back by starting the
  exe again. Every start, including at sign-in, opens the window in front of other windows.
- Every hidden app is restored if ShareHider hits an unexpected error.
- Share detection for browser-based meetings (Chrome, Edge, Brave, Vivaldi, Opera),
  Microsoft Teams and Zoom, plus custom detection signatures in `settings.json`.
- Optional minimizing of hidden apps during a share, restored afterwards.
- Global hotkey (default Ctrl+Alt+H), a tray toggle, and double-clicking the tray icon
  to hide or show by hand.
- Settings for automatic detection, minimizing, the hotkey, and adding apps to a group
  by exe name.
- A diagnostics option that saves the visible-window list to `window-list.txt`.
- Tray menu links to the GitHub project page and the latest release notes.
- A reminder in the window and the tray tooltip while a Zoom meeting is open and no
  share is detected, with a **Hide now** button, since detection can miss a share.
  `CustomMeetingSignatures` in `settings.json` adds other meeting apps.
- **Save window list** also lists the taskbar's buttons, in order.
- Restored taskbar buttons go back to their original place instead of the end of the
  taskbar. The buttons after them blink once while they are put back in order.
- CI: a Windows build check with a smoke test that launches the app and hides and restores
  Notepad's taskbar button, uploading a test exe and screenshots for every push, tag-triggered
  releases (including pre-releases from feature branches), and workflow linting.
