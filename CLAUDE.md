# Math Snip Board – handoff for Claude Code

A single-file Windows app (C# / WinForms / .NET Framework 4.x, **no dependencies**) for a math teacher who
teaches with a **pen tablet**: snip any part of the screen, it opens on a board with space around it, write on it.
Owner/maintainer: Mohaneesh (GitHub `ZInuke21`). Public repo: https://github.com/ZInuke21/math-snip-board

## Current status (as of 2026-11-12)

| | |
|---|---|
| Latest release | **v2.1.1** (2026-11-07) |
| In use by | Mohaneesh's math teacher, daily, Windows laptop + pen tablet |
| Last bug | `OutOfMemoryException` in `Graphics.DrawBezier` while writing (v2.1.0). **Fixed in v2.1.1** – root cause proven on Windows CI (see "GDI+ tiny curves" below) |
| Waiting on | Teacher confirming (a) the Update button upgraded him 2.1.0 → 2.1.1 – first real-world run of the updater swap, (b) the error no longer appears |
| CI | Green on `main`; every push builds + tests on `windows-latest` |

Verified on real Windows (CI): build with the built-in `csc.exe`, 21 self-tests, GDI+ stress probe, updater reaching
the GitHub API, release publishing. Verified on the teacher's PC: app runs, snipping and pen writing work (the crash
stack trace came from the live pen path). **Not yet verified on real hardware:** updater exe swap on Windows,
per-monitor DPI with a projector at different scaling, tray icon + global hotkey behaviour.

## How releases & updates work (most important workflow)

1. Change code → push to `main` (CI builds + tests on Windows automatically).
2. Release: GitHub → **Actions → Build & release → Run workflow**, enter version (`2.1.2`, must be higher) and
   "what's new" (items separated by `;`). Or push a tag `vX.Y.Z` (annotated tag message = notes).
3. `.github/workflows/release.yml` rewrites `src/Version.cs` from the version, builds with `build.cmd`, runs
   `--selftest`, `--gdi-probe`, `--update-check`, then `gh release create` with `MathSnipBoard.exe` + `.zip`.
4. Installed apps check `api.github.com/repos/ZInuke21/math-snip-board/releases/latest` 8 s after start and every
   6 h, show an **Update** button (board toolbar + launcher + tray balloon), and on click: download →
   verify (size, `MZ`, `AssemblyName == "MathSnipBoard"`, version) → rename running exe to `.old` → move new exe in
   → start it with `--updated <pid>` → release single-instance mutex → exit (hard `Environment.Exit` after 2 s).
   New copy waits for the old PID, then deletes `.old`.

**Never change:** the assembly name `MathSnipBoard`, the asset name `MathSnipBoard.exe`, or `Updater.Owner/Repo`
– deployed copies depend on them to find and accept updates.

## Build & test

