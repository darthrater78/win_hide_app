# Handoff: ShareHider taskbar hider (2026-09-25)

**Goal:** Windows 11 app that hides running apps' taskbar buttons on demand (click in a taskbar mockup) and automatically while a screen share is detected.

**Current state:** `feat/taskbar-hider` @ `c58461a`, pushed. The WPF redesign builds and all 55 tests pass. CI run 36183446613 passed: the app launches on Windows and the UI renders (screenshot reviewed). **Not verified anywhere yet:** that taskbar buttons actually disappear and come back on a real taskbar, and that Teams and Zoom detection works (their window titles are best guesses).

**Gate status:**
- 🔢 VERSION: ⬜ (0.1.0, unreleased)
- 🔨 BUILD: ⏳ waiting on the user's Windows 11 test of the CI artifact
- 🔒 SECURITY: ✅ 2 open, 0 Critical, 0 High
  - M1: exe unsigned; the user chose to ship it unsigned for now
  - M3: GitHub's default branch is `feat/taskbar-hider` (it was the first push to an empty repo); `main` must be pushed and set as the default at release
- 📄 DOCS, 📦 RELEASE, 🚀 SHIP: ⬜

**Mode:** semi-autonomous this session. The next session asks again.

**Key files:**
- `src/ShareHider.Core/HideSelection.cs`: which apps are hidden (auto-hide list + per-app click overrides)
- `src/ShareHider/Controller.cs`: the 1 s loop (detect share → apply to taskbar → refresh mockup) and the window's view model
- `src/ShareHider/TaskbarController.cs`: `ITaskbarList::DeleteTab`/`AddTab`, minimize/restore
- `src/ShareHider/WindowEnumerator.cs`: which windows have taskbar buttons (cloaked windows, Store-app frame hosts)
- `src/ShareHider/MainWindow.xaml`: the Fluent UI and the mockup
- `scripts/build.sh`, `scripts/smoke-test.ps1`, `.github/workflows/`: local build, CI smoke test, tag releases

**Decisions:**
- Only running apps can be hidden: Windows has no supported API for pinned icons or other apps' tray icons.
- The mockup has one button per app. A click hides or shows the app now; a right-click toggles it on the auto-hide list.
- WPF with the Fluent theme, plus a WinForms NotifyIcon for the tray. .NET 10 LTS, self-contained single-file win-x64.
- `main` can't be pushed until the release gates pass, because the enforcement check blocks default-branch pushes.

**Shell environment:** Linux terminal (bash). The user tests on Windows 11 from CI artifacts.

**Next step:** the user tests the CI artifact (https://github.com/darthrater78/win_hide_app/actions/runs/36183446613/artifacts/10885561269). Offered but not yet approved: a CI smoke-test step that hides and restores Notepad's button and screenshots each state.
