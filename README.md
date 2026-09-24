# Torrid

A Windows desktop app that monitors CPU and GPU (core/hotspot/VRAM) temperatures in real time, shows them as color-coded numbers in a system tray icon and a main window, and fires a native Windows notification when any sensor crosses into the critical zone.

Built on top of [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) for sensor access.

## Status

Planning stage — see [PROJECT_PLAN.md](PROJECT_PLAN.md) for the full design, architecture, and phased build plan.

## Why

Most GPU monitoring tools show core temperature only. For thermal validation and long-running workloads (e.g. local LLM inference), Hot Spot and Memory Junction temperatures are the metrics that actually catch problems early — Torrid surfaces those front and center with live color-coded alerts instead of requiring you to keep a separate monitoring tool open and watch it manually.

## Tech stack

C# / .NET, WPF, LibreHardwareMonitorLib.

## License

TBD.
