# Phase 0 sensor probe findings

Ran on the target machine (AMD Ryzen 9 5950X + NVIDIA GPU(s)), both non-elevated and elevated (Administrator). Source: `Program.cs` in this directory.

## Confirmed sensor names to build against

| Metric | Hardware | Sensor.Name | SensorType | Needs elevation? |
|---|---|---|---|---|
| CPU package temp | `[Cpu] AMD Ryzen 9 5950X` | `Core (Tctl/Tdie)` | Temperature | **Yes** — reads `0.0` non-elevated, real value (~59–63°C idle) elevated |
| GPU core temp | `[GpuNvidia] NVIDIA GeForce RTX 3090` | `GPU Core` | Temperature | No — reads correctly either way |
| GPU hot spot temp | same | `GPU Hot Spot` | Temperature | No — reads correctly either way |
| GPU memory junction (VRAM) temp | same | `GPU Memory Junction` | Temperature | No — reads correctly either way |

Additional CPU temp sensors available if useful later: `CCD1 (Tdie)`, `CCD2 (Tdie)`, `CCDs Max (Tdie)`, `CCDs Average (Tdie)`, `SoC` (all elevated-only, all `0.0` non-elevated).

## Elevation requirement — confirmed concretely

Non-elevated: `Core (Tctl/Tdie)` = `0.0`, CPU per-core clocks mostly `null`/`0.0`, `Package` power = `0.0`.
Elevated: all of the above populate with real values.
GPU temperature sensors (Core/Hot Spot/Memory Junction) read correctly in **both** modes on this machine — contrary to the plan's assumption that NVAPI access needs elevation, it did not for temps. The app should still require elevation (for the confirmed CPU behavior), but don't assume GPU temps are blocked without it.

## GPU count — only one GPU currently detected, not two

`Computer.Hardware` enumerates exactly **one** `[GpuNvidia]` entry (`NVIDIA GeForce RTX 3090`, `Id=/gpu-nvidia/0`), consistently across 5 ticks, both elevated and non-elevated. This was cross-checked independently of LibreHardwareMonitor:
- `nvidia-smi -L` → lists only `GPU 0: NVIDIA GeForce RTX 3090`
- `Get-CimInstance Win32_VideoController` → lists only one NVIDIA video controller

So this isn't a LibreHardwareMonitor enumeration bug — Windows itself currently sees only one RTX 3090 on this machine, despite the plan describing two (MSI GAMING X TRIO + EVGA FTW3 ULTRA GAMING). **Flagged back to the user — see conversation.**

The architecture in the plan (loop over all `IHardware` where `HardwareType == Gpu`, index/order-based, no MSI/EVGA string match available from the sensor API) already works generically for 1..N GPUs, so this doesn't block building the Sensor Service — it just means the "both GPUs" path can't be exercised end-to-end on real hardware until the second card is present again.

## GPU naming

`hardware.Name` for the GPU is just `"NVIDIA GeForce RTX 3090"` — identical for both cards, no MSI/EVGA branding available from the sensor API, confirming plan section 10's open question. Distinguishing "MSI" vs "EVGA" will need to be a user-assigned label in Settings, keyed by enumeration index/order (`hardware.Identifier`, e.g. `/gpu-nvidia/0` vs `/gpu-nvidia/1`), not derived automatically.
