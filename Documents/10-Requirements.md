# 10 — Requirements

**Author:** Cal Lucas
**Project:** KingStudio Engine (KSE)
**Document version:** 1.0
**Last updated:** 29/07/2026

---

Because KSE was developed under a WAgile approach rather than pure Agile (`09-Project-Plan.md` §9.1), this
is one consolidated requirements document covering the whole project, rather than one smaller document per
sprint — the same convention the assessment brief specifies for Waterfall-leaning projects. Every
requirement below is derived from what is actually implemented (cross-referenced against
`03-Implementation.md` and `06-Report.md`), not aspirational.

## 10.1 Functional Requirements

| ID | Requirement | Implemented in |
|---|---|---|
| FR1 | The system shall let a user create a new local account with a username and password. | `AccountStore.TryCreateAccount`, `LoginScreen.cs` |
| FR2 | The system shall enforce a minimum username length (3 characters) and password length (8 characters) at account creation. | `AccountStore.cs` |
| FR3 | The system shall reject a username that is already taken (case-insensitive). | `AccountStore.cs` |
| FR4 | The system shall let a returning user log in with a previously created username and password. | `AccountStore.TryLogin`, `LoginScreen.cs` |
| FR5 | The system shall reject an invalid login (wrong password or unknown username) with one generic error message, so a failed attempt cannot be used to discover which usernames exist. | `AccountStore.TryLogin` |
| FR6 | The system shall mask password input on-screen (shown as `•`). | `LoginScreen.DrawField` |
| FR7 | The system shall gate the 3D scene behind successful authentication — nothing scene-related loads until `IsAuthenticated` is true. | `Program.cs` (`RunLoginGate`) |
| FR8 | The system shall let an authenticated user log out (Esc) and return to a fresh login screen without closing the application. | `Program.cs` (`RunScene`, Phase 6 follow-up) |
| FR9 | The system shall render a real-time 3D scene (cube, ground plane, mirror/bounce spheres, an indoor room) navigable with a free-fly camera. | `Program.cs`, `Shaders.cs` |
| FR10 | The system shall provide at least four selectable rendering quality tiers (Low/Medium/High/Ray Tracing), switchable at runtime via Up/Down. | `Program.cs` (`RenderScaleFor`, `ShadowResolutionFor`) |
| FR11 | The system shall render dynamic shadows via shadow mapping on every quality tier. | `Program.cs` shadow pass, `Shaders.cs` |
| FR12 | When the GPU supports `VK_KHR_ray_query` + `VK_KHR_acceleration_structure`, the system shall render ray-traced shadows, reflections, and a bounded multi-bounce global-illumination approximation. | `VulkanAccelerationStructure.cs`, `Shaders.FragmentRT` |
| FR13 | The system shall display on-screen performance overlays (FPS, frame time, 1%/10% lows, GPU usage, VRAM) and a live input-state overlay. | `StatsOverlay.cs`, `InputOverlay.cs` |
| FR14 | The system shall handle window resize events without crashing, recreating render targets as required. | `Program.cs` (`RecreateSceneTargets`), `VulkanSwapChain.Resize` |

## 10.2 Non-Functional Requirements

| ID | Category | Requirement | Status / Evidence |
|---|---|---|---|
| NFR1 | Security | Passwords must never be stored or transmitted in plaintext. Stored using a salted, iterated password-hashing algorithm (PBKDF2-HMAC-SHA256, ≥600,000 iterations, unique random 128-bit salt per account). | **Met** — `PasswordHasher.cs`; verified by `tests/Engine.Tests/PasswordHasherTests.cs`. |
| NFR2 | Performance | The rendering sample should sustain a real-time frame rate (target ≥60 FPS) on the reference hardware. | **Met** — observed ~165 FPS average (vsync-capped), 1% lows 50–70 FPS; see `08-Testing.md` §8.4. |
| NFR3 | Portability | Must run on any Windows 10/11 x64 machine with a Vulkan 1.3-capable GPU; ray tracing must be optional, not a hard requirement. | **Met** — `02-Research.md` §2.3; capability probed at runtime, falls back to shadow-mapped rasterisation if absent. |
| NFR4 | Maintainability | The codebase should be divided into clearly separated, independently understandable modules (interfaces/classes), each documented. | **Met** — five-project solution (`Engine.RHI`, `Engine.RHI.Vulkan`, `Engine.RHI.Direct3D12`, `Engine.Windowing`, `Sandbox`); every file explained in `03-Implementation.md`. |
| NFR5 | Usability | The application should be operable by a first-time user with reasonable on-screen guidance, without requiring developer supervision. | **Partially met** — Michelle Chapman (71, limited recent keyboard-and-mouse experience) completed login/account creation unaided, but needed continuous guidance for in-scene movement; see `07-Client.md` §7.5. Recorded honestly as partially met rather than rounded up, per `01-Statement-of-Intent.md` §1.7. |
| NFR6 | Data privacy | No account data should ever leave the local machine. | **Met by design** — the application has no network layer; `accounts.json` lives only under `%LOCALAPPDATA%\KSE\` (`02-Research.md` §2.7, `11-Design.md` §11.3). |

## 10.3 Acceptance Criteria

Each functional requirement is considered accepted when its corresponding test case(s) in
`08-Testing.md` §8.3 pass:

| Requirement | Acceptance criterion | Verified by |
|---|---|---|
| FR1 | Submitting a username (≥3 chars) and password (≥8 chars) via the sign-up form creates an account and immediately loads the scene. | TC01 |
| FR2 | Submitting a username <3 chars or password <8 chars shows the corresponding inline error and does not create an account. | TC02, TC03 |
| FR3 | Submitting a username that matches an existing account (any case) shows "That username is already taken." | TC04 |
| FR4 | Logging in with a previously created username/password loads the scene. | TC05 |
| FR5 | Logging in with a wrong password or an unknown username shows the same generic error message. | TC06 |
| FR6 | Characters typed into the password field are displayed as `•`. | TC07 |
| FR7 | The scene never renders before `IsAuthenticated` is true; closing the window from the login screen exits without building any scene resources. | TC14 |
| FR8 | Pressing Esc during the scene returns to a fresh login screen; the process keeps running and can log in again. | TC12, TC13 |
| FR9 | The scene renders and the camera responds to WASD/mouse-look/Space/Ctrl. | TC10 (implicit), `07-Client.md` §7.5 |
| FR10 | Pressing Up/Down cycles through Low/Medium/High/Ray Tracing, visibly changing shadow resolution and render scale. | TC10 |
| FR11 | Shadows are visible on every quality tier, including Low. | TC10 |
| FR12 | On ray-tracing-capable hardware, the Ray Tracing tier shows sharp reflections on the mirror sphere. | TC11 |
| FR13 | The stats and input overlays are visible and update live during the scene. | Observed during TC09–TC11 |
| FR14 | Resizing the window does not crash the application and the scene continues rendering at the new size. | TC09 |

Non-functional requirements are accepted per the "Status / Evidence" column in §10.2 above, since they are
evaluated by measurement/observation (performance, usability) or by design/code inspection (security,
portability, maintainability, privacy) rather than a single pass/fail test case.
