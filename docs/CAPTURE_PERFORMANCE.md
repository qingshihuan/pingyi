# Screenshot startup investigation — 2026-11-05

This is development-branch evidence, not a release announcement or a guarantee for other desktops.

## Measured bottleneck

Baseline: main `325a988`, including `246de33` and `745e9d1`, with metadata timers and a render-batch completion probe added before changing the pixel path. Windows, Release/net10.0, Avalonia 12.1.1; an unlocked native desktop with DWM enabled. Three monitors: 1920×1080 at 100%, 3840×2160 at 150%, 1920×1080 at 100%; virtual desktop 7680×2160. No screenshot, screenshot hash, recognized text, user path or credential was saved.

The single-monitor optimization does not help this desktop. Before showing any overlay the old path compressed the full desktop, then decoded that entire PNG and encoded a cropped PNG **for each monitor on the UI thread**. The overlays decoded those PNGs again. The first baseline sample took 1,774ms to finish rendering all monitors (Opened occurred earlier); repeated samples were still slow.

The native probe calls the production window isolation, capture router, screen capture, cropper and overlay session. It uses a synthetic shell window and cancels selection, without initializing models, configuration stores or networking. It alternates UI entry and worker-to-UI dispatch. **It does not press a physical key, measure OS hotkey delivery, or drive the real main-window button.** Baseline dispatch entries used a same-thread dispatcher hop; the final probe uses a worker-to-UI post. This changes only the trigger proxy, not the expensive pixel/render path. Production instrumentation separately starts in the actual hotkey callback and button handler, and includes coordinator/gate delay.

## Before / after measurements

Baseline: 12 samples. Final: 30 samples, including the first capture after synthetic resource warmup. All samples are retained; P50 is the median, P95 is nearest rank. Times are milliseconds. The desktop was live, not a frozen fixture: scene complexity and background load vary. Intermediate measurements (before the final hide barrier change) ranged from P50 189–219ms; the final dataset below is not a universal performance claim.

| Measurement | Before | After |
| --- | ---: | ---: |
| All monitors' first rendered frame, P50 | 1650.11 | 193.06 |
| All monitors' first rendered frame, P95 | 1800.95 | 232.89 |
| First sample, all monitors rendered | 1773.89 | 321.82 |
| Repeated captures, rendered P50 | 1628.42 | 192.85 |
| All monitors input-ready, P50 | 1698.06 | 242.28 |
| All monitors input-ready, P95 | 1877.77 | 280.32 |
| UI-entry rendered P50 | 1650.11 | 192.59 |
| Dispatcher-entry rendered P50 | 1616.14 | 193.28 |

Stage durations below are P50, summing per-monitor work within each capture before taking the median. Nested stages overlap: do not add every row to estimate total latency.

| Stage | Before | After |
| --- | ---: | ---: |
| Hide application windows | 5.19 | 5.84 |
| Background dispatcher hide drain | 18.98 | Removed on Windows |
| Fixed hide settle delay | 21.57 | Only a 50ms fallback if DWM is unavailable |
| DwmFlush | 6.36 | 5.46 |
| GDI allocation | 0.16 | 0.07 |
| BitBlt | 93.07 | 92.67 |
| GetDIBits / DIB managed copy | 7.64 | 3.82 |
| Whole-desktop PNG encode | 481.45 | Removed |
| Per-monitor decode/crop/PNG encode | 769.52 | <0.01 (shared pixel views) |
| Overlay XAML initialization | 3.86 | 4.22 |
| Overlay bitmap decode / raw pixel copy | 69.46 | 8.07 |
| Entire window construction (includes XAML/bitmap) | 85.93 | 26.00 |
| Show/activation requests | 73.74 | 24.28 |

PNG compression plus repeated per-monitor codecs account for over 1.2 seconds of the baseline median. BitBlt remains around 93ms on this desktop; it was not the principal one-second regression. The optimized path creates **zero PNG bytes before selection**, then encodes only the selected rectangle on a worker. The compact numeric evidence is in [capture measurement summaries](performance/capture-2026-11-05.json).

The 200/350ms goals refer to first display, not input readiness. This run's render-completion proxy meets those percentiles; the input-ready P50 does **not** meet 200ms. A render-thread completion is stronger evidence than Opened, but is not a photodiode/high-speed-camera measurement of physical scanout. Do not claim that every real hotkey, first launch, driver or screen arrangement meets the goals.

## Pipeline and ownership

