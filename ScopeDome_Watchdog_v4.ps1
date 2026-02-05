# ScopeDome_Watchdog.ps1
# Live telemetry to terminal; log file only when restarting.

# ---------------- CONFIG ----------------
$MONITOR_IP          = "192.168.6.100"
$PING_INTERVAL_SEC   = 1
$FAILS_TO_TRIGGER    = 5

$PLUG_IP             = "192.168.61.45"
$SWITCH_ID           = 0
$OFF_SECONDS         = 15

$COOLDOWN_SECONDS     = 120
$POST_CYCLE_GRACE_SEC = 30

# Dome process control
$DOME_PROC = "ASCOM.ScopeDomeUSBDome"
$DOME_EXE  = "C:\ScopeDome\Driver_LS\ASCOM.ScopeDomeUSBDome.exe"

# ASCOM dome driver (COM ProgID) used for FindHome after restart
$ASCOM_DOME_PROGID           = "ASCOM.ScopeDomeUSBDome.DomeLS"
$ASCOM_CONNECT_TIMEOUT_SEC   = 180   # how long to keep retrying the COM connect
$ASCOM_CONNECT_RETRY_SEC     = 5     # retry interval while connecting
$FINDHOME_TIMEOUT_SEC        = 900   # max time to wait for AtHome / Slewing=false after FindHome
$FINDHOME_POLL_MS            = 500   # poll interval while waiting

# ASCOM switch driver (COM ProgID) used to force peripherals ON after restart
$ASCOM_SWITCH_PROGID                 = "ASCOM.ScopeDomeUSBDome.DomeLS.Switch"
$FAN_SWITCH_INDEX                    = 18   # FanOnOff
$ASCOM_SWITCH_CONNECT_TIMEOUT_SEC    = 60
$ASCOM_SWITCH_CONNECT_RETRY_SEC      = 3
$FAN_ENSURE_TIMEOUT_SEC              = 30   # max time to retry setting/reading the switch


# Shared lock (prevents collisions with N.I.N.A script)
$LOCK_NAME = "ScopeDomePowerCycleLock"


# Manual trigger event (external scripts can request a restart)
$TRIGGER_EVENT_NAME = "ScopeDomeWatchdog.TriggerRestart"

# Rolling latency window
$LAT_WINDOW = 60
# ----------------------------------------

$ErrorActionPreference = "Stop"

$Desktop = [Environment]::GetFolderPath('Desktop')
$LogFile = Join-Path $Desktop ("ScopeDomeWatchdog_{0}.log" -f (Get-Date).ToString("yyyy-MM-dd"))

function Ts() { (Get-Date).ToString("yyyy-MM-dd HH:mm:ss") }

function LogRestart([string]$msg) {
  $line = "[{0}] {1}" -f (Ts), $msg
  Write-Host $line
  Add-Content -Path $LogFile -Value $line
}

function GetJson([string]$url) {
  $resp = Invoke-WebRequest -Uri $url -Method GET -TimeoutSec 5 -UseBasicParsing
  return $resp.Content | ConvertFrom-Json
}

function Rpc([string]$method, [hashtable]$query) {
  $qs = ($query.GetEnumerator() | ForEach-Object {
    "{0}={1}" -f [uri]::EscapeDataString([string]$_.Key),
               [uri]::EscapeDataString([string]$_.Value)
  }) -join "&"

  $url = "http://$PLUG_IP/rpc/$method" + ($(if ($qs) { "?$qs" } else { "" }))
  return GetJson $url
}

function Ping-Once([string]$ip) {
  $out = & ping.exe -n 1 -w 900 $ip 2>&1
  $ok = ($LASTEXITCODE -eq 0)
  $ms = $null
  if ($ok) {
    $m = [regex]::Match($out, '(time|tid)[=<]\s*(\d+)\s*ms', 'IgnoreCase')
    if ($m.Success) { $ms = [int]$m.Groups[2].Value }
  }
  return @{ ok = $ok; ms = $ms }
}

