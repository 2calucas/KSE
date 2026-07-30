# KingStudio Engine (KSE)

KSE is a personal rendering-engine project: a backend-agnostic GPU abstraction layer (`Engine.RHI`), a
working Vulkan implementation of it (including ray-query-based ray tracing), a minimal native Win32
windowing layer, and a sample application (`run_test.exe`) that puts it all together — a small real-time
3D scene with shadow mapping, selectable quality tiers, and, when the GPU supports it, ray-traced shadows,
reflections, and global illumination.

## Main file

Use this to find the main file for `run_me.exe` for further discovery of the application.
`KSE`/`samples`/`Sandbox`/`Program.cs`

## What's included

- **`Engine.RHI`** — interfaces for GPU buffers, textures, pipelines, resource sets, command
  buffers/queues, swap chains, and ray-tracing acceleration structures, with no dependency on a specific
  graphics API.
- **`Engine.RHI.Vulkan`** — a complete Vulkan implementation of the above.
- **`Engine.RHI.Direct3D12`** — an in-progress Direct3D 12 implementation. It does not currently compile
  and its ray-tracing path is unfinished — documented honestly in `Documents/03-Implementation.md` rather
  than hidden.
- **`Engine.Windowing`** — a native Win32 window, built directly on P/Invoke (no third-party windowing
  library).
- **`samples/Sandbox`** — the test application: a spinning cube, ground plane, mirror/bounce spheres, and
  an indoor room, with a free-fly camera (WASD + mouse-look), a directional sun light with shadow mapping,
  Low/Medium/High/Ray-Tracing quality tiers, and on-screen performance/input overlays.
- **A local login/sign-up system** — before the 3D scene loads, `run_test.exe` shows a login/sign-up
  screen (rendered through the engine's own pipeline, not a separate GUI toolkit). Accounts are stored
  locally under `%LOCALAPPDATA%\KSE\accounts.json`; passwords are never stored in plaintext — only a
  per-account random salt and a PBKDF2-HMAC-SHA256 hash (600,000 iterations). See
  `Documents/02-Research.md` §2.7 for why this algorithm was chosen. Press **Esc** at any point in the
  scene to log out and return to the login screen without closing the app.

## Documentation

Full project documentation — statement of intent, research, architecture and a file-by-file explanation
of the entire codebase, a development log tied to real commit history, and a timesheet — lives in
[`Documents/`](Documents/):

1. [`01-Statement-of-Intent.md`](Documents/01-Statement-of-Intent.md)
2. [`02-Research.md`](Documents/02-Research.md)
3. [`03-Implementation.md`](Documents/03-Implementation.md)
4. [`04-Development-Log.md`](Documents/04-Development-Log.md)
5. [`05-Timesheet.md`](Documents/05-Timesheet.md) (+ [`Timesheet.csv`](Documents/Timesheet.csv))
6. [`06-Report.md`](Documents/06-Report.md) — project evaluation, plus a flowchart/pseudocode deep dive
   into `Program.cs`, the hardest script in the codebase to follow
7. [`07-Client.md`](Documents/07-Client.md) — the external client tester and the brief they were given
8. [`08-Testing.md`](Documents/08-Testing.md) — functional test cases, performance results, and the UAT
   script for the client
9. [`09-Project-Plan.md`](Documents/09-Project-Plan.md) — development-approach justification, Gantt chart,
   and weekly % complete updates
10. [`10-Requirements.md`](Documents/10-Requirements.md) — functional/non-functional requirements and
    acceptance criteria
11. [`11-Design.md`](Documents/11-Design.md) — a UML class diagram of the RHI abstraction, and an IPO
    chart covering data security
12. [`12-Outcome-Mapping.md`](Documents/12-Outcome-Mapping.md) — maps HSC Software Engineering outcomes to
    the specific evidence for each

Automated unit tests live in [`tests/Engine.Tests/`](tests/Engine.Tests/) (run with `dotnet test`).

## Running it

**[`run_me.exe`](run_me.exe)**, at the repository root, is a self-contained release build — just download
and double-click it, no .NET or Vulkan SDK installation required (a Vulkan 1.3-capable GPU/driver is still
needed to actually render; ray tracing is optional and only offered when the GPU/driver support
`VK_KHR_ray_query` and `VK_KHR_acceleration_structure`). It's built from the same `samples/Sandbox` source
as the `run_test.exe` development build below, just published as a single win-x64 executable via:

```
dotnet publish samples/Sandbox/Sandbox.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## How to use it

### 1. Log in or create an account

`run_me.exe` (or `run_test.exe`) opens on a login/sign-up screen — nothing else loads until you get past
it:

- **Tab** — switch between the username and password fields
- Type normally to enter text; **Backspace** to correct
- **F2** — toggle between "Login" and "Create Account" mode
- **Enter** — submit the form
- The password field is masked with `•`, and validation/login errors are shown inline. Passwords are never
  stored in plaintext — see the note in [What's included](#whats-included) above.

Accounts are local to your machine (`%LOCALAPPDATA%\KSE\accounts.json`); there's no server, so an account
created on one machine won't carry over to another.

### 2. Move around the scene

Once you're logged in, the 3D scene loads with a free-fly camera:

| Input | Action |
|---|---|
| **W A S D** | Move forward / left / back / right |
| Hold **Right Mouse Button** | Mouse-look (moves the camera view while held) |
| **Space** / **Ctrl** | Move up / down |
| **Shift** | Sprint |
| **Up** / **Down** arrow | Cycle the quality tier: Low → Medium → High → Ray Tracing (if the GPU/driver support it) |
| **Esc** | Log out — tears down the scene and returns to the login screen without closing the app |
| Close the window | Exit the application entirely |

The starting area has a spinning cube, a ground plane, and two spheres (one a fixed mirror, one that
oscillates between mirror and matte). Fly out to roughly **x = 25** to find an enclosed indoor room with a
moving point light and more reflective surfaces — a good place to compare Low/Medium/High against Ray
Tracing, since it has more indirect light and reflections for ray tracing to pick up.

### 3. Read the on-screen overlays

Three overlays are drawn on top of the scene:

- **Quality-tier picker** (corner-anchored) — shows the currently selected tier and which ones are
  available on your GPU.
- **Performance HUD** — FPS, frame time, 1%/10% low frame rates, GPU usage, and VRAM use.
- **Input HUD** — a live readout of which movement keys are held, current mouse delta, and camera
  position/yaw/pitch — mainly useful for debugging input issues.

## Building from source

Open `Engine.slnx` or run `dotnet build samples/Sandbox/Sandbox.csproj`, then run the resulting
`run_test.exe` from `samples/Sandbox/bin/x64/Debug/net10.0-windows/`.

## License

Personal, non-commercial use only — see [`LICENSE.md`](LICENSE.md).
