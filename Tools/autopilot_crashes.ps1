# Scored crash test course. Requires the open editor with Assets/Editor/LiveBridge.cs running.
param([switch]$SkipRebuild)
$p = Split-Path $PSScriptRoot; $L = "$PSScriptRoot\live.ps1"; $hb = "$p\.livebridge\heartbeat.txt"; $c = "$p\.livebridge\console.log"
$script:fails = 0
function U { param([Parameter(ValueFromRemainingArguments=$true)]$w) & powershell -NoProfile -File $L @w | Out-String }
function WaitPlay($want){ for($i=0;$i -lt 240;$i++){ $h=Get-Content $hb -ErrorAction SilentlyContinue; if($h -match "playing=$want compiling=False"){return}; Start-Sleep 1 }; throw "timeout waiting for playing=$want" }
function S { $r=@{}; foreach($m in [regex]::Matches((U status),'(\w+)=(\S+)')){ $r[$m.Groups[1].Value]=$m.Groups[2].Value }; $r }
function Check($name,[bool]$ok,$info){ if($ok){"PASS  $name  ($info)"}else{"FAIL  $name  ($info)";$script:fails++} }
function Crash($kind,$off,$mph){ U repair|Out-Null; U frontview off|Out-Null; U launchat $kind $off $mph|Out-Null; U hold 0 $mph|Out-Null; Start-Sleep 3; S }
$start = if(Test-Path $c){(Get-Content $c).Count}else{0}
if(-not $SkipRebuild){ U refresh|Out-Null; Start-Sleep 8; WaitPlay 'False'; Start-Sleep 3; U rebuild|Out-Null; WaitPlay 'False'; U refresh|Out-Null; Start-Sleep 4; WaitPlay 'False' }
U play|Out-Null; WaitPlay 'True'; Start-Sleep 4; U park|Out-Null

$a=Crash pier 0 30; $b=Crash pier 0 60; $d=Crash pier 0 100
Check 'Harder hits do more damage' ([double]$a.damage -lt [double]$b.damage -and [double]$b.damage -lt [double]$d.damage) "30mph=$($a.damage) 60mph=$($b.damage) 100mph=$($d.damage)"
foreach($x in @($a,$b,$d)){ Check 'No rebound off a pier' ([math]::Abs([double]$x.mph) -lt 1) "mph=$($x.mph)" }
Check 'Nose visibly crushed at speed' ([double]$d.crush -gt 0.3) "crush=$($d.crush)"
Check '100 mph pier hit disables the car' ($d.disabled -eq 'True') "disabled=$($d.disabled)"
U frontview on|Out-Null; Start-Sleep 2; U shot auto_wrecked|Out-Null
U hold 0 50|Out-Null; Start-Sleep 2; $x=S
Check 'Disabled car cannot accelerate' ([math]::Abs([double]$x.mph) -lt 2) "mph=$($x.mph)"
U repair|Out-Null; U frontview on|Out-Null; Start-Sleep 2; $x=S; U shot auto_repaired|Out-Null; U frontview off|Out-Null
Check 'Recovery repairs the car' ([double]$x.damage -eq 0 -and $x.disabled -eq 'False') "damage=$($x.damage)"

U repair|Out-Null; U launch 3000 -1.83 124 0|Out-Null; U drive 200 0|Out-Null; Start-Sleep 8; $base=S
U launch 3000 -1.83 124 0|Out-Null; U setdamage 0.5|Out-Null; U drive 200 0|Out-Null; Start-Sleep 8; $hurt=S
Check 'Damage lowers top speed, stays on road' ([double]$hurt.mph -lt [double]$base.mph - 15 -and [double]$hurt.mph -gt 80 -and $hurt.offroad -eq 'False') "healthy=$($base.mph) damaged=$($hurt.mph) offroad=$($hurt.offroad)"
U repair|Out-Null; U launch 3000 -9 60 0|Out-Null; U hold 0 60|Out-Null; Start-Sleep 3; $h0=S
U repair|Out-Null; U launch 3000 -9 60 0|Out-Null; U setdamage 0.5|Out-Null; U hold 0 60|Out-Null; Start-Sleep 3; $h1=S
Check 'Damage makes the car pull to one side' ([math]::Abs([double]$h1.lateral-[double]$h0.lateral) -gt 1) "lateral healthy=$($h0.lateral) damaged=$($h1.lateral)"

$x=Crash post 0 50
Check 'Sign post snaps and car keeps going' ([double]$x.mph -gt 30 -and [double]$x.damage -lt 10) "mph=$($x.mph) damage=$($x.damage)"
U repair|Out-Null; U launch 7890 0.3 70 6|Out-Null; U hold 0 70|Out-Null; Start-Sleep 2; $x=S
Check 'Shallow barrier scrape keeps moving' ([double]$x.mph -gt 40 -and [double]$x.damage -lt 40) "mph=$($x.mph) damage=$($x.damage) impact=$($x.lastHit)"
$reading = [double](($x.lastHit -split '@')[1] -replace 'mph','')
Check 'Barrier impact speed is recorded' ($reading -gt 3) "lastHit=$($x.lastHit)"
U repair|Out-Null; U launch 7890 -6 70 35|Out-Null; U hold 0 70|Out-Null; Start-Sleep 1; $x=S
Check 'Steep barrier hit hurts more' ([double]$x.damage -gt 15) "damage=$($x.damage) lastHit=$($x.lastHit)"

$x=Crash pier 0.9 50
U shot auto_offset|Out-Null
Check 'Off-center hit: moderate damage, turns the car' ([double]$x.damage -gt 5 -and [double]$x.damage -lt 50 -and [math]::Abs([double]$x.heading) -gt 5) "damage=$($x.damage) heading=$($x.heading)"
U repair|Out-Null; U aim pier 0|Out-Null; U hold 0 50|Out-Null; Start-Sleep 7; U reverse|Out-Null; Start-Sleep 3; $x=S
Check 'Can reverse out of a crash' ([double]$x.mph -lt -5) "mph=$($x.mph) damage=$($x.damage)"
U repair|Out-Null; U drive 70 1|Out-Null; Start-Sleep 6; $x=S
Check 'Clean driving after repair' ([double]$x.damage -eq 0 -and [math]::Abs([double]$x.mph-70) -lt 3) "mph=$($x.mph) damage=$($x.damage)"
U stop|Out-Null; WaitPlay 'False'
$errors = Get-Content $c | Select-Object -Skip $start | Where-Object {$_ -match '^\S+ (Error|Exception)|NullReference'}
Check 'No console errors' ($errors.Count -eq 0) ($errors | Select-Object -First 3 | Out-String)
"FAILURES: $script:fails"
