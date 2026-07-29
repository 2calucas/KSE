# KSE (KingStudio Engine) — Statement of Intent

**Author:** Cal Lucas
**Project:** KingStudio Engine (KSE)
**Repository:** https://github.com/2calucas/KSE
**Document version:** 1.0
**Last updated:** 22/07/2026

---

## 1.1 Statement of Intent

This document set covers the development of KSE (KingStudio Engine), a personal software engineering
project built to deepen my understanding of how real-time rendering engines work at a low level —
specifically, how a single piece of application code can drive two completely different graphics APIs
(Vulkan and Direct3D 12) through one shared abstraction layer, and how a game engine authenticates and
protects its users' data.

The project builds on prior coursework experience and introduces new technical challenges: GPU resource
management, cross-API abstraction design, real-time shadow mapping, hybrid rasterisation/ray-tracing,
and — as of this update — secure local account creation and login.

## 1.2 Purpose of This Document Set

This documentation exists to record *why* KSE is built the way it is, not just *what* it does. It is a
development log and technical reference, not a tutorial or a marketing page. It is intended to let an
assessor (or my future self) follow the project from its first commit to its current state, understand
the reasoning behind each architectural decision, and see an honest account of what works, what doesn't
yet, and what was learned along the way.

The document set consists of:

| Document | Purpose |
|---|---|
| `01-Statement-of-Intent.md` | This document — aim, goals, scope |
| `02-Research.md` | Research into comparable engines, APIs, and languages that informed KSE's design |
| `03-Implementation.md` | Architecture overview and a file-by-file explanation of every source file |
| `04-Development-Log.md` | A weekly log of development activity, tied to real commit history |
| `05-Timesheet.md` (+ `Timesheet.csv`) | Logged/estimated hours across the project timeline |
| `06-Report.md` | Evaluation against the goals in §1.5, plus a flowchart/pseudocode deep dive into the hardest script to follow, plus the Assessment Task 2 development-justification report (§6.7) |
| `07-Client.md` | The external client tester who reviewed the finished build, and the brief they were given |
| `08-Testing.md` | Functional and performance testing, plus the UAT script for the client to execute |
| `09-Project-Plan.md` | Development-approach justification, Gantt chart, and weekly % complete updates |
| `10-Requirements.md` | Functional requirements, non-functional requirements, and acceptance criteria |
| `11-Design.md` | Design documents: a UML class diagram of the RHI abstraction, and an IPO chart covering data security |
| `12-Outcome-Mapping.md` | Maps HSC Software Engineering outcomes (SE-12-01–09) to the specific evidence for each |

## 1.3 Project Overview

KSE is **a rendering engine foundation**, not a finished game or a finished editor. Concretely, at the
time of writing, the repository contains:

- **`Engine.RHI`** — a hardware-abstraction interface layer (an "RHI", the same category of design used
  by Unreal Engine and Diligent Engine) describing GPU concepts — buffers, textures, pipelines, resource
  sets, command buffers/queues, swap chains, and ray-tracing acceleration structures — without committing
  to a specific graphics API.
- **`Engine.RHI.Vulkan`** — a working implementation of that interface on top of the Vulkan API, including
  ray-query-based ray tracing (shadows, reflections, and multi-bounce global illumination).
- **`Engine.RHI.Direct3D12`** — an in-progress implementation of the same interface on Direct3D 12. It
  does not currently compile (see Known Issues in `03-Implementation.md`) and its ray-tracing path is an
  unfinished stub. This is documented honestly rather than glossed over, because recording exactly this
  kind of in-progress/broken state is one of the stated purposes of this log.
- **`Engine.Windowing`** — a minimal Win32 windowing layer (raw P/Invoke, no third-party windowing
  library) providing the native window handle the RHI needs to create a swap chain, plus keyboard/mouse
  input.
- **`samples/Sandbox`** — a test application (built as `run_test.exe`) that exercises the engine: a
  small 3D scene (spinning cube, ground plane, mirror/bounce spheres, an indoor room) with a directional
  sun light and shadow map, a free-fly camera, adjustable quality tiers, and — when the GPU supports it —
  ray-traced shadows, reflections, and global illumination, alongside on-screen performance/input overlays.
