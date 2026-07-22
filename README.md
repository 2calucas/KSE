# KingStudio Engine (KSE)

KSE is a personal rendering-engine project: a backend-agnostic GPU abstraction layer (`Engine.RHI`), a
working Vulkan implementation of it (including ray-query-based ray tracing), a minimal native Win32
windowing layer, and a sample application (`run_test.exe`) that puts it all together — a small real-time
3D scene with shadow mapping, selectable quality tiers, and, when the GPU supports it, ray-traced shadows,
reflections, and global illumination.

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
  `Documents/02-Research.md` §2.7 for why this algorithm was chosen.

## Documentation

Full project documentation — statement of intent, research, architecture and a file-by-file explanation
of the entire codebase, a development log tied to real commit history, and a timesheet — lives in
[`Documents/`](Documents/):

1. [`01-Statement-of-Intent.md`](Documents/01-Statement-of-Intent.md)
2. [`02-Research.md`](Documents/02-Research.md)
3. [`03-Implementation.md`](Documents/03-Implementation.md)
4. [`04-Development-Log.md`](Documents/04-Development-Log.md)
5. [`05-Timesheet.md`](Documents/05-Timesheet.md) (+ [`Timesheet.csv`](Documents/Timesheet.csv))

## Running it

**[`run_me.exe`](run_me.exe)**, at the repository root, is a self-contained release build — just download
and double-click it, no .NET or Vulkan SDK installation required (a Vulkan 1.3-capable GPU/driver is still
needed to actually render; ray tracing is optional and only offered when the GPU/driver support
`VK_KHR_ray_query` and `VK_KHR_acceleration_structure`). It's built from the same `samples/Sandbox` source
as the `run_test.exe` development build below, just published as a single win-x64 executable via:

```
dotnet publish samples/Sandbox/Sandbox.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Building from source

Open `Engine.slnx` or run `dotnet build samples/Sandbox/Sandbox.csproj`, then run the resulting
`run_test.exe` from `samples/Sandbox/bin/x64/Debug/net10.0-windows/`.

## License

Personal, non-commercial use only — see [`LICENSE.md`](LICENSE.md).
