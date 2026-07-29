# 9 — Project Plan

**Author:** Cal Lucas
**Project:** KingStudio Engine (KSE)
**Document version:** 1.0
**Last updated:** 29/07/2026

---

## 9.1 Development Approach: WAgile

KSE was developed, and is documented here, under a **WAgile** approach — a Waterfall skeleton (Setup →
Architecture → Sample Application → Account System) with Agile-style short, working-increment cycles inside
the later phases, rather than either extreme on its own. Full justification, compared against pure Waterfall
and pure Agile, is in `06-Report.md` §6.7. In short: the RHI/Vulkan/Windowing core (Phase 5) was designed
and delivered as one large, planned, sequential unit — genuinely Waterfall in character, since an
abstraction layer that several backends depend on is expensive to redesign mid-flight — while the
account/login system and its follow-ups (Phase 6: login, sign-up, then a week later escape-to-logout, then
client testing) shipped as a series of small, independently working increments, each one usable on its own
— genuinely Agile in character. This isn't a retrofitted label: it is what the commit history in
`04-Development-Log.md` actually shows happened.

## 9.2 Gantt Chart

```mermaid
gantt
    title KSE — Project Plan (WAgile)
    dateFormat YYYY-MM-DD
    axisFormat %d/%m
    todayMarker on

    section Phase 1 — Setup & Initial Architecture
    Repo setup, licensing, placeholder Editor/Engine/Application tree :done, p1, 2026-03-19, 14d

    section Phase 2 — Gap
    No committed activity (exam/coursework load elsewhere) :done, p2, 2026-04-02, 56d

    section Phase 3 — Formal Documentation Pass
    Statement of Intent, Aims, Goals authored :done, p3, 2026-05-28, 7d

    section Phase 4 — Gap
    No committed activity :done, p4, 2026-06-04, 42d

    section Phase 5 — Architecture Rewrite (Waterfall)
    Design RHI interfaces (buffers, textures, pipelines, cmd buffers) :done, p5a, 2026-07-16, 3d
    Implement Vulkan backend + ray-query ray tracing :done, p5b, 2026-07-19, 3d
    Implement Win32 windowing (Engine.Windowing) :done, p5c, 2026-07-19, 2d
    Build Sandbox sample scene + quality tiers :done, p5d, 2026-07-21, 2d
    Direct3D 12 backend (stub, incomplete) :active, p5e, 2026-07-22, 1d

    section Phase 6 — Secure Login/Logout (Agile increments)
    Increment 1: CharInput + PasswordHasher + AccountStore :done, p6a, 2026-07-23, 3d
    Increment 2: LoginScreen UI wired into Program.cs :done, p6b, 2026-07-26, 2d
    Increment 3: Escape-to-logout (RunScene loop) :done, p6c, 2026-07-25, 1d

    section Documentation & Assessment Deliverables
    Docs 01-06 (Intent, Research, Implementation, Log, Timesheet, Report) :done, d1, 2026-07-22, 8d
    Docs 07-08 (Client profile, Testing) :done, d2, 2026-07-29, 1d
    Docs 09-12 (Project Plan, Requirements, Design, Outcome Mapping) + unit tests :active, d3, 2026-07-29, 1d

    section Remaining / Future Work
    Fix Direct3D 12 build error + D3D12 ray tracing (stretch goal) :p7, 2026-07-30, 10d
    Client written sign-off + UAT execution email from Michelle :crit, p8, 2026-07-30, 5d
```

**Reading this chart:** `done`/`active` bars are genuinely in the past or happening today (29/07/2026);
plain and `crit` bars are planned but not yet started. The `crit` bar on the client sign-off/UAT email is
marked critical deliberately — it is the one remaining item in the *Tests* and *Project identification*
deliverables that depends on someone other than the developer (Michelle Chapman, `07-Client.md`) and is
therefore the most at-risk item on this plan if her reply is delayed.

### 9.2.1 Percentage Complete by Task (as of 29/07/2026)

Mermaid's renderer doesn't fill bars by percentage, so completion is recorded explicitly here instead —
this table *is* the "% complete" column a tool like MS Project would show natively:

| Task | % Complete | Notes |
|---|---|---|
| Phase 1 — Setup & initial architecture | 100% | Superseded in Phase 5, not deleted from history — see `01-Statement-of-Intent.md` §1.3. |
| Phase 3 — Formal documentation pass | 100% | Ancestor of `01-Statement-of-Intent.md`. |
| Phase 5 — RHI + Vulkan + Windowing + Sandbox | 100% | |
| Phase 5 — Direct3D 12 backend | ~40% | Compiles as a project but has a build error (`CS0721`, `03-Implementation.md` §3.2); ray tracing path not started. Recorded as "partially met" in `06-Report.md` §6.3, not hidden. |
| Phase 6 — Login/sign-up/logout | 100% | |
| Docs 01–08 | 100% | |
| Docs 09–12 + unit tests | 100% | Completed 29/07/2026 alongside this plan. |
| Direct3D 12 fix (stretch) | 0% | Not started; scoped as optional/stretch, not required for this submission. |
| Client written sign-off + UAT email | 0% | Michelle has completed the testing session itself (`07-Client.md` §7.5), but her written agreement and a formal UAT-results email are still outstanding — see `08-Testing.md` §8.7. |

