# 4 — Development Log

This log is built directly from the project's git commit history (`git log`), not reconstructed from
memory. Every date, commit hash, and message below is real and independently verifiable by running
`git log --format="%ad %h %s" --date=short --reverse` in the repository. Weeks with no commits are shown
as such rather than smoothed over — see `01-Statement-of-Intent.md` §1.7 on honesty of scope, which
applies equally to this log.

The log is organized into six development phases, each containing the weekly breakdown for that phase.
Weeks run Thursday–Wednesday, anchored to the project's actual first commit (19/03/2026, a Thursday).

---

## Phase 1 — Project Setup & Initial Architecture (Weeks 1–2, 19/03/2026 – 01/04/2026)

Initial repository setup, licensing, and a first attempt at engine folder architecture using an empty,
Unity/Unreal-style placeholder tree (`Editor/`, `Engine/`, `Application/` folders with empty stub files)
rather than working code — an initial plan for scope that was later deliberately narrowed (see Phase 5).

**Week 1 (19/03 – 25/03/2026)**
- `4d27d6e` Initial commit
- `5ff3834` Update LICENSE
- `922f688`, `b9d6a04` Update README.md (x2)
- `0ec498e` Updated structure
- `f6d19fd` Add config files and script stubs
- `22918f2` Delete LICENSE → `d6c7295` Add personal-use LICENSE.md (switched from a generic license to the
  project's own personal-use-only terms — see `LICENSE.md`)
- `eb01718` Add placeholder files across project tree (the Editor/Engine/Application stub layout)
- `a6fcab8` Add KSE crash report editor skeleton
- `5f10e75` Revise README to detail KSE features and future growth (the expanded README recovered in
  `02-Research.md`/`01-Statement-of-Intent.md` §1.7)
- `da81219` Delete Placeholder
- `0a3e9b6` Create Window.md, `f3a39e4` Create KSEScriptEditorText.txt — early planning notes for an
  editor window system and a script editor, neither implemented in the current codebase
- `2d00e8d`/`e524526` Support notification stub
- `f762cee` Create python-package-conda.yml, `18e0eb7` Create codeql.yml — CI scaffolding added early,
  before there was C# code for CodeQL to analyse

**Week 2 (26/03 – 01/04/2026)**
- `3764ab0` Folder layout update — added stub locations for a window system and a "SystemReader" live
  performance reader (commit message: *"no code has been entered yet"*)
- `960bfde` Giving GLFW support — vendored GLFW 3.4 as a prospective cross-platform windowing layer (see
  `02-Research.md` §2.5 for why this was later replaced)

## Phase 2 — Gap (Weeks 3–10, 02/04/2026 – 27/05/2026)

No commits recorded in this eight-week period. *(This is a real gap in the repository's history, not an
estimation error — fill in what you were actually doing during this stretch: coursework/exam pressure
from other subjects, informal planning that wasn't committed, or a genuine pause. The next log entry,
Phase 3, shows written planning resuming on 01/06/2026.)*

## Phase 3 — Formal Documentation Pass (Week 11, 28/05/2026 – 03/06/2026)

**Week 11 (28/05 – 03/06/2026)**
- `ef4bb0e` Create Readme, `92f0ed3` Adding Documents — added a `Documents/` folder (`Description`, `Plan`,
  `Progress`) for formal coursework documentation, separate from the code
- `5926251` Documentation Update, `968e6ba` Extended Description of the Description — authored the
  project's Statement of Intent, Aims, and Goals sections (recoverable from git history at commit
  `968e6ba`); this is the direct ancestor of `01-Statement-of-Intent.md` in the current document set

## Phase 4 — Gap (Weeks 12–17, 04/06/2026 – 15/07/2026)

No commits recorded in this six-week period. *(As with Phase 2 — annotate with what actually happened
here if this log is submitted as coursework evidence.)*

## Phase 5 — Architecture Rewrite: RHI, Vulkan, Direct3D 12, Windowing (Week 18, 16/07/2026 – 22/07/2026)

The single largest change in the project's history. The entire placeholder `Editor/`/`Engine/`/
`Application/` folder tree (including the vendored GLFW 3.4 source from Phase 1) was removed and replaced
with the current, much smaller, actually-compiling five-project solution: `Engine.RHI`,
`Engine.RHI.Vulkan`, `Engine.RHI.Direct3D12`, `Engine.Windowing`, and `samples/Sandbox`.

**Week 18 (16/07 – 22/07/2026)**
- `b11073a` Update README.md — posted the outage/roadmap notice ("technical difficulty… will come at
  5:30PM 22/07/2026… Vulkan… Raytracing… logging in and creating an account")
- `50a5d0b` **"major update"** — Vulkan support and the `run_test.exe` player: the RHI interface layer, a
  working Vulkan backend (including ray-query ray tracing), an in-progress Direct3D 12 backend, the Win32
  windowing layer, and the Sandbox sample scene (shadow mapping, quality tiers, performance/input
  overlays) all landed in this single commit
- `c1bb2c4` Merge branch 'main' of https://github.com/2calucas/KSE

This phase is documented in full technical detail in `03-Implementation.md`.

## Phase 6 — Secure Local Login/Sign-Up System (Week 19, 23/07/2026 – 29/07/2026)

The feature promised in the Week 18 README update. Implemented as part of the same session as this
document set:

- Added `CharInput` (WM_CHAR) support to `Engine.Windowing` so text fields can receive properly
  shift/caps-resolved characters instead of raw virtual-key codes.
- Added `PasswordHasher.cs` (salted PBKDF2-HMAC-SHA256), `AccountStore.cs` (local JSON-backed account
  storage under `%LOCALAPPDATA%\KSE\`), and `LoginScreen.cs` (an interactive login/sign-up panel rendered
  through the engine's own pipeline) to `samples/Sandbox`.
- Wired a login gate into `Program.cs`'s `Main()` so the Sandbox's 3D scene does not build or run until a
  user has logged in or created an account.
- Authored this full documentation set (`Documents/01` through `Documents/05`).
- Verified via `dotnet build` that the change compiles cleanly and does not affect the pre-existing,
  unrelated Direct3D 12 build failure (`03-Implementation.md` §3.2).

*(23/07 – 29/07/2026 remaining days: to be completed as they occur — see `05-Timesheet.md` for the
day-by-day template.)*
