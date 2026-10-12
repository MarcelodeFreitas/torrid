<#
.SYNOPSIS
  Proves that Windows accepts Torrid's toasts, without anyone looking at the screen.

.DESCRIPTION
  Windows writes HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\<AUMID>\LastNotificationAddedTime
  for every toast it actually accepts (see tools/ToastProbe/FINDINGS.md). This script records that value, runs the
  Debug build with --test-toast (the app ensures its Start Menu shortcut, waits 15 s, sends one test toast, waits 4 s
  and exits), then reads the value again.

  A brand-new shortcut can take up to about a minute to be picked up by Windows, so the very first run on a machine
  can FAIL; run it again.

.EXAMPLE
  powershell -NoProfile -File tools\Verify-Toast.ps1
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot "..\Torrid.App\bin\Debug\net8.0-windows10.0.17763.0\Torrid.App.exe"),
    [string]$Aumid = "Torrid.App.Dev",
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = "Stop"
$keyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\$Aumid"

function Read-LastAdded {
    $item = Get-ItemProperty -Path $keyPath -Name LastNotificationAddedTime -ErrorAction SilentlyContinue
    if ($item) { return [string]$item.LastNotificationAddedTime }
    return $null
}

if (-not (Test-Path $Exe)) { Write-Host "FAIL: $Exe not found (build Torrid.sln in Debug first)."; exit 2 }
$exePath = (Resolve-Path $Exe).Path

$config = Join-Path $env:TEMP ("torrid-verify-toast-{0}.json" -f [guid]::NewGuid().ToString("N"))
$before = Read-LastAdded
Write-Host "LastNotificationAddedTime before: $(if ($before) { $before } else { '(none)' })"

$proc = Start-Process -FilePath $exePath -ArgumentList @("--test-toast", "--config", "`"$config`"") -PassThru
Write-Host "Started Torrid.App (pid $($proc.Id)) with --test-toast; waiting up to $TimeoutSeconds s for it to exit..."
if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    Remove-Item $config -ErrorAction SilentlyContinue
    Write-Host "FAIL: Torrid.App did not exit within $TimeoutSeconds s (killed)."
    exit 1
}
Remove-Item $config -ErrorAction SilentlyContinue

$after = Read-LastAdded
Write-Host "LastNotificationAddedTime after:  $(if ($after) { $after } else { '(none)' })"

if ($after -and $after -ne $before) {
    Write-Host "PASS: Windows accepted the notification"
    exit 0
}

Write-Host "FAIL: Windows did not record a notification for $Aumid (a just-created Start Menu shortcut can need up to a minute; run again)."
exit 1
