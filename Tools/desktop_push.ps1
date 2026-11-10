# Pushes the current branch through GitHub Desktop (git on the command line has no saved login).
# Usage: Tools\desktop_push.ps1 -Send   (without -Send it only brings the window forward and takes a screenshot)
param([switch]$Send)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class W { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
[DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e); }
"@
$p = Get-Process GitHubDesktop -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) {
  $exe = Get-ChildItem "$env:LOCALAPPDATA\GitHubDesktop\GitHubDesktop.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
  Start-Process $exe.FullName; Start-Sleep 8
  $p = Get-Process GitHubDesktop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
}
# Alt tap lets this process take the foreground.
[W]::keybd_event(0x12,0,0,[UIntPtr]::Zero); [W]::keybd_event(0x12,0,2,[UIntPtr]::Zero)
[W]::ShowWindow($p.MainWindowHandle, 3) | Out-Null
[W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep 2
if ($Send) { [System.Windows.Forms.SendKeys]::SendWait("^p"); Start-Sleep 8 }
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$out = Join-Path $PSScriptRoot "..\.livebridge\desktop.png"
$small = New-Object System.Drawing.Bitmap $bmp, ([int]($b.Width/2)), ([int]($b.Height/2))
$small.Save($out); "saved $out"

