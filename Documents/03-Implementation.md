# 3 — Implementation

## 3.1 Architecture Overview

KSE is a .NET 10 solution (`Engine.slnx`) made of five projects:

```
Engine.RHI            (interfaces only — no graphics API dependency)
   ^        ^
   |        |
Engine.RHI.Vulkan   Engine.RHI.Direct3D12      (one implementation of Engine.RHI per graphics API)
   ^
   |
Engine.Windowing    (native window + input, no RHI dependency)
   ^         ^
   |_________|
        |
samples/Sandbox     (the test application — run_test.exe)
```

`Engine.RHI` defines every GPU concept (`IGraphicsDevice`, `IBuffer`, `ITexture`, `IPipeline`,
`IResourceSet`, `ICommandBuffer`, `ICommandQueue`, `ISwapChain`, `IFence`, `IAccelerationStructure`) as
interfaces and plain descriptor records, with zero dependency on Vulkan or Direct3D types. Each backend
project implements those interfaces against its native API. `Engine.Windowing` is independent of the RHI
entirely — it only needs to hand out a native window handle (`nint`) that the RHI's `CreateSwapChain` can
consume. `samples/Sandbox` is the only project that references everything, and it references only the
**Vulkan** backend, not Direct3D12 (see §3.2 — the D3D12 backend does not currently build).

`Directory.Build.props` applies shared settings to every project: `net10.0` target framework, nullable
reference types on, `unsafe` blocks allowed (needed for raw GPU buffer access via `Span<byte>`), and an
`x64`-only platform (all three native graphics APIs involved are 64-bit).

**This update's addition** — a login/sign-up gate — sits entirely inside `samples/Sandbox` and does not
touch the RHI or backend projects except for one small, generically useful addition to `Engine.Windowing`
(a `CharInput` event, described in §3.3). Control flow in `Program.Main()` is now:

1. Create the window, the Vulkan device, the swap chain, and compile shaders (unchanged from before).
2. **New:** construct an `AccountStore` and a `LoginScreen`, then run `RunLoginGate(...)` — a small
   message-pump loop that renders *only* the login/sign-up panel to the swap chain and feeds keyboard
   input into it, until the user authenticates or closes the window.
3. If the window was closed before authenticating, exit immediately — none of the scene resources
   (meshes, shadow map, acceleration structures) are ever created.
4. Otherwise, proceed exactly as before: build the scene, and run the existing main render loop.

## 3.2 Known Issues

Recorded here deliberately, rather than omitted, per the honesty goal in `01-Statement-of-Intent.md`:

- **The Direct3D 12 backend does not currently compile.** `dotnet build` fails with `CS0721` in
  `D3D12Utils.cs` (line 9): `'ResultCode': static types cannot be used as parameters`. This predates the
  login/sign-up work added in this update and was not introduced or fixed by it.
- **Direct3D 12 ray tracing is an unfinished stub, not a working implementation.** `D3D12GraphicsDevice`,
  `D3D12CommandBuffer`, and `D3D12ResourceSet` all reference a class named `D3D12AccelerationStructure`
  for BLAS/TLAS creation and binding — but no such class exists anywhere in the repository. The Vulkan
  backend's equivalent (`VulkanAccelerationStructure.cs`) is fully implemented.
- **The Sandbox only ever instantiates the Vulkan backend** (`new VulkanGraphicsDevice(...)` in
  `Program.cs`) — the Direct3D 12 backend is not wired into the sample at all, so the build failure above
  does not affect running the actual application.
- **No automated test suite exists.** Correctness has been checked by running the Sandbox and observing
  behaviour, not by unit/integration tests.
- **Swap-chain present/resize forces a full `WaitIdle()`** on both backends — a documented Milestone-1
  simplification (no per-frame semaphore/fence pacing yet), acceptable for a single-window sample but not
  representative of a production frame pacing strategy.

## 3.3 File-by-File Reference

### Repository root

