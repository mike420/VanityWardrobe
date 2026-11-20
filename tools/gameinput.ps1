param([string]$Action, [string]$Arg1 = "", [string]$Arg2 = "", [string]$Out = "")
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, int d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte sc, uint f, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  public struct RECT { public int L, T, R, B; }
}
"@
[W]::SetProcessDPIAware() | Out-Null
$p = Get-Process valheim -ErrorAction Stop | Select-Object -First 1
$h = $p.MainWindowHandle
[W]::ShowWindow($h, 9) | Out-Null
[W]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 250
$r = New-Object W+RECT; [W]::GetWindowRect($h, [ref]$r) | Out-Null
function Shot($path) {
  $w = $r.R - $r.L; $hh = $r.B - $r.T
  $bmp = New-Object System.Drawing.Bitmap $w, $hh
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}
switch ($Action) {
  "shot"  { Shot $Out; "$($r.R-$r.L)x$($r.B-$r.T) at $($r.L),$($r.T)" }
  "click" { $x = $r.L + [int]$Arg1; $y = $r.T + [int]$Arg2; [W]::SetCursorPos($x, $y) | Out-Null; Start-Sleep -Milliseconds 120; [W]::mouse_event(2,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [W]::mouse_event(4,0,0,0,[UIntPtr]::Zero) }
  "move"  { [W]::SetCursorPos($r.L + [int]$Arg1, $r.T + [int]$Arg2) | Out-Null }
  "key"   { $vk = [byte][int]$Arg1; [W]::keybd_event($vk,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 90; [W]::keybd_event($vk,0,2,[UIntPtr]::Zero) }
  "scroll"{ [W]::mouse_event(0x800,0,0,[int]$Arg1,[UIntPtr]::Zero) }
}