1. The hotkey callback creates a per-operation timer before `Dispatcher.Post`. The button handler starts its own timer before disabling the button. Coordinator entry and operation-gate wait are separate measurements; rapid triggers cancel the previous operation.
2. The scope closes app popups, temporarily disables window transitions/excludes supported HWNDs, hides visible app windows and verifies native `IsWindowVisible == false`. Avalonia's Win32 Hide calls `ShowWindow(SW_HIDE)` synchronously. `DwmFlush` fences the app's pending presentation; it does not flush every other process. A failed/unavailable DWM call retains a 50ms fallback. Linux keeps its dispatcher drain and 200ms wait.
3. Windows captures a top-down BGRA32 DIB, calls `GdiFlush` before reading, copies once into managed ownership, and releases every GDI handle in `finally`. No native pixel pointer escapes. The old encoded capture interface remains available to existing callers; X11 and portal routing are unchanged.
4. `DesktopFrame` validates size/stride/buffer bounds. Per-display views share an immutable managed buffer, including padded stride and negative desktop coordinates. Avalonia's opaque BGRA bitmap constructor copies into its own storage while the source view is pinned; pinning ends before rendering. GDI's undefined alpha byte is ignored. No caller may mutate the transferred pixel buffer.
5. Each fresh session constructs new overlay windows, sizes them for each monitor's current scaling, and activates only the pointer's monitor for Esc. It does not retain old desktop images or native windows between captures. Measured construction cost was small enough that native-window pooling was not justified against stale-frame/lifecycle risk. Startup idle work instead warms XAML/fonts/Skia by rendering a tiny **synthetic** image off-screen without showing a window; it is skipped during capture/shutdown and never performs screen capture.
6. A custom screenshot control records the image drawing commands. The batch containing those commands is observed via `CompositionBatch.Rendered`. `overlay_first_frame_rendered` is emitted only after render-thread completion; canceled/closed windows cannot report a successful stale frame. `all_overlays_input_ready` also waits for all windows' UI-thread input-priority continuations. Opened is kept as a separate earlier milestone.
7. Esc, cancellation, failure and selection close every overlay, detach sources and dispose bitmaps on the UI cleanup queue. Fresh captures use fresh buffers. The coordinator gate serializes restoration; shutdown cancels and drains pending operations without resurrecting windows. Only a completed selected region becomes an `ImageFrame` for existing OCR/model/QR paths.

## Diagnostics and reproduction

Every operation sample contains a numeric operation ID, fixed stage name, `duration_ms`, `total_ms`, display count, virtual desktop dimensions and `png_bytes`. Metadata not yet discovered is zero. On raw Windows captures `png_bytes` is zero until the selected region is encoded. Milestone durations cover the interval since the previous milestone; explicit measured scopes cover only their named operation. No log accepts image contents, file names, credentials or arbitrary exception messages.

To collect the real app's hotkey/button trace, start it with `PINGYI_CAPTURE_TIMING=1` and redirect standard error locally. Timings also go to .NET Trace listeners. This setting changes no saved user configuration. Check `hotkey_received`/`capture_button_received` → `coordinator_entered` and `operation_gate_wait` if the full application differs from the standalone probe.

From the repository, on an unlocked desktop:

```powershell
dotnet build PingYi.slnx -c Release
dotnet tests/PingYi.Capture.Benchmarks/bin/Release/net10.0/PingYi.Capture.Benchmarks.dll --desktop --prewarm --verify 30 > artifacts/capture.jsonl
python scripts/summarize-capture-timing.py artifacts/capture.jsonl
```

The opt-in probe briefly opens overlays and synthetic color windows. Its stdout contains only timing, numeric display metadata and fixed verification codes. `--verify` checks changing synthetic background colors underneath hidden cover windows, restoration, all native overlay pixel bounds at current DPI, selected-region encoding and bitmap cleanup. It never saves desktop images or accesses local model/API settings. Without `--desktop` it does nothing. Hosted Windows CI runs a small native probe and uploads only JSON metadata; CI timing is not workstation performance evidence.

## Verification and remaining limits

- Native Windows workstation: latest-frame checks, own-window exclusion/restoration, three monitors, mixed 100%/150% DPI, selected PNG and bitmap cleanup passed. No monitor setting was changed. Physical hotkey latency and a human drag/physical Esc were not measured by the probe.
- Automated tests cover opaque BGRA/color/stride/slices, pixel-buffer ownership, encoded fallback, cross-monitor geometry (including requested 200% scale), Esc, rapid cancellation/reopen with changed pixels, early close, bitmap release, raw routing, operation-gate supersession and shutdown. Requested headless scaling is not equivalent to a physical 200% monitor.
- Local Windows Release build passed with zero warnings/errors; 399 Core tests, 145 App tests and 38 Python tests passed. The secret audit covers the staged files and resulting commit. Windows/Ubuntu CI also builds/tests; native X11 conflict/portal/smoke checks remain in the workflow. Headless/CI results do not prove physical desktop performance or Linux mixed-DPI behavior.
- Still to validate across more machines: real hotkey/button-to-scanout distributions under model load, first capture racing startup warmup, physical Linux multi-monitor/mixed-DPI setups, HDR, display hot-plug during selection and the DWM-unavailable fallback. GPU readback and native window presentation remain the largest residual costs. No official Release is produced by this work.

Implementation references: [Avalonia 12.1.1 Win32 Hide](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Windows/Avalonia.Win32/WindowImpl.cs), [raw bitmap copy/lifetime](https://github.com/AvaloniaUI/Avalonia/blob/12.1.1/src/Skia/Avalonia.Skia/ImmutableBitmap.cs), [render-batch completion](https://docs.avaloniaui.net/api/avalonia/rendering/composition/transport/compositionbatch), [DIB synchronization](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-createdibsection), [DwmFlush scope](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmflush).
