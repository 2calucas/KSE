# 8 — Testing

**Author:** Cal Lucas
**Project:** KingStudio Engine (KSE)
**Document version:** 1.0
**Last updated:** 29/07/2026

---

## 8.1 Purpose and Method

This document records a testing pass on the finished `run_me.exe` build: functional test cases covering
the login/sign-up gate and the render loop, and performance results captured while running the scene.

**A note on how the performance analysis was produced, in the same spirit as the "(est.)" labelling in
`05-Timesheet.md`:** the frame-time figures in §8.4 below are the real numbers observed while testing (not
invented), but the *explanation* of why they came out that way was not captured with a GPU profiler — it is
reasoned from what this document set already knows about the implementation (`03-Implementation.md` §3.2,
`06-Report.md` §6.4.3, and the source itself, checked directly for this document). Treat §8.4's root-cause
paragraphs as an informed hypothesis, not a profiler trace, and revise them if a future pass with
RenderDoc/Nsight/PIX says otherwise.

## 8.2 Test Environment

| Component | Spec |
|---|---|
| CPU | AMD Ryzen 7 7700X |
| GPU | AMD Radeon RX 7900 XT, 20GB, factory-OC variant |
| RAM | 64GB DDR5 |
| Storage | 2.5TB SSD |
| Window resolution | 1024×768 (the Sandbox's fixed `requestedWidth`/`requestedHeight` in `Program.cs`) |
| Build under test | `run_me.exe` — Release, self-contained, single-file, `win-x64` (see `README.md` → *Running it*) |
| Present mode | `PresentMode.Fifo` (the RHI's default — vsync'd, no tearing; see `src/Engine.RHI/SwapChain.cs`) |
| Vulkan validation | Enabled (`new VulkanGraphicsDevice(enableValidation: true)` in `Program.cs`) |

This hardware is far ahead of what the Sandbox needs at 1024×768 — relevant context for §8.4, since it
means the bottleneck most of the time is not raw GPU throughput.

## 8.3 Functional Test Cases

| ID | Description | Steps | Expected result | Actual result | Pass/Fail |
|---|---|---|---|---|---|
| TC01 | Create a new account | Launch app → Tab to focus fields → type a username (≥3 chars) and password (≥8 chars) → Enter | Account created, scene loads immediately (creating an account authenticates you, per `LoginScreen.Submit()`) | As expected | Pass |
| TC02 | Username too short | Try to create an account with a 1–2 character username | Inline error: *"Username must be at least 3 characters."*, no account created | As expected | Pass |
| TC03 | Password too short | Try to create an account with a <8 character password | Inline error: *"Password must be at least 8 characters."* | As expected | Pass |
| TC04 | Duplicate username | Create an account, then try to create a second account with the same username (any case) | Inline error: *"That username is already taken."* | As expected | Pass |
| TC05 | Log back in with correct credentials | Close and reopen the app → F2 to "Log In" mode if needed → enter the same username/password → Enter | Scene loads | As expected | Pass |
| TC06 | Log in with a wrong password | Enter a valid username with an incorrect password | Inline error: *"Invalid username or password."* (same generic message as an unknown username — no enumeration) | As expected | Pass |
| TC07 | Password field masking | Type into the password field | Characters shown as `•`, not plaintext | As expected | Pass |
| TC08 | Toggle Login ↔ Create Account | Press F2 | Panel title and hint text switch between "Log In" and "Create Account" | As expected | Pass |
| TC09 | Window resize | Drag-resize the window while in the scene | Swap chain and scene render targets resize without a crash; a brief hitch is expected (see §8.4) | As expected, plus the expected hitch | Pass |
| TC10 | Cycle all four quality tiers | Press Up/Down repeatedly through Low → Medium → High → Ray Tracing → back | Shadow resolution and render scale visibly change; Ray Tracing only appears/works if the GPU reports `VK_KHR_ray_query` + `VK_KHR_acceleration_structure` support (true on the RX 7900 XT) | As expected | Pass |
| TC11 | Ray-traced reflections | Select the Ray Tracing tier and look at the mirror sphere / left room wall | Sharp reflections of the rest of the scene, not just a flat shaded surface | As expected | Pass |
| TC12 | Escape to log out | Press Esc while in the scene | Scene GPU resources are torn down, app returns to a fresh login screen, process keeps running | As expected | Pass |
| TC13 | Re-authenticate after logout | After TC12, log back in | Scene rebuilds and loads normally a second time in the same process | As expected | Pass |
| TC14 | Close window from login screen | Close the window before authenticating | App exits immediately, no scene resources are ever built | As expected | Pass |
| TC15 | Close window from scene | Close the window while in the scene | App exits cleanly | As expected | Pass |

## 8.4 Performance Results

Across all four quality tiers, frame rate sat at roughly **165 FPS on average**, with **1% lows in the
50–70 FPS range**.

### 8.4.1 Why the average lands right around 165, not higher

Present mode is `Fifo` (§8.2) — a vsync mode that caps the frame rate at the display's refresh rate rather
than letting it run uncapped. An RX 7900 XT rendering a scene this simple at only 1024×768 finishes each
frame with room to spare regardless of tier, so the observed ~165 FPS almost certainly reflects a **165Hz
display's refresh rate**, not the engine's actual rendering limit. This is consistent with quality tier
having little visible effect on the *average* number: at this resolution, even the Ray Tracing tier's extra
work (inline ray-query shadows, reflections, bounded multi-bounce GI) is cheap enough for this GPU to still
be waiting on vsync most frames.

### 8.4.2 Why the 1% lows drop to 50–70 FPS

The most likely explanation is **not** GPU throughput — it's how the engine paces frames on the CPU side.
Three things in the implementation compound:

1. **`VulkanSwapChain.Present()` calls a full `_device.WaitIdle()` (`vkDeviceWaitIdle`) before every single
   present** (`src/Engine.RHI.Vulkan/VulkanSwapChain.cs`). That fully serializes CPU and GPU work — there is
   no overlap between frames despite `BufferCount = 2` in the swap-chain descriptor. Any transient hiccup
   (OS scheduler, driver bookkeeping) has nowhere to hide and shows up directly as a slow frame.
2. **Vulkan validation is enabled** (`enableValidation: true`, §8.2). Validation layers intercept every
   Vulkan call to check it, which adds CPU-side overhead — most noticeable on exactly the kind of
   fully-serialized, per-frame-barrier-heavy loop this is (each frame transitions the shadow map, scene
   color target, and back buffer, and — on the Ray Tracing tier — rebuilds the TLAS too).
3. **Quality-tier changes and window resizes are synchronous, multi-step GPU operations, each with their
   own extra `WaitIdle()`.** `CreateShadowMap`, `CreateDepthTarget`, and `CreateSceneColorTarget` in
   `Program.cs` each submit a one-off command buffer and call `queue.WaitIdle()` before returning. TC09 and
   TC10 in §8.3 exercise exactly this path — resizing the window and cycling through all four quality
   tiers — so if those actions happened to fall inside the ~2-second rolling window `FrameStats` uses to
   compute 1%/10% lows, a single tier switch or resize is enough on its own to produce one or more very slow
   "frames" (each one is really a full resource-teardown-and-rebuild, not a normal render).

On the Ray Tracing tier specifically, the **full TLAS rebuild every frame** (`06-Report.md` §6.4.3 — a
rebuild, not an incremental refit, across all 12 tracked instances) adds a further per-frame CPU cost on
top of the above, which is the most plausible reason the low end of the 50–70 FPS range would be seen more
often on that tier than on Low/Medium/High.

**In short:** the hardware is not the bottleneck here at all — the 1% lows look like a direct, explainable
consequence of a frame-pacing model (`WaitIdle()` every present) and synchronous resource-recreation paths
that were both already flagged as documented simplifications rather than production-grade frame pacing
(`03-Implementation.md` §3.2), not a sign of a GPU or driver problem.

## 8.5 Client Involvement

Michelle Chapman (see [`07-Client.md`](07-Client.md)) ran through part of the brief in §8.3 as part of a
separate session on 29/07/2026, from a first-time-user perspective rather than a developer's. She completed
account creation and login (TC01/TC05) successfully, but needed continuous guidance from the developer and
repeated reference to the README's control table to manage the in-scene movement controls — a usability
finding distinct from the pass/fail functional results above, given her limited recent hands-on experience
with a keyboard-and-mouse PC. Full detail is recorded in `07-Client.md` §7.5, since that account is about
her experience as a user rather than the feature-by-feature correctness this section covers.

## 8.7 User Acceptance Testing (UAT) Script and Client Sign-Off

The functional test cases in §8.3 are the developer's own tests. This section is the separate artefact the
assessment brief asks for: a script written *for the client to execute herself*, plus a template for the
email she sends back with her results. Michelle's informal session (§8.5, `07-Client.md` §7.5) already
surfaced the biggest real finding — that the controls need guidance for an infrequent PC user — so the
script below keeps that context rather than pretending the informal session didn't happen: it asks her to
mark each step as she did it, guidance included, not to redo the whole session unaided.

### 8.7.1 UAT Script

| Step | Instruction | Expected result | Client result (to be completed by Michelle) | Pass/Fail |
|---|---|---|---|---|
| UAT1 | Launch `run_me.exe`. Create an account using a username and password you'll remember. | The 3D scene appears after submitting. | | |
| UAT2 | Close the app, reopen it, and log back in with the same details. | You're back in the same scene. | | |
| UAT3 | Try logging in with the wrong password on purpose. | A message tells you the login failed, without saying which part was wrong. | | |
| UAT4 | Move around the scene using the controls in `README.md` → *How to use it*, with help if you need it. | You can move the camera and look around. | | |
| UAT5 | Press the Up/Down arrow keys a few times. | The picture in the corner of the screen changes between quality levels, and the scene may look sharper or blurrier. | | |
| UAT6 | Try to find the indoor room (there's an on-screen hint about which direction to fly). | You find a room with different lighting from the outdoor scene. | | |
| UAT7 | Press Esc. | You're returned to the login screen, and the app is still running (not closed). | | |
| UAT8 | Overall — was anything confusing, broken, or worth mentioning? | (open-ended) | | N/A |

### 8.7.2 Client UAT Results Email (Template)

As with §7.6, this is a **template** for Michelle to send once she has completed §8.7.1 with her own
results filled in — it is left as a template rather than a fabricated reply, so that the actual submitted
evidence is a real email from her:

> **Subject:** UAT results — KSE testing
>
> Hi Cal,
>
> Here are my results from going through the test script:
>
> [Michelle pastes/describes her completed copy of the table in §8.7.1 here, including anything under UAT8.]
>
> Michelle

Once received, log the date in `09-Project-Plan.md` §9.2.1 next to "Client written sign-off + UAT email"
and update that row's % complete from 0%.

## 8.8 Conclusion

Every functional test case in §8.3 passed. Performance is capped by vsync at a healthy frame rate for this
hardware, and the occasional dip to 50–70 FPS has a specific, source-grounded explanation (§8.4.2) rather
than an unexplained one — which is itself a useful outcome of this testing pass: it points at three concrete,
already-known implementation details (`Present()`'s forced `WaitIdle()`, validation layers left enabled, and
synchronous resource recreation on tier/resize changes) as the next things worth addressing if frame pacing
ever needs to improve.
