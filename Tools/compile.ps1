# Refreshes Unity, waits for the compile, and prints recent errors. Then check the dll timestamps (see CLAUDE.md).
$p = Split-Path $PSScriptRoot -Parent; $L="$p\Tools\live.ps1"; $hb="$p\.livebridge\heartbeat.txt"; $c="$p\.livebridge\console.log"
$start=(Get-Content $c).Count
& powershell -NoProfile -File $L stop | Out-Null
Start-Sleep 3
& powershell -NoProfile -File $L refresh
Start-Sleep 30
for($i=0;$i -lt 300;$i++){ $h=Get-Content $hb -ErrorAction SilentlyContinue; if($h -match "compiling=False"){break}; Start-Sleep 1 }
Start-Sleep 5
"heartbeat: $(Get-Content $hb)"
Get-Content $c | Select-Object -Skip $start | Where-Object {$_ -match 'error CS|Exception'} | Select-Object -First 15
"done"