function Stop-DomeProcess-BestEffort() {
  try {
    $p = Get-Process -Name $DOME_PROC -ErrorAction SilentlyContinue
    if ($p) {
      LogRestart "Killing process: $DOME_PROC.exe"
      $p | Stop-Process -Force -ErrorAction SilentlyContinue
    } else {
      LogRestart "Process not running: $DOME_PROC.exe"
    }
  } catch {
    LogRestart ("Warning: failed to stop process: " + $_.Exception.Message)
  }
}

function Start-DomeProcess-BestEffort() {
  try {
    if (Test-Path -LiteralPath $DOME_EXE) {
      LogRestart "Launching: $DOME_EXE"
      Start-Process -FilePath $DOME_EXE | Out-Null
    } else {
      LogRestart "Warning: EXE not found: $DOME_EXE"
    }
  } catch {
    LogRestart ("Warning: failed to launch EXE: " + $_.Exception.Message)
  }
}

function Release-ComObject([object]$obj) {
  if (-not $obj) { return }
  try { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($obj) } catch {}
}

function Connect-AscomDomeCom([string]$progId, [int]$timeoutSec, [int]$retrySec) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $lastErr = $null

  while ($true) {
    $dome = $null
    try {
      $dome = New-Object -ComObject $progId
      $dome.Connected = $true

      # Some drivers only truly connect after a short delay; verify it.
      try {
        if (-not [bool]$dome.Connected) {
          throw "Connected remained false"
        }
      } catch {
        throw "Connected check failed: $($_.Exception.Message)"
      }

      return $dome
    }
    catch {
      $lastErr = $_.Exception.Message
      if ($dome) {
        try { $dome.Connected = $false } catch {}
        Release-ComObject $dome
      }

      LogRestart ("ASCOM connect attempt failed for '{0}': {1}" -f $progId, $lastErr)

      if ($sw.Elapsed.TotalSeconds -ge $timeoutSec) {
        throw "Timeout connecting to ASCOM dome '$progId' after ${timeoutSec}s. Last error: $lastErr"
      }
      Start-Sleep -Seconds $retrySec
    }
  }
}

function Invoke-DomeFindHomeAndWait([object]$dome, [int]$timeoutSec, [int]$pollMs) {
  # Validate capability where possible.
  try {
    if (-not [bool]$dome.CanFindHome) {
      throw "Driver reports CanFindHome=false"
    }
  } catch {
    # If CanFindHome isn't implemented (rare for IDomeV2), still attempt FindHome.
    LogRestart ("Warning: couldn't verify CanFindHome ({0}); attempting FindHome anyway." -f $_.Exception.Message)
  }

  $atHomeSupported = $true
  try {
    if ([bool]$dome.AtHome) {
      LogRestart "Dome already at home (AtHome=true); skipping FindHome."
      return
    }
  } catch {
    $atHomeSupported = $false
    LogRestart ("Warning: couldn't read AtHome ({0}); will fall back to waiting for Slewing=false." -f $_.Exception.Message)
  }

  LogRestart "Triggering ASCOM FindHome()..."
  $dome.FindHome()

  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $lastLog = 0

  while ($true) {
    Start-Sleep -Milliseconds $pollMs

    $slewing = $null
    $atHome = $null
    $az = $null

    try { $slewing = [bool]$dome.Slewing } catch { $slewing = $null }
    if ($atHomeSupported) {
      try { $atHome = [bool]$dome.AtHome } catch { $atHomeSupported = $false; $atHome = $null }
    }
    try { $az = [double]$dome.Azimuth } catch { $az = $null }

    if ($atHomeSupported -and $atHome -eq $true) {
      LogRestart ("Home found (AtHome=true). Elapsed {0:n1}s. Azimuth={1}" -f $sw.Elapsed.TotalSeconds, $az)
      return
    }

    if (-not $atHomeSupported -and $slewing -eq $false) {
      LogRestart ("FindHome finished (Slewing=false; AtHome unavailable). Elapsed {0:n1}s. Azimuth={1}" -f $sw.Elapsed.TotalSeconds, $az)
      return
    }

    # Light progress ping every ~10s while we're waiting, so the log isn't silent during long homing.
    if ($sw.Elapsed.TotalSeconds -ge ($lastLog + 10)) {
      $lastLog = [int]$sw.Elapsed.TotalSeconds
      LogRestart ("Waiting for home... elapsed {0:n0}s | Slewing={1} AtHome={2} Azimuth={3}" -f $sw.Elapsed.TotalSeconds, $slewing, $atHome, $az)
    }

    if ($sw.Elapsed.TotalSeconds -ge $timeoutSec) {
      throw "Timeout waiting for FindHome completion after ${timeoutSec}s. Slewing=$slewing AtHome=$atHome Azimuth=$az"
    }
  }
}