- **As of this update, a local account system**: a login/sign-up screen shown before the scene loads,
  backed by salted-and-hashed local credential storage (see `03-Implementation.md` §3 for the
  cryptographic detail). This was already announced as the next planned feature in the project's own
  README before this work began.

KSE is **not** currently a general-purpose game engine: there is no editor, no ECS, no physics, no audio,
and no networking layer, despite some earlier planning notes describing an eventual multi-layer
Editor/Engine/Application structure resembling Unity or Unreal. That structure was scaffolded early on as
placeholder folders and was deliberately replaced (see the "major update" entry in the development log)
with a smaller, working RHI + windowing + sample-app foundation, on the reasoning that a real, compiling,
runnable renderer is more valuable — and demonstrates more actual engineering — than a large tree of empty
placeholder files.

## 1.4 Aim

The aim of KSE is to build a **correct, working abstraction over modern graphics APIs**, deep enough to
demonstrate real GPU programming (buffers, pipelines, descriptor/resource-set binding, synchronization,
and ray tracing) rather than a simplified toy renderer, while keeping the amount of implemented surface
area small enough that it can be understood, tested, and documented in full by one person.

A secondary aim, specific to this update, is to add a secure local user-account system — login and
sign-up — that protects stored passwords properly (salted, iterated hashing; no plaintext storage) even
though the application has no server or network component to protect against.

## 1.5 Goals

1. **A working cross-API rendering abstraction**
   - Define GPU concepts (buffers, textures, pipelines, resource sets, command buffers, fences,
     acceleration structures) as backend-agnostic interfaces in `Engine.RHI`.
   - Implement those interfaces fully on Vulkan.
   - Implement those interfaces on Direct3D 12 to the extent time allows, documenting what remains
     unfinished rather than hiding it.

2. **A native window and input layer with no external dependency**
   - Create and manage a Win32 window directly via P/Invoke.
   - Support keyboard state polling, mouse-look capture, and window resize events.

3. **A representative real-time rendering sample**
   - Render a small scene using rasterisation with shadow mapping.
   - Add an optional ray-traced path (ray query) for shadows, mirror/glossy reflections, and a bounded
     multi-bounce global-illumination approximation, gated behind runtime GPU capability checks.
   - Provide a selectable quality tier (Low/Medium/High/Ray Tracing) and on-screen performance/debug
     overlays (FPS, frame time, 1%/10% lows, GPU usage, VRAM, live input state).

4. **A secure local account system (this update's addition)**
   - Let a user create a local account (username + password) and log in on subsequent runs.
   - Never store plaintext passwords: use a salted, iterated password-hashing algorithm.
   - Gate the Sandbox's 3D scene behind a successful login, using the engine's own rendering pipeline to
     draw the login/sign-up screen (rather than a separate GUI toolkit), so the feature is genuinely
     "built into the .exe" and not a bolt-on console prompt.

5. **Maintain accurate, continuous documentation**
   - Keep a development log tied to real commit history rather than a smoothed narrative.
   - Explain every source file's purpose so the repository is self-describing to an assessor or a future
     contributor.

## 1.6 Target Audience

This document set is written for:
- An assessor reviewing this as a piece of coursework.
- My future self, returning to the project after a gap (which, as the development log shows, has already
  happened more than once).
- Anyone reading the public repository who wants to understand how the RHI abstraction and the ray-traced
  sample scene work.

## 1.7 Note on Scope and Honesty

Earlier planning notes for this project (see `02-Research.md` and the deleted `Documents/Description`
file visible in git history at commit `968e6ba`) described a much larger scope — multiplayer, combat
mechanics, a full editor — under an earlier working name. None of that is implemented, and this document
set does not claim otherwise. What is documented here is limited strictly to what exists in the
repository as of commit `50a5d0b` plus the login/sign-up feature added alongside this document set.
