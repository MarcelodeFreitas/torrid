<#
.SYNOPSIS
  Screenshots the Torrid main window to a PNG so UI work can be checked visually without clicking anything.

.EXAMPLE
  powershell -File tools\Capture-Window.ps1 -Launch -Out shot.png
  Launches the Debug build with --show, captures its window, then closes it.

.EXAMPLE
  powershell -File tools\Capture-Window.ps1 -Out shot.png
  Captures an already-running Torrid.App window (it must be visible: start it with --show).
#>
param(
    [string]$Out = "torrid-window.png",
    [switch]$Launch,
    [string]$Exe = "Torrid.App\bin\Debug\net8.0-windows10.0.17763.0\Torrid.App.exe",
    [string[]]$AppArgs = @("--show"),
    [int]$WaitSeconds = 4
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);
}
"@
[NativeCapture]::SetProcessDPIAware() | Out-Null

$started = $null
if ($Launch) {
    $exePath = Resolve-Path $Exe
    $started = Start-Process -FilePath $exePath -ArgumentList $AppArgs -PassThru
}

try {
    $deadline = (Get-Date).AddSeconds($WaitSeconds + 10)
    $hwnd = [IntPtr]::Zero
    Start-Sleep -Seconds $WaitSeconds
    while ((Get-Date) -lt $deadline -and $hwnd -eq [IntPtr]::Zero) {
        $proc = Get-Process -Name "Torrid.App" -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($proc) { $hwnd = $proc.MainWindowHandle } else { Start-Sleep -Milliseconds 300 }
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw "No visible Torrid.App window found (start it with --show, or use -Launch)." }

    $rect = New-Object NativeCapture+RECT
    [NativeCapture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    $ok = [NativeCapture]::PrintWindow($hwnd, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT (captures WPF content even if occluded)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    if (-not $ok) { throw "PrintWindow failed." }

    # Crop away the invisible resize border / shadow padding so only the visible window remains.
    $frame = New-Object NativeCapture+RECT
    $crop = $bmp
    if ([NativeCapture]::DwmGetWindowAttribute($hwnd, 9, [ref]$frame, 16) -eq 0) {   # 9 = DWMWA_EXTENDED_FRAME_BOUNDS
        $cx = [Math]::Max(0, $frame.Left - $rect.Left); $cy = [Math]::Max(0, $frame.Top - $rect.Top)
        $cw = [Math]::Min($w - $cx, $frame.Right - $frame.Left); $ch = [Math]::Min($h - $cy, $frame.Bottom - $frame.Top)
        if ($cw -gt 0 -and $ch -gt 0) { $crop = $bmp.Clone((New-Object System.Drawing.Rectangle $cx, $cy, $cw, $ch), $bmp.PixelFormat); $w = $cw; $h = $ch }
    }
    $full = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Out)) }
    $crop.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($crop -ne $bmp) { $crop.Dispose() }
    $bmp.Dispose()
    Write-Host "Saved ${w}x${h} screenshot to $full"
}
finally {
    if ($started -and -not $started.HasExited) { Stop-Process -Id $started.Id -Force -ErrorAction SilentlyContinue }
}