function Ensure-ScopeDomeFanOnOff([string]$switchProgId, [int]$index, [int]$timeoutSec) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $lastErr = $null

  while ($true) {
    $swObj = $null
    try {
      LogRestart "ASCOM: connecting to switch driver '$switchProgId'"
      # Reuse the generic COM-connect helper (despite the name)
      $swObj = Connect-AscomDomeCom $switchProgId $ASCOM_SWITCH_CONNECT_TIMEOUT_SEC $ASCOM_SWITCH_CONNECT_RETRY_SEC

      # Validate writability (best-effort; some drivers may throw)
      try {
        $canWrite = [bool]$swObj.CanWrite($index)
        if (-not $canWrite) { throw "CanWrite($index)=false" }
      } catch {
        LogRestart ("Warning: couldn't verify CanWrite({0}) ({1}); attempting to set anyway." -f $index, $_.Exception.Message)
      }

      $setOk = $false

      # Try boolean first (most common for FanOnOff)
      try {
        $swObj.SetSwitch($index, $true)
        $setOk = $true
      } catch {
        $lastErr = $_.Exception.Message
        LogRestart ("Warning: SetSwitch({0}, true) failed: {1}" -f $index, $lastErr)
      }

      # Also try numeric value for drivers that prefer value API (0/1)
      try {
        $swObj.SetSwitchValue($index, 1)
        $setOk = $true
      } catch {
        if (-not $setOk) {
          $lastErr = $_.Exception.Message
          LogRestart ("Warning: SetSwitchValue({0}, 1) failed: {1}" -f $index, $lastErr)
        }
      }

      if (-not $setOk) { throw "Failed to set fan switch $index (both SetSwitch and SetSwitchValue failed). Last error: $lastErr" }

      # Verify (best-effort) and log resulting state/value
      $state = $null
      $value = $null
      try { $state = [bool]$swObj.GetSwitch($index) } catch {}
      try { $value = [double]$swObj.GetSwitchValue($index) } catch {}

      LogRestart ("FanOnOff ensured: index={0} GetSwitch={1} GetSwitchValue={2}" -f $index, $state, $value)
      return
    }
    catch {
      $lastErr = $_.Exception.Message
      LogRestart ("ASCOM fan ensure attempt failed: {0}" -f $lastErr)

      if ($sw.Elapsed.TotalSeconds -ge $timeoutSec) {
        throw "Timeout ensuring FanOnOff (switch index $index) after ${timeoutSec}s. Last error: $lastErr"
      }
      Start-Sleep -Seconds 2
    }
    finally {
      if ($swObj) {
        try { $swObj.Connected = $false } catch {}
        Release-ComObject $swObj
      }
    }
  }
}

