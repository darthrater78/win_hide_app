# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Tray app that hides chosen apps' taskbar buttons while you share your screen,
  and restores them afterwards.
- Share detection for browser-based meetings (Chrome, Edge, Brave, Vivaldi, Opera),
  Microsoft Teams and Zoom, plus custom detection signatures in `settings.json`.
- Optional minimizing of hidden apps during a share, restored afterwards.
- Global hotkey (default Ctrl+Alt+H), a tray toggle, and double-clicking the tray icon
  to hide or show by hand.
- Settings window: pick apps by exe name or from the running apps, and set the
  minimize option and the hotkey.
- A diagnostics option that saves the visible-window list to `window-list.txt`.
- Tray menu links to the GitHub project page and the latest release notes.
- CI: a Windows build check that uploads a test exe for every push, tag-triggered
  releases (including pre-releases from feature branches), and workflow linting.