| File | Purpose |
|---|---|
| `Engine.slnx` | The solution file (XML-based `.slnx` format) listing all five projects and their folder grouping (`/src/`, `/samples/`). |
| `Directory.Build.props` | MSBuild properties applied to every project automatically: `net10.0`, implicit usings, nullable reference types, `AllowUnsafeBlocks`, `x64` platform. |
| `LICENSE.md` | A personal-use-only, closed-source license — no commercial use, redistribution, or modification permitted without contacting the copyright holder. |
| `run_me.exe` | A self-contained, single-file Release build of `samples/Sandbox` (`dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true`), placed at the repository root so it can be downloaded and run directly with no .NET runtime installation required. Built from the same source as the `run_test.exe` development build; a Vulkan-capable GPU/driver is still required to render. |
| `README.md` | Project landing page. Prior to this update it was a short outage notice announcing Vulkan support, quality tiers, ray tracing, and the login/sign-up feature documented here. |
| `.gitignore` | Excludes build output (`bin/`, `obj/`, `.vs/`), user-specific files, and a `third_party/FidelityFX-SDK/` path reserved for a future AMD FidelityFX integration that is not yet present. |
| `.vscode/tasks.json` | A VS Code build task for compiling a single active C/C++ file with `clang.exe` — left over from early native-tooling experiments; unrelated to the current C# build (which uses `dotnet build`/the `.slnx`). |
| `.github/workflows/codeql.yml` | GitHub CodeQL security-scanning workflow, configured for `actions` and `python` analysis (not currently `csharp`, despite the project being C#). |
| `.github/workflows/python-package-conda.yml` | A Conda-based Python CI workflow (lint with flake8, test with pytest) left over from earlier, broader project planning that included Python (see `02-Research.md` §2.4); there is no Python source for it to act on. |

### `src/Engine.RHI/` — the backend-agnostic interface layer

| File | Purpose |
|---|---|
| `Engine.RHI.csproj` | Plain class library project; no native/third-party dependencies — this project must stay portable across every backend. |
| `GraphicsDevice.cs` | `IGraphicsDevice` — the root interface: reports backend/capabilities/limits/adapter name, and is the factory for every other GPU object (buffers, textures, pipelines, resource sets, acceleration structures, queues, fences). |
| `Buffers.cs` | `BufferUsage` flags, `MemoryLocation` (device-local vs. host-upload vs. host-readback), `BufferDescriptor`, and `IBuffer` (with `Map()`/`Unmap()` for host-visible memory). |
| `Textures.cs` | `ResourceState` (the barrier-state enum: RenderTarget, ShaderResource, Present, etc.), `TextureDescriptor`/`TextureViewDescriptor`, `ITexture`/`ITextureView`. |
| `Formats.cs` | `TextureFormat` (colour/depth/compressed formats) and `IndexFormat`, plus extension helpers (`IsDepthFormat`, `HasStencil`). |
| `Samplers.cs` | Filtering/address/compare-mode enums, `SamplerDescriptor`, `ISampler`. |
| `Pipelines.cs` | Rasterizer/depth-stencil/blend state records, `GraphicsPipelineDescriptor`/`ComputePipelineDescriptor`, `IPipeline`, and the resource-set-layout/resource-set types (`IResourceSetLayout`, `IResourceSet`) that describe shader binding points. |
| `ShaderReflection.cs` | `ShaderStage`/`ShaderStageFlags`, `ResourceKind`, `VertexFormat`, and `ShaderReflectionInfo` — the backend-agnostic result of reflecting a compiled shader (used differently by each backend; see §3.4/§3.5). |
| `CommandBuffer.cs` | `ICommandBuffer` (render passes, pipeline/resource binding, draw/dispatch, copies, barriers, and `RebuildTopLevelAccelerationStructure`) and `ICommandQueue` (submission, `WaitIdle`). |
| `SwapChain.cs` | `PresentMode`, `SwapChainDescriptor`, `ISwapChain` (`AcquireNextTexture`/`Present`/`Resize`). |
| `Sync.cs` | `IFence` — GPU/CPU synchronization primitive. |
| `AccelerationStructures.cs` | Ray-tracing types: `AccelerationStructureKind`, the `AccelerationStructureTransform` struct (and its `FromEngineMatrix` conversion from the engine's row-vector `System.Numerics.Matrix4x4` to the row-major layout Vulkan/D3D12 both expect), BLAS/TLAS descriptors, and `IAccelerationStructure`. |

### `src/Engine.RHI.Vulkan/` — working backend

Depends on `Vortice.Vulkan` 3.2.3, `Vortice.VulkanMemoryAllocator` 1.7.0 (VMA bindings), `Vortice.SPIRV.Reflect` 1.0.6, and `Vortice.Dxc` 3.8.3.

| File | Purpose |
|---|---|
| `VulkanGraphicsDevice.cs` | Instance/device/queue setup; probes for `VK_KHR_acceleration_structure` + `VK_KHR_ray_query` + `VK_KHR_deferred_host_operations` and only enables `RhiCapabilities.RayQuery` if all three (plus `bufferDeviceAddress`) are present; owns the VMA allocator and one shared `VkDescriptorPool`. |
| `VulkanBuffer.cs` | `VkBuffer` allocated via VMA, with `AutoPreferHost`/`AutoPreferDevice` memory usage chosen from `MemoryLocation`; host-visible buffers are persistently mapped so `Map()` is effectively free. |
| `VulkanTexture.cs` | `VkImage`/`VkImageView` wrapper via VMA; derives aspect flags (color/depth/stencil) from format, and the cube-compatible flag for `TextureCube`. |
| `VulkanSampler.cs` | Thin `VkSampler` wrapper — a real persistent GPU object (unlike D3D12's sampler, see below). |
| `VulkanShaderModule.cs` | Creates `VkShaderModule` from raw SPIR-V and runs SPIRV-Reflect to recover **named** resource bindings, push-constant size, and vertex input locations. |
| `VulkanPipeline.cs` | Builds `VkPipelineLayout`/`VkPipeline` using Vulkan 1.3 dynamic rendering (no render-pass objects) and dynamic viewport/scissor. |
| `VulkanResourceSetLayout.cs` | Wraps `VkDescriptorSetLayout`, mapping `ResourceKind` to `VkDescriptorType` (including acceleration structures). |
| `VulkanResourceSet.cs` | Allocates a `VkDescriptorSet` from the shared pool and writes it via `vkUpdateDescriptorSets`, with a dedicated acceleration-structure write path. |
| `VulkanCommandBuffer.cs` | Render passes via `vkCmdBeginRendering`/`EndRendering`; barriers via `VkImageMemoryBarrier2`/synchronization2; forwards TLAS rebuilds to `VulkanAccelerationStructure`. |
| `VulkanCommandQueue.cs` | Wraps `VkQueue` + one `VkCommandPool`; submits via `vkQueueSubmit2`. |
| `VulkanFence.cs` | `VkFence` wrapper — true resettable boolean semantics. |
| `VulkanSwapChain.cs` | `VkSurfaceKHR`/`VkSwapchainKHR` management; recreates the swap chain in place on `ErrorOutOfDateKHR`/`SuboptimalKHR`. |
| `VulkanAccelerationStructure.cs` | **Fully implemented ray tracing.** BLAS built once from static geometry; TLAS rebuilt every frame (full rebuild, not incremental refit) with instance transforms rewritten into a host-mapped instance buffer, followed by a build→fragment-shader memory barrier. Handles VMA's lack of alignment control for scratch/instance buffers by over-allocating and rounding addresses up manually. |
| `VulkanUtils.cs` | Format/usage/barrier translation tables; `ToBarrierPoint` centrally maps each `ResourceState` to the `(VkImageLayout, VkAccessFlags2, VkPipelineStageFlags2)` triple used for every barrier. |
| `Engine.RHI.Vulkan.csproj` | Project file listing the four Vortice package references above. |

### `src/Engine.RHI.Direct3D12/` — in-progress backend (see Known Issues, §3.2)

Depends on `Vortice.Direct3D12` 3.8.2, `Vortice.DXGI` 3.8.3, `Vortice.Dxc` 3.8.3.

| File | Purpose |
|---|---|
| `D3D12GraphicsDevice.cs` | Creates the DXGI factory/adapter (prefers a non-software, high-performance adapter), `ID3D12Device5`, checks `RaytracingTier1_1` support, and owns four descriptor heaps (CBV/SRV/UAV, Sampler, RTV, DSV) plus the graphics queue. |
| `D3D12Buffer.cs` | `ID3D12Resource` via `CreateCommittedResource`, heap type derived from `MemoryLocation`. |
| `D3D12Texture.cs` | Wraps an owned or swap-chain-borrowed `ID3D12Resource`; the view type additionally allocates RTV/DSV descriptors. |
| `D3D12Sampler.cs` | No native object — D3D12 has no persistent sampler handle; this just precomputes a description materialized into a heap slot at bind time. |
| `D3D12ShaderModule.cs` | Holds compiled DXIL plus `ID3D12ShaderReflection`. DXIL reflection returns register/size data but **no names**, so (unlike the Vulkan backend) nothing here is looked up by name — only used to size the push-constant buffer and recover vertex semantics. |
| `D3D12ResourceSetLayout.cs` | Pure C# bookkeeping (D3D12 has no `VkDescriptorSetLayout` equivalent); precomputes each binding's heap offset. |
| `D3D12ResourceSet.cs` | Writes CBV/SRV/UAV/Sampler descriptors into the shared heaps; its acceleration-structure write path references the missing `D3D12AccelerationStructure` type (§3.2). |
| `D3D12Pipeline.cs` | Builds `ID3D12RootSignature` + `ID3D12PipelineState`; register numbers are assigned by counting bindings in declaration order (since DXIL reflection lacks names), relying on HLSL `register()` clauses matching that convention. |
| `D3D12CommandBuffer.cs` | Wraps `ID3D12GraphicsCommandList4` — render targets, binding, draws/dispatches/copies, barrier transitions; its TLAS-rebuild path also references the missing type. |
| `D3D12CommandQueue.cs` | Wraps `ID3D12CommandQueue` + a single shared command allocator (no per-frame ring) — documented as safe only because every call site already forces a full idle wait first. |
| `D3D12Fence.cs` | Wraps `ID3D12Fence` as a monotonically increasing counter (`Reset()` is a documented no-op — D3D12 fences aren't resettable, unlike Vulkan's). |
| `D3D12SwapChain.cs` | Wraps `IDXGISwapChain3` (`FlipDiscard`); forces a full `WaitIdle()` on present/resize. |
| `D3D12DescriptorAllocator.cs` | A coalescing free-list allocator over one descriptor heap, chosen over a bump allocator because resource sets are destroyed/recreated on quality-tier changes and resizes. |
| `D3D12Utils.cs` | Enum/struct translation tables. **Contains the line that currently fails to compile** (`CS0721`, see §3.2). |

### `src/Engine.Windowing/` — native window + input

| File | Purpose |
|---|---|
| `Engine.Windowing.csproj` | Plain class library; no third-party dependency (see `02-Research.md` §2.5 for why GLFW was tried and dropped). |
| `IWindow.cs` | `IWindow` interface: handle/size/`ShouldClose`, `Resized`/`KeyDown` events, focus/key-state polling, mouse capture and delta polling, and message pumping. **This update adds** a `CharInput` event for resolved text-character entry (see below). |
| `Win32Window.cs` | The only implementation, built directly on `user32.dll`/`kernel32.dll` P/Invoke (no GLFW, no WinForms/WPF). Registers a window class, handles `WM_CLOSE`/`WM_DESTROY`/`WM_SIZE`/`WM_KEYDOWN` in its `WndProc`. **This update adds** `WM_CHAR` handling (`0x0102`), raising the new `CharInput` event with the already shift/caps-lock-resolved character — used by `LoginScreen` for username/password text entry instead of reasoning about virtual-key codes and shift state itself. |

### `samples/Sandbox/` — the test application (`run_test.exe`)

| File | Purpose |
|---|---|
| `Sandbox.csproj` | Executable project (`net10.0-windows`, `AssemblyName=run_test`); references `Engine.RHI`, `Engine.RHI.Vulkan`, and `Engine.Windowing` (not the D3D12 backend); pulls in `System.Diagnostics.PerformanceCounter` and `System.Drawing.Common`. |
| `Program.cs` | Entry point. Compiles shaders, creates the device/swap chain, **runs the new login/sign-up gate (`RunLoginGate`) before building any scene resources**, then builds the shadow map, scene color target, meshes, BLAS/TLAS, and runs the main per-frame render loop (shadow pass → main pass → blit/upscale pass → UI overlay pass). |
| `Camera.cs` | Free-fly camera (Unreal-editor-viewport style): WASD + mouse-look, Space/Ctrl for vertical movement, Shift to sprint. |
| `Cube.cs`, `Plane.cs`, `Quad.cs`, `Sphere.cs`, `Cone.cs` | Procedural mesh generators sharing one interleaved position(3)+normal(3)+uv(2) vertex layout, so every mesh works with the same pipelines and acceleration-structure build path with no asset pipeline needed. |
| `Shaders.cs` | All HLSL shader source, compiled at runtime by DXC to either DXIL or SPIR-V (see `02-Research.md` §2.4): the main forward-pass vertex shader, a non-ray-traced fragment shader (shadow-map shadows only), a ray-traced fragment shader (real shadow rays, reflections, and bounded multi-bounce GI via inline ray queries), the depth-only shadow-pass shaders, and the unlit textured-quad UI shaders shared by every overlay **and by the new `LoginScreen`**. |
| `UiOverlay.cs` | Corner-anchored quality-tier picker (Low/Medium/High/Ray Tracing/…), text rasterized via GDI+ into a texture and drawn as an alpha-blended screen-space quad. |
| `StatsOverlay.cs` | Top-left performance HUD (FPS, 1%/10% lows, GPU usage, VRAM), refreshed on a timer. |
| `InputOverlay.cs` | Live input-state readout (held keys, mouse delta, camera pose) for debugging. |
| `GpuUpload.cs` | Shared helper: uploads CPU pixel data into a GPU texture via a staging buffer + one-off command buffer, used by every GDI+-backed overlay (including the new `LoginScreen`). |
| `GpuUsageMonitor.cs` | Reads the Windows "GPU Engine" performance-counter category for this process's GPU utilization (the same source Task Manager's per-process GPU column uses). |
| `FrameStats.cs` | Rolling frame-time statistics (current/average FPS, 1%/10% lows) over a ~2-second window. |
| **`PasswordHasher.cs`** *(new)* | Salted PBKDF2-HMAC-SHA256 password hashing (600,000 iterations, 128-bit random salt, constant-time verification). No dependency beyond `System.Security.Cryptography`. See `02-Research.md` §2.7 for the algorithm choice. |
| **`AccountStore.cs`** *(new)* | Local, file-backed account store (`%LOCALAPPDATA%\KSE\accounts.json`, via a source-generated `JsonSerializerContext` for trimming-friendly JSON). `TryCreateAccount`/`TryLogin` validate input, enforce a minimum username/password length, and never store or return plaintext passwords. Login failures use one generic error message regardless of whether the username exists, to avoid username enumeration. |
| **`LoginScreen.cs`** *(new)* | The login/sign-up UI itself — same GDI+ texture-blit technique as `UiOverlay`/`StatsOverlay`, but centered on screen and interactive rather than corner-anchored and read-only. Owns its own pipeline/resource-set-layout/sampler (mirroring the other overlays' self-contained construction). Handles `CharInput` for text entry and `KeyDown` for Tab (switch field)/Backspace/Enter (submit)/F2 (toggle Login ↔ Create Account); masks the password field with `•`; shows inline validation/error text; exposes `IsAuthenticated`/`LoggedInUsername` once a login or sign-up succeeds. |

### `Documents/` — this document set

| File | Purpose |
|---|---|
| `01-Statement-of-Intent.md` | Aim, goals, scope, and an explicit note on what earlier, broader planning was *not* carried forward. |
| `02-Research.md` | Comparable engines, graphics API choice, languages, the GLFW-to-Win32 windowing decision, third-party libraries, and the password-hashing research behind this update's feature. |
| `03-Implementation.md` | This document. |
| `04-Development-Log.md` | A weekly log built from real commit history (see next section), organized into development phases rather than a flat week-by-week list. |
| `05-Timesheet.md` / `Timesheet.csv` | Logged/estimated hours, 19/03/2026 through 29/07/2026. |