function PowerCycle-OrTurnOn-Plug_WithDomeSequence() {
  # Acquire shared lock; if busy, skip (no logging) and let caller handle cooldown.
  $mutex = $null
  $acquired = $false
  try {
    $mutex = New-Object System.Threading.Mutex($false, $LOCK_NAME)
    $acquired = $mutex.WaitOne(0)
    if (-not $acquired) { return $false }

    LogRestart "=== RESTART SEQUENCE BEGIN ==="

    Stop-DomeProcess-BestEffort
    LogRestart "Waiting 30 seconds..."
    Start-Sleep -Seconds 30
    $st = Rpc "Switch.GetStatus" @{ id = $SWITCH_ID }
    $isOn = [bool]$st.output
    LogRestart ("Plug state before action: output={0}" -f $isOn)

    if (-not $isOn) {
      LogRestart "Already OFF -> turning ON only (no cycle)"
      Rpc "Switch.Set" @{ id = $SWITCH_ID; on = "true" } | Out-Null
    } else {
      LogRestart "Cycling plug: OFF -> wait -> ON"
      Rpc "Switch.Set" @{ id = $SWITCH_ID; on = "false" } | Out-Null
      Start-Sleep -Seconds $OFF_SECONDS
      Rpc "Switch.Set" @{ id = $SWITCH_ID; on = "true" } | Out-Null
    }

    LogRestart "Waiting 30 seconds after power action..."
    Start-Sleep -Seconds 30

    Start-DomeProcess-BestEffort
    LogRestart "Waiting 30 seconds after launch..."
    Start-Sleep -Seconds 30

    # After power-cycling + relaunching, re-home the dome via ASCOM COM.
    $dome = $null
    try {
      LogRestart "ASCOM: connecting to dome driver '$ASCOM_DOME_PROGID'"
      $dome = Connect-AscomDomeCom $ASCOM_DOME_PROGID $ASCOM_CONNECT_TIMEOUT_SEC $ASCOM_CONNECT_RETRY_SEC
      LogRestart "ASCOM: connected; starting FindHome and waiting for completion..."
      Invoke-DomeFindHomeAndWait $dome $FINDHOME_TIMEOUT_SEC $FINDHOME_POLL_MS
      LogRestart "ASCOM: FindHome complete."
    } finally {
      if ($dome) {
        try { $dome.Connected = $false } catch {}
        Release-ComObject $dome
      }
    }


    # Ensure dome fan is ON (ASCOM Switch index 18: FanOnOff)
    try {
      Ensure-ScopeDomeFanOnOff $ASCOM_SWITCH_PROGID $FAN_SWITCH_INDEX $FAN_ENSURE_TIMEOUT_SEC
    } catch {
      LogRestart ("Warning: failed to ensure FanOnOff: " + $_.Exception.Message)
    }

    LogRestart "=== RESTART SEQUENCE END ==="
    return $true
  }
  finally {
    if ($mutex) {
      if ($acquired) { try { $mutex.ReleaseMutex() } catch {} }
      $mutex.Dispose()
    }
  }
}

# ---- Telemetry state ----
$total = 0
$okCount = 0
$failCount = 0
$consecutiveFails = 0
$lat = New-Object System.Collections.Generic.Queue[int]
$lastRestart = $null
$lastCycle = [datetime]::MinValue


# Manual trigger handle (named event)
$triggerCreatedNew = $false
$triggerEvent = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, $TRIGGER_EVENT_NAME, [ref]$triggerCreatedNew)

Write-Host ("[{0}] Watchdog running. Monitoring {1} every {2}s; trigger after {3} consecutive FAILs." -f (Ts), $MONITOR_IP, $PING_INTERVAL_SEC, $FAILS_TO_TRIGGER)
Write-Host ("Plug: {0} (switch_id={1}), off_seconds={2}, cooldown={3}s" -f $PLUG_IP, $SWITCH_ID, $OFF_SECONDS, $COOLDOWN_SECONDS)
Write-Host ("Manual trigger event: {0}" -f $TRIGGER_EVENT_NAME)
Write-Host "Telemetry updates every second. Log file is written ONLY when a restart happens."
Write-Host ""