- **Windows:** `build.cmd` (uses `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, nothing to install).
  Run checks: `MathSnipBoard.exe --selftest <dir>` (writes `<dir>\selftest.log`, exit code 0 = pass).
- **Linux/macOS:** `./build.sh` (Mono `mcs` against `/usr/lib/mono/4.5-api` reference assemblies) and
  `tools/test-linux.sh` (headless via Xvfb).
- **The real test is CI on Windows.** Mono's libgdiplus does not reproduce Windows GDI+ bugs (it passed the
  crash that the teacher hit). Push to a branch and read the job log.

Hidden CLI flags: `--selftest <dir>`, `--gdi-probe <file>`, `--update-check <file>`,
`--parse-release <json> <out>`, `--open <image>`, `--blank`, `--updated <pid>` (internal).
Env var `MATHSNIP_UPDATE_API` overrides the release API URL (for testing the updater against a local server).

## Hard constraints

- **C# 5 only.** The CI/Windows compiler is the legacy .NET Framework `csc.exe`: no `$""`, `?.`, `nameof`,
  `=>` members, `??=`, etc. Check locally with `mcs -langversion:5`.
- **References: only `System`, `System.Drawing`, `System.Windows.Forms`.** No `System.Core` (so no `HashSet`,
  LINQ). No NuGet. Keep it one exe that runs on any stock Windows 10/11.
- **GDI+ tiny curves:** Windows GDI+ throws `OutOfMemoryException` on microscopic Béziers (CI probe: ~16k failures
  in 135k pen-sized curves). `StrokeItem` draws curves smaller than 2.5 device px as straight micro-lines and wraps
  every GDI+ ink call in a fallback. Any new drawing code: guard it, and add cases to `src/Diagnostics.cs`.
- Errors never repeat-popup: `Program.ShowError` shows each distinct error once per session.

## Architecture (`src/`)

| File | What's in it |
|---|---|
| `Program.cs` | `Main`, per-monitor-v2 DPI, single-instance mutex + wake event, crash handler, snip orchestration (hides windows → captures virtual screen → overlay), `Settings` (`%APPDATA%\MathSnipBoard\settings.ini`) |
| `Version.cs` | Assembly attributes; version rewritten by CI |
| `UI.cs` | `Theme` colours/fonts, `Icons` (vector line icons on a 24×24 grid), `ToolButton`, `SwatchButton`, `Divider`, `Toast`, `LogoMark`, `ScrollPanel` |
| `Overlay.cs` | `SnipOverlay`: full virtual-screen dim + crosshair + size label; Enter = whole monitor |
| `Model.cs` | `Item` (immutable once committed) → `StrokeItem` (smooth midpoint Béziers, per-point width), `ShapeItem` (line/arrow/rect/ellipse), `TextItem`, `ImageItem`; `Geo` helpers |
| `Board.cs` | The canvas: page model in page units (= screen px at 100 %), zoom, viewport-sized render cache (visible area + margin), Windows Ink `WM_POINTER` input (coalesced history, HIMETRIC sub-pixel, pressure, eraser end, barrel = eraser), adaptive smoothing, hold-to-perfect line/circle snapping, line/arrow end dots (drag a dot to move that end; Select tool drags the whole line, Delete removes it), partial eraser, laser pointer, select/move/resize pictures (ink on a picture moves with it), text editing, undo/redo (list snapshots), export |
| `Editor.cs` | Board window: toolbar, palettes (pen vs highlighter), shortcuts, copy/save PNG/JPG/PDF, paste/drag-drop images, fullscreen, update button, `RunSelfTest` |
| `Launcher.cs` | Start window, tray icon, global hotkey `Ctrl+Alt+S` (fallback `Ctrl+Alt+Shift+S`), hides while a board is open; `HelpForm` |
| `Updater.cs` | Release check, JSON parsing (regex, no JSON lib), download/verify/swap/restart, `UpdateProgressForm` |
| `Pdf.cs` | Minimal one-page PDF writer (embedded JPEG); `SelfTest.Run` |
| `Diagnostics.cs` | `--gdi-probe`: raw vs fixed ink drawing stress test |

Data flow: input → `Board.BeginInput/MoveInput/EndInput` → live `StrokeItem` painted incrementally into the cache
→ `CommitItem` pushes an undo snapshot. Anything that changes items must push `undo` and call `Changed()`.

## Gotchas learned the hard way

- Mono quirks (only matter for local testing): `CompositingMode.SourceCopy` blits fail on big areas (removed);
  Mono's `ClientSize` includes scrollbars – `Board.ViewWidth()` handles both; custom cursors log X11 errors (harmless).
- `pkill -f "mono MathSnipBoard"` matches your own shell command line – use `pkill -f "^mono "`.
- First self-built exe crashed on Windows because Mono's `mscorlib` was referenced (`Stack<T>` lives in `System.dll`
  on .NET Framework). Always compile against reference assemblies (`build.sh` does) or with Windows `csc`.
- The Claude Code cloud sandbox can push branches/commits but **not tags** and cannot create repos/releases
  directly – releases go through the Actions "Run workflow" button.

## Ideas / backlog (not started)

- Multi-page A4 PDF export (currently one tall page).
- Lasso-select and move ink; copy/paste ink.
- Autosave + crash recovery of open boards.
- Blackboard (dark) page theme; custom grid size; ruler/protractor/compass tools for geometry.
- Handle `WM_DPICHANGED` (moving a board between monitors with different scaling keeps the old UI size).
- Code-sign the exe to avoid the SmartScreen "Windows protected your PC" prompt.

## Repo layout

`src/` code · `build.cmd` / `build.sh` · `icon.ico` (regenerate with `tools/make_icon.py`) ·
`tools/fixtures/` (sample question image, sample GitHub release JSON) · `docs/` (README images) ·
`.github/workflows/release.yml` · local only (gitignored): `builds/` released exes, `_archive/` v1 source.
