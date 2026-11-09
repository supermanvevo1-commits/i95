# Sends one command to the open Unity editor through Assets/Editor/LiveBridge.cs and prints the reply.
# Example: powershell -File Tools/live.ps1 status
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Words)
$dir = Join-Path (Split-Path $PSScriptRoot) '.livebridge'
$id = [guid]::NewGuid().ToString('N').Substring(0, 8)
$res = Join-Path $dir "res_$id.txt"
[IO.File]::WriteAllText((Join-Path $dir 'cmd.tmp'), "$id $($Words -join ' ')")
Move-Item -Force (Join-Path $dir 'cmd.tmp') (Join-Path $dir 'cmd.txt')
for ($i = 0; $i -lt 600; $i++) {
  if (Test-Path $res) { Get-Content $res; Remove-Item $res -Confirm:$false; exit 0 }
  Start-Sleep -Milliseconds 100
}
'TIMEOUT: Unity did not answer'; exit 1
