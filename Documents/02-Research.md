# 2 — Research

This section covers the research that shaped KSE's technical decisions: comparable engines and rendering
abstractions, the graphics APIs available and why Vulkan was prioritised over Direct3D 12 and OpenGL,
the languages and libraries used, the windowing approach (including an abandoned GLFW attempt), and the
security research behind the login/sign-up feature added in this update.

> Note: this section documents research that actually informed decisions visible in the codebase and
> commit history. Earlier planning notes (see the deleted `Documents/Description`, recoverable from git
> history) also researched game-specific topics such as multiplayer netcode and combat design under an
> earlier project scope. None of that was carried forward into the current codebase, so it is not
> repeated here — see `01-Statement-of-Intent.md` §1.7.

## 2.1 Comparable Engines and Rendering Abstractions

| Project | Relevance to KSE |
|---|---|
| **Unreal Engine (RHI layer)** | The direct naming inspiration for `Engine.RHI`. Unreal's RHI abstracts D3D11/D3D12/Vulkan/Metal behind one interface so the renderer is written once. KSE copies this idea at a much smaller scale: one interface (`IGraphicsDevice`, `ICommandBuffer`, etc.), multiple backends. |
| **Diligent Engine** | An open-source, MIT-licensed RHI abstraction over D3D11/D3D12/Vulkan/Metal/OpenGL. Closest existing project to what `Engine.RHI` is attempting; useful as a reference for descriptor/resource-set naming (its `IShaderResourceBinding` maps closely to KSE's `IResourceSet`). |
| **bgfx** | A cross-platform rendering library with a similar "describe once, run on many backends" philosophy, but C-style API rather than object-oriented interfaces. Read for API-shape ideas, not copied directly. |
| **The Forge** | A AAA-oriented cross-platform renderer with explicit ray-tracing abstraction across DXR and Vulkan ray tracing — relevant reference for how `IAccelerationStructure`/BLAS/TLAS naming could map across backends. |
| **Godot (RenderingDevice)** | Godot 4's `RenderingDevice` is a similar Vulkan-first abstraction later back-ported to D3D12/Metal, and a useful precedent for "build the Vulkan backend first, treat the others as followers" — the same order KSE took. |
| **Hazel Engine** | A widely-referenced open-source/YouTube-documented learning engine (by The Cherno). Referenced early in project planning as an example of a solo/small-team engine built for learning rather than shipping a commercial product, which matches KSE's own stated purpose. |

The common thread across all of these is the same core idea KSE adopts: define GPU resources and
operations as interfaces first, then write one implementation per graphics API against that interface,
so the rest of the application (in KSE's case, `samples/Sandbox`) never touches Vulkan or D3D12 types
directly.

## 2.2 Graphics APIs

| API | Considered | Outcome |
|---|---|---|
| **Vulkan** | Yes | **Chosen as the primary/working backend.** Explicit, verbose, but gives direct control over memory, synchronization, and descriptor binding — the things an RHI abstraction needs to model precisely. Wide GPU/driver support (NVIDIA, AMD, Intel) including on the development machine. Ray tracing available through the `VK_KHR_ray_query` + `VK_KHR_acceleration_structure` extensions, which (unlike the pipeline-based `VK_KHR_ray_tracing_pipeline`) can be called from an ordinary fragment shader — the approach KSE's `FragmentRT` shader uses for shadows/reflections/GI. |
| **Direct3D 12** | Yes | **Attempted, currently incomplete.** Conceptually very close to Vulkan (explicit command lists, descriptor heaps, barrier-based synchronization), which is exactly why it was picked as the second backend — most of the abstraction work done for Vulkan should transfer. In practice the backend does not currently compile (see `03-Implementation.md`, Known Issues) and its ray-tracing path (DXR — `ID3D12GraphicsCommandList4`/`DXR 1.1`) was never finished. Kept in the repository rather than deleted, because an honest account of "started, not finished" is more useful to this documentation than pretending it doesn't exist. |
| **OpenGL** | Considered, rejected for now | Listed as a possible `RhiBackend` enum value for future compatibility (older/integrated GPUs), but no implementation exists. Rejected as a near-term target because its global-state binding model doesn't map cleanly onto the explicit resource-set/command-buffer model the RHI already committed to for Vulkan/D3D12. |
| **Metal** | Not considered | Out of scope — the project targets Windows only (see Hardware, §2.3). |

## 2.3 Hardware

| Category | Detail |
|---|---|
| Development machine | Windows 11 PC with a discrete GPU exposing `VK_KHR_ray_query`, `VK_KHR_acceleration_structure`, and `VK_KHR_deferred_host_operations` (checked at runtime in `VulkanGraphicsDevice`; ray tracing silently disables itself and falls back to shadow-mapped rasterisation if any are missing — see `Program.cs`'s `rtSupported` flag). |
| Target hardware | Any Windows 10/11 x64 machine with a Vulkan 1.3-capable GPU. Ray tracing is optional at runtime, not a hard requirement, by design. |
| School/lab hardware | Where lab machines lack a ray-tracing-capable GPU or up-to-date drivers, the engine still runs via the non-ray-traced fragment shader (`Shaders.Fragment`) and shadow-map-only shadows — this fallback was a deliberate design requirement, not an afterthought, precisely because hardware availability outside the development machine could not be guaranteed. |

## 2.4 Languages

| Language | Where used | Why |
|---|---|---|
| **C#** (.NET 10, `net10.0`/`net10.0-windows`, `unsafe` blocks enabled) | All engine and sample code | Memory-safe by default but with `AllowUnsafeBlocks` enabled where raw GPU memory access is unavoidable (`Span<byte>` buffer mapping, `MemoryMarshal`). Modern C# (`ImplicitUsings`, collection expressions `[...]`, `readonly record struct` for lightweight GPU descriptors) keeps the RHI's many small descriptor types concise. Chosen over C++ specifically so the project could focus research time on API/architecture design rather than manual memory management and build-system complexity — the tradeoff being reliance on P/Invoke bindings (Vortice.Windows) for the actual native graphics/window calls. |
| **HLSL** | All shaders (`samples/Sandbox/Shaders.cs`) | Written once per shader and compiled twice by the same DXC compiler (`Vortice.Dxc`) — once to DXIL for Direct3D 12, once to SPIR-V for Vulkan (`GenerateSpirv = true`) — so shader logic is not duplicated between backends. This is the same reasoning that drove the RHI interface split: one source of truth, multiple compiled targets. |
| **C/C++** | Considered for a windowing layer via GLFW; not used in the final design (see §2.5) | GLFW itself is C. |
| **Python** | Referenced in early planning documents and a `python-package-conda.yml` GitHub Actions workflow | Left over from earlier, broader project planning (see §2.6); no Python code exists in the current engine. The workflow file remains in `.github/workflows/` but has no corresponding source to lint/test. |

## 2.5 Windowing: GLFW vs. raw Win32

An early commit ("Giving GLFW support", 26/03/2026) added a vendored copy of GLFW 3.4 under
`Engine/Core/GLFW/`, intending to use it as a cross-platform windowing layer (GLFW handles window
creation, input, and Vulkan surface creation on Windows/Linux/macOS). This vendored copy was removed
entirely in the July "major update" rewrite, in favour of the current `Engine.Windowing` project, which
implements a Win32 window directly via `System.Runtime.InteropServices` P/Invoke (`Win32Window.cs`).

**Reasoning for the switch:**
- GLFW is a native C library, which would have required either shipping a prebuilt DLL per platform or a
  C build step — friction the project didn't want to carry for a Windows-only target.
- The actual amount of windowing functionality KSE needs (create a window, get its `HWND`, pump messages,
  report resize/keyboard/mouse) is small enough to implement directly against `user32.dll`/`kernel32.dll`
  with .NET's `LibraryImport`-based source-generated P/Invoke, with no native dependency at all.
- Cross-platform support (GLFW's main advantage) isn't currently needed — the project targets Windows
  only (§2.3) — so the portability GLFW offers wasn't worth its packaging cost at this stage.

This is a genuine example of research-through-trial: GLFW was integrated, then deliberately dropped once
its cost/benefit was assessed against the project's actual (Windows-only, small-surface-area) needs.

## 2.6 Third-Party Libraries

| Package | Backend | Purpose |
|---|---|---|
| `Vortice.Dxc` 3.8.3 | Both / Sandbox | .NET bindings to Microsoft's DXC shader compiler — compiles the engine's HLSL shaders to DXIL (D3D12) or SPIR-V (Vulkan) at runtime. |
| `Vortice.Direct3D12` 3.8.2, `Vortice.DXGI` 3.8.3 | Direct3D 12 | .NET COM-interop bindings over the native D3D12/DXGI APIs (device, command lists, root signatures, swap chain). |
| `Vortice.Vulkan` 3.2.3 | Vulkan | .NET bindings over the native Vulkan API. |
| `Vortice.VulkanMemoryAllocator` 1.7.0 | Vulkan | Bindings to AMD's Vulkan Memory Allocator (VMA) — used for all buffer/texture allocation on the Vulkan backend rather than hand-rolled sub-allocation. |
| `Vortice.SPIRV.Reflect` 1.0.6 | Vulkan | Reflects compiled SPIR-V to recover named resource bindings, push-constant size, and vertex input layout — Vulkan's pipeline construction relies on this; D3D12's DXIL reflection path returns numeric register data only, not names (see `03-Implementation.md`). |
| `System.Diagnostics.PerformanceCounter` | Sandbox | Reads the Windows "GPU Engine" performance-counter category for the live GPU-usage debug overlay. |
| `System.Drawing.Common` | Sandbox | GDI+ text/shape rasterisation, used to draw the debug overlays (and, as of this update, the login/sign-up screen) into a texture that the engine then renders as an ordinary alpha-blended quad. |

## 2.7 Security Research (Login/Sign-Up Feature)

The project's own README announced this feature ahead of implementation: *"It will also include support
for logging in and creating an account for password hashing and secure storing."* Research for this
feature focused on password storage, since KSE has no server component and the account store lives
entirely on the local machine.

| Approach | Verdict |
|---|---|
| Plaintext / reversible encryption of passwords | Rejected outright — never acceptable regardless of threat model, per OWASP's Password Storage Cheat Sheet. |
| MD5 / SHA-1 / unsalted SHA-256 | Rejected — fast, unsalted general-purpose hashes are practical to brute-force or rainbow-table today. |
| **PBKDF2-HMAC-SHA256** | **Chosen.** A NIST-recognised (SP 800-132), FIPS-approved key-derivation function, deliberately slow via a configurable iteration count, and available directly in .NET's `System.Security.Cryptography` (`Rfc2898DeriveBytes.Pbkdf2`) with no third-party dependency. Used with a unique random 128-bit salt per account and a 600,000-iteration count in line with current OWASP guidance for PBKDF2-HMAC-SHA256. |
| bcrypt | Considered — a well-regarded, purpose-built password hash, but requires a third-party NuGet package (no first-party .NET implementation) for a benefit (a work-factor cost function) that PBKDF2's iteration count already provides adequately for this project's threat model (a local single-user file, not an internet-facing login endpoint). |
| Argon2id | Considered — currently the strongest general recommendation (memory-hard, resists GPU/ASIC cracking better than PBKDF2), but likewise requires a third-party package and additional tuning (memory/parallelism cost) beyond what a local, single-machine account store needs. Noted here as the natural next step if KSE ever gains networked accounts. |

**Storage decision:** account records (username, salt, hash, iteration count, creation timestamp) are
stored as JSON under the current user's `%LOCALAPPDATA%\KSE\accounts.json` — never inside the repository,
never in plaintext, and never transmitted anywhere, since the application has no network layer. See
`03-Implementation.md` §3 for the exact implementation.

## 2.8 File Types Reference

| Extension | Used for |
|---|---|
| `.cs` | All engine, backend, and sample source code |
| `.csproj` / `.slnx` | .NET project and solution files (`.slnx` is the newer XML-based solution format, replacing `.sln`) |
| `.props` | `Directory.Build.props` — shared MSBuild properties (target framework, nullable/unsafe settings) applied to every project in the solution |
| `.md` | Documentation (this document set, `README.md`, `LICENSE.md`) |
| `.yml` | GitHub Actions CI workflow definitions |
| `.json` | Local account storage (`accounts.json`, introduced this update); VS Code task configuration |

## 2.9 Sources and Tutorials Referenced

The lists below are organized by the technology they informed, rather than kept as one flat bibliography,
since that maps more directly onto which part of KSE each source actually shaped. **Honesty note, in the
same spirit as the estimation labelling in `05-Timesheet.md`:** this is reconstructed from the concrete
APIs, packages, and techniques that actually ended up in the codebase (SPIR-V/DXIL, `Vortice.*`, PBKDF2,
raw Win32 P/Invoke, etc.) and points to the canonical documentation/tutorial for each — it is not a
browser-history log of every page visited during development. Add or replace any specific tutorial,
video, or article you remember using that isn't listed here.

### 2.9.1 Vulkan

- Khronos Group — [Vulkan Registry and Specification](https://registry.khronos.org/vulkan/) — the
  authoritative reference for every Vulkan struct/function used across `Engine.RHI.Vulkan`.
- Khronos Group — [`VK_KHR_ray_query`](https://registry.khronos.org/vulkan/specs/1.3-extensions/man/html/VK_KHR_ray_query.html)
  and [`VK_KHR_acceleration_structure`](https://registry.khronos.org/vulkan/specs/1.3-extensions/man/html/VK_KHR_acceleration_structure.html)
  extension pages — the two extensions `VulkanGraphicsDevice` probes for and that `VulkanAccelerationStructure.cs`/
  `Shaders.FragmentRT` build on.
- [vulkan-tutorial.com](https://vulkan-tutorial.com/) — the standard starting-point walkthrough for
  instance/device/swap-chain/pipeline setup, the same sequence `VulkanGraphicsDevice.cs` and
  `VulkanSwapChain.cs` follow.
- [vkguide.dev](https://vkguide.dev/) — a more modern (dynamic-rendering-first) Vulkan guide, closer to
  what KSE actually uses (`vkCmdBeginRendering`/`EndRendering` rather than legacy render-pass objects).

### 2.9.2 Direct3D 12 and DXR

- Microsoft Learn — [Direct3D 12 Programming Guide](https://learn.microsoft.com/en-us/windows/win32/direct3d12/directx-12-programming-guide) —
  the reference for the command-list/descriptor-heap/root-signature model `Engine.RHI.Direct3D12` is built
  against (see the Known Issues in `03-Implementation.md` §3.2 for its current build status).
- Microsoft — [DirectX-Specs: Raytracing (DXR)](https://microsoft.github.io/DirectX-Specs/d3d/Raytracing.html) —
  the spec the unfinished `D3D12AccelerationStructure` path would need to implement.
- [DirectXShaderCompiler (DXC) repository](https://github.com/microsoft/DirectXShaderCompiler) — the
  compiler `Vortice.Dxc` wraps; used at runtime by `Program.CompileShader` to produce both DXIL and SPIR-V
  from the same HLSL source.

### 2.9.3 Windowing and the Win32 API

- Microsoft Learn — [Windows API index](https://learn.microsoft.com/en-us/windows/win32/api/) — reference
  for the `user32.dll`/`kernel32.dll` functions `Win32Window.cs` P/Invokes directly (window class
  registration, `WndProc`, message pumping).
- [pinvoke.net](https://www.pinvoke.net/) — community reference for Win32 P/Invoke signatures, useful when
  translating a native declaration into the C# `LibraryImport` form KSE uses.
- Microsoft Learn — [Source-generated P/Invoke (`LibraryImport`)](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation) —
  why `Engine.Windowing` can P/Invoke `user32`/`kernel32` with no `DllImport` marshalling boilerplate.
- [GLFW](https://www.glfw.org/) — evaluated and then dropped in favour of raw Win32 (§2.5); its docs were
  the basis for comparison, not something the final `Engine.Windowing` code depends on.

### 2.9.4 Third-Party Bindings and Libraries

- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) — the repository behind
  `Vortice.Vulkan`, `Vortice.Direct3D12`, `Vortice.DXGI`, and `Vortice.Dxc` (§2.6); its own samples were the
  main reference for correct usage of the .NET bindings themselves, as distinct from the native APIs above.
- [VulkanMemoryAllocator (AMD GPUOpen)](https://github.com/GPUOpen-LibrariesAndSDKs/VulkanMemoryAllocator) —
  the allocator `VulkanBuffer.cs`/`VulkanTexture.cs` use via `Vortice.VulkanMemoryAllocator`, instead of
  hand-rolled sub-allocation.
- [SPIRV-Reflect (Khronos)](https://github.com/KhronosGroup/SPIRV-Reflect) — what `VulkanShaderModule.cs`
  uses (via `Vortice.SPIRV.Reflect`) to recover named resource bindings from compiled SPIR-V.

### 2.9.5 Cryptography and Secure Password Storage

- OWASP Foundation — [Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html) —
  the source for rejecting plaintext/reversible storage outright and for the PBKDF2 iteration-count
  guidance `PasswordHasher.cs` follows (§2.7).
- NIST — [Special Publication 800-132](https://csrc.nist.gov/publications/detail/sp/800-132/final),
  Recommendation for Password-Based Key Derivation — the standard behind choosing PBKDF2-HMAC-SHA256.
- Microsoft Learn — [`Rfc2898DeriveBytes`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes) —
  the exact .NET API `PasswordHasher.cs` calls (`Rfc2898DeriveBytes.Pbkdf2`), avoiding any third-party
  cryptography dependency.

### 2.9.6 C# and .NET

- Microsoft Learn — [.NET documentation](https://learn.microsoft.com/en-us/dotnet/) and
  [C# language reference](https://learn.microsoft.com/en-us/dotnet/csharp/) — general reference for the
  modern C# features used throughout (`readonly record struct`, collection expressions, `Span<byte>`).
- Microsoft Learn — [`System.Text.Json` source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation) —
  the pattern `AccountStore.cs`/`AccountRecordJsonContext` use to (de)serialize `accounts.json` in a
  trimming/AOT-friendly way, relevant since `run_me.exe` is published as a self-contained single file.

### 2.9.7 Comparable Engines (Source Repositories)

Referenced for RHI/rendering-abstraction design precedent (§2.1):

- [Diligent Engine](https://github.com/DiligentGraphics/DiligentEngine)
- [bgfx](https://github.com/bkaradzic/bgfx)
- [The Forge](https://github.com/ConfettiFX/The-Forge)
- [Godot Engine](https://github.com/godotengine/godot) (`RenderingDevice`)
- [Hazel Engine (The Cherno)](https://github.com/TheCherno/Hazel)