## 9.3 Weekly Updates

One row per calendar week, Thursday-anchored per `04-Development-Log.md`. Gap weeks are combined into one
row, matching how the development log itself records them, rather than padded out with invented daily
detail.

| Week(s) | Dates | % complete of that period's focus | Summary |
|---|---|---|---|
| Week 1 | 19/03–25/03/2026 | 100% of planned Phase 1 setup | Repository created, licensing settled on a personal-use license, and an initial Editor/Engine/Application placeholder folder structure added — an ambitious early scope later deliberately narrowed. See `04-Development-Log.md` Phase 1. |
| Week 2 | 26/03–01/04/2026 | 100% of planned Phase 1 setup | Folder layout revised; GLFW 3.4 vendored in as a prospective cross-platform windowing layer. This turned out to be a dead end — removed entirely in Phase 5 once Win32-direct windowing was judged simpler for a Windows-only target (`02-Research.md` §2.5). |
| Weeks 3–10 (gap) | 02/04–27/05/2026 | 0% — no repository activity | No commits in this eight-week span. Recorded honestly rather than smoothed over, per `01-Statement-of-Intent.md` §1.7. |
| Week 11 | 28/05–03/06/2026 | 100% of planned documentation pass | Returned to the project to author formal planning documentation (Description, Plan, Progress) — the direct ancestor of the current `Documents/` set. |
| Weeks 12–17 (gap) | 04/06–15/07/2026 | 0% — no repository activity | A second, longer gap. Same honesty note as above applies. |
| Week 18 | 16/07–22/07/2026 | 100% of planned Phase 5 rewrite | The single biggest week in the project: the placeholder Editor/Engine/Application tree was deleted and replaced with the current, actually-compiling `Engine.RHI` / `Engine.RHI.Vulkan` / `Engine.RHI.Direct3D12` / `Engine.Windowing` / `samples/Sandbox` structure, landing in one large commit (`50a5d0b`, "major update"). This was a deliberate scope revision (§9.4) rather than an accident. |
| Week 19 | 23/07–29/07/2026 | 100% of planned Phase 6 + documentation | Added the local account system (salted PBKDF2 hashing, JSON storage, an in-engine login/sign-up screen), then escape-to-logout two days later, then authored the full `Documents/` set (`01`–`12`), added Michelle Chapman as an external client tester, and added the first automated unit tests (`tests/Engine.Tests`). This is also the week this project plan itself was written — see §9.5 for what that implies about its own accuracy. |

## 9.4 Revisions to the Plan

Recorded here because a project plan that never changed would be unusual, and this document set's own
stated approach is to record things as they actually happened (`01-Statement-of-Intent.md` §1.7):

1. **Scope narrowed from a multi-layer game engine (Editor/Engine/Application, with early notes on
   multiplayer and combat) to a smaller RHI + Sandbox foundation**, during Phase 5 (16–22/07/2026). Reason:
   a large tree of empty placeholder files demonstrated less real engineering than a smaller,
   fully-working renderer — see `01-Statement-of-Intent.md` §1.3 and §1.7.
2. **The Direct3D 12 backend was downgraded from a required goal to a partially-met/stretch goal**, once
   it became clear during Phase 5 that finishing a second full graphics backend alongside Vulkan, ray
   tracing, windowing, and the account system was not realistic in the remaining time. Recorded as
   "Partially met" in `06-Report.md` §6.3 rather than either hidden or quietly redefined as "done."
3. **A client tester (Michelle Chapman) was added to the plan in Week 19**, after the account system and
   Sandbox were functionally complete enough to be worth testing from a non-developer's perspective — see
   `07-Client.md`.

## 9.5 A Note on Retrospective Planning

Weeks 1–18 of this plan were written in Week 19, after the fact, from real commit history rather than from
a project plan kept live since March. That is worth stating plainly rather than implying this Gantt chart
was maintained turn-by-turn from day one — it wasn't, and `01-Statement-of-Intent.md` §1.7's honesty
principle applies to this document exactly as much as to any other. From 29/07/2026 onward (the "Docs
09–12" row and everything in §9.2's "Remaining / Future Work" section), this plan is genuinely prospective,
and should be updated live, Thursday by Thursday, from here on — including the % complete column in §9.2.1
— rather than backfilled again later.
