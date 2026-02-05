# restartScopedomeLocalv3.ps1
# N.I.N.A helper: request the watchdog to run its restart sequence (power-cycle + FindHome)
# by signalling a named event. The watchdog consumes this and behaves as if it saw 5 failed pings.

param(
  [string]$EventName = "ScopeDomeWatchdog.TriggerRestart"
)

$ErrorActionPreference = "Stop"

$createdNew = $false
$evt = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, $EventName, [ref]$createdNew)

try {
  [void]$evt.Set()
  Write-Host ("[{0}] Restart requested: signalled event '{1}' (createdNew={2})." -f (Get-Date).ToString("yyyy-MM-dd HH:mm:ss"), $EventName, $createdNew)
  exit 0
}
catch {
  Write-Error ("Failed to signal event '{0}': {1}" -f $EventName, $_.Exception.Message)
  exit 1
}
finally {
  if ($evt) { $evt.Dispose() }
}
