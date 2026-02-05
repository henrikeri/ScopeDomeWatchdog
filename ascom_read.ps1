# List-AscomDevices.ps1
# Prints registered ASCOM drivers (ProgID + friendly name) for common device types.

param(
  [string[]]$DeviceTypes = @(
    "Dome","Telescope","Focuser","Camera","FilterWheel","Rotator",
    "Switch","SafetyMonitor","ObservingConditions","CoverCalibrator","Video"
  )
)

$ErrorActionPreference = "Stop"

function Release-ComObject([object]$obj) {
  if (-not $obj) { return }
  try { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($obj) } catch {}
}

try {
  $profile = New-Object -ComObject "ASCOM.Utilities.Profile"
} catch {
  Write-Error "Could not create ASCOM.Utilities.Profile COM object. Is the ASCOM Platform installed (and same 32/64-bit as this PowerShell)?" 
  throw
}

try {
  foreach ($t in $DeviceTypes) {
    Write-Host ""
    Write-Host "=== $t ==="

    $drivers = $null
    try {
      $drivers = $profile.RegisteredDevices($t)
    } catch {
      Write-Host "  (Error reading registered devices: $($_.Exception.Message))"
      continue
    }

    if (-not $drivers -or ($drivers.PSObject.Properties.Name -contains "Count" -and $drivers.Count -eq 0)) {
      Write-Host "  (none)"
      continue
    }

    foreach ($kv in $drivers) {
      # Most ASCOM installs return Key/Value pairs: Key=ProgID, Value=FriendlyName/Description
      $progId = $null
      $name   = $null

      try { $progId = $kv.Key }   catch { $progId = [string]$kv }
      try { $name   = $kv.Value } catch { $name   = "" }

      if ($name) {
        Write-Host ("  {0}  -  {1}" -f $progId, $name)
      } else {
        Write-Host ("  {0}" -f $progId)
      }
    }
  }
}
finally {
  Release-ComObject $profile
}