while ($true) {
  $total++
  $p = Ping-Once $MONITOR_IP
  $now = Get-Date

  $manualRequested = $triggerEvent.WaitOne(0)

  if ($p.ok) {
    $okCount++
    if ($consecutiveFails -gt 0) { $consecutiveFails = 0 }
    if ($p.ms -ne $null) {
      $lat.Enqueue($p.ms)
      while ($lat.Count -gt $LAT_WINDOW) { [void]$lat.Dequeue() }
    }
  } else {
    $failCount++
    $consecutiveFails++
  }


  if ($manualRequested) {
    # Treat this exactly like hitting the failure threshold.
    if ($consecutiveFails -lt $FAILS_TO_TRIGGER) { $consecutiveFails = $FAILS_TO_TRIGGER }
  }

  $okPct = if ($total -gt 0) { [math]::Round(100.0 * $okCount / $total, 1) } else { 0.0 }
  $avgMs = if ($lat.Count -gt 0) { [math]::Round(($lat.ToArray() | Measure-Object -Average).Average, 1) } else { $null }

  $sinceRestartSec = if ($lastRestart) { [int](($now - $lastRestart).TotalSeconds) } else { -1 }
  $sinceCycle = ($now - $lastCycle).TotalSeconds
  $inCooldown = ($sinceCycle -lt $COOLDOWN_SECONDS)

  $status = if ($manualRequested) { "TRIG" } elseif ($p.ok) { "OK  " } else { "FAIL" }
  $msTxt = if ($p.ok -and $p.ms -ne $null) { "{0,4}ms" -f $p.ms } elseif ($p.ok) { "   ?ms" } else { "    --" }
  $avgTxt = if ($avgMs -ne $null) { "{0,5}ms" -f $avgMs } else { "   n/a" }
  $lrTxt  = if ($lastRestart) { $lastRestart.ToString("HH:mm:ss") } else { "never" }
  $coolTxt = if ($inCooldown) { "cooldown {0:N0}s/{1}s" -f $sinceCycle, $COOLDOWN_SECONDS } else { "ready" }

  $line = "[{0}] ping {1} {2} | consecFail {3}/{4} | ok {5}/{6} ({7}%) | avg{8}s {9} | lastRestart {10} ({11}s) | {12}" -f `
            $now.ToString("HH:mm:ss"), $status, $msTxt, $consecutiveFails, $FAILS_TO_TRIGGER, $okCount, $total, $okPct, `
            $LAT_WINDOW, $avgTxt, $lrTxt, $sinceRestartSec, $coolTxt

  Write-Host "`r$line".PadRight([console]::WindowWidth - 1) -NoNewline

  if ($consecutiveFails -ge $FAILS_TO_TRIGGER) {
    if (-not $inCooldown) {
      Write-Host ""
      $didRestart = $false
      try {
        $manualActive = $triggerEvent.WaitOne(0)  # manual-reset event, non-consuming
        if ($manualActive) {
          LogRestart ("Manual trigger received via event '{0}'" -f $TRIGGER_EVENT_NAME)
          try { $triggerEvent.Reset() } catch {}
        }

        # Attempt restart; may skip if lock held (no log on skip)
        $didRestart = PowerCycle-OrTurnOn-Plug_WithDomeSequence
      } catch {
        LogRestart ("ERROR during restart: " + $_.Exception.Message)
        $didRestart = $false
      }

      if ($didRestart) {
        $lastCycle = Get-Date
        $lastRestart = $lastCycle
        $consecutiveFails = 0

        if ($POST_CYCLE_GRACE_SEC -gt 0) {
          LogRestart ("Post-cycle grace: waiting {0}s" -f $POST_CYCLE_GRACE_SEC)
          Start-Sleep -Seconds $POST_CYCLE_GRACE_SEC
        }
      } else {
        # Lock was busy (or restart not performed). Avoid collision by entering cooldown anyway.
        if ($manualActive) { try { $triggerEvent.Set() } catch {} }
        Write-Host ("[{0}] Restart skipped: another script is cycling power (lock '{1}' held). Entering cooldown." -f $now.ToString("HH:mm:ss"), $LOCK_NAME)
        $lastCycle = Get-Date
        $consecutiveFails = 0
      }

      Write-Host ""
    }
  }

  Start-Sleep -Seconds $PING_INTERVAL_SEC
}
