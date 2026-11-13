# End-to-end check: runs the real exe, presses the global hotkeys, drags the mouse, drives the settings window,
# pastes into Word and Edge. Heads-up: it takes over keyboard/mouse for ~40 s.
# Usage: .\test.ps1 [-Exe build\DonatScreen.exe] [-Shots <dir for UI screenshots>]
param([string]$Exe = "$PSScriptRoot\build\DonatScreen.exe", [string]$Shots = "")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class In {
  [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
  [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  public struct RECT { public int L, T, R, B; }
  public static IntPtr Foreground() { return GetForegroundWindow(); }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, string l);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, System.Text.StringBuilder l, uint flags, uint ms, out IntPtr result);
  delegate bool EnumProc(IntPtr h, IntPtr l);
  public static bool HasVScroll(IntPtr h) { return (GetWindowLong(h, -16) & 0x00200000) != 0; }   // WS_VSCROLL
  // WM_GETTEXT (GetWindowText can't read another process's edit boxes), with a timeout so a hung window can't stall the run.
  static string Text(IntPtr h) { var sb = new System.Text.StringBuilder(1024); IntPtr r; SendMessageTimeout(h, 0x000D, (IntPtr)1024, sb, 2, 500, out r); return sb.ToString(); }
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  static bool Ours(IntPtr h, int pid) { uint p; GetWindowThreadProcessId(h, out p); return p == pid; }
  // Process.MainWindowHandle can point at a tooltip or a closed dialog; look windows up by title instead.
  public static IntPtr TopWindow(int pid, string title) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { if (Ours(h, pid) && IsWindowVisible(h) && Text(h) == title) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  public static IntPtr ChildWithText(int pid, string text) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((top, l0) => {
      if (Ours(top, pid)) EnumChildWindows(top, (h, l) => { if (Text(h) == text) { found = h; return false; } return true; }, IntPtr.Zero);
      return found == IntPtr.Zero;
    }, IntPtr.Zero);
    return found;
  }
  public static uint ForegroundPid() { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid; }
  public static void Keys(params byte[] vks) {
    foreach (var k in vks) keybd_event(k, 0, 0, UIntPtr.Zero);
    for (int i = vks.Length - 1; i >= 0; i--) keybd_event(vks[i], 0, 2, UIntPtr.Zero);
  }
  public static void Down() { mouse_event(2, 0, 0, 0, UIntPtr.Zero); }
  public static void Up() { mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
}
'@
[In]::SetProcessDPIAware() | Out-Null   # match the app: real pixels
$CtrlShift1 = [byte[]](0x11, 0x10, 0x31); $CtrlShift2 = [byte[]](0x11, 0x10, 0x32)
$HollowStar = [string][char]0xE734; $Cross = [string][char]0xE711   # icon-font glyphs; .ps1 is read as ANSI: no literals
$CtrlAltShiftF11 =[byte[]](0x11, 0x12, 0x10, 0x7A); $WinShiftS = [byte[]](0x5B, 0x10, 0x53); $WinShiftZ = [byte[]](0x5B, 0x10, 0x5A)

$dir = Join-Path $env:TEMP 'DonatScreenTest'
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $dir | Out-Null
Copy-Item $Exe $dir
$exePath = Join-Path $dir 'DonatScreen.exe'
$ini = Join-Path $dir 'DonatScreen.ini'
$out = Join-Path $dir 'shots'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$savedRun = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).DonatScreen
# Start from "autostart off" and always put the user's own entry back, even if the run dies halfway.
Remove-ItemProperty $runKey DonatScreen -ErrorAction SilentlyContinue
trap { if ($savedRun) { Set-ItemProperty $runKey DonatScreen $savedRun }; break }
Get-Process DonatScreen -ErrorAction SilentlyContinue | Stop-Process -Force

$fails = 0
function Check($name, $ok, $detail = '') { if ($ok) { "PASS  $name" } else { $script:fails++; "FAIL  $name  $detail" } }

# 0. Every Indonesian UI key (L.T("...") and tray Item(menu, "...")) has an English entry in Lang.cs with the same {n} slots.
$lit = '"((?:[^"\\]|\\.)*)"'
$src = Get-ChildItem $PSScriptRoot -Filter *.cs | Where-Object Name -ne 'Lang.cs' | ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 }
$keys = @($src | ForEach-Object { [regex]::Matches($_, "(?:L\.T|Item\(menu,)\s*\(?$lit") } | ForEach-Object { $_.Groups[1].Value }) | Sort-Object -Unique
$en = @{}
[regex]::Matches((Get-Content "$PSScriptRoot\Lang.cs" -Raw -Encoding UTF8), "\[$lit\]\s*=\s*$lit") | ForEach-Object { $en[$_.Groups[1].Value] = $_.Groups[2].Value }
$missing = @($keys | Where-Object { -not $en.ContainsKey($_) })
Check "every UI text has an English translation ($($keys.Count) keys)" ($missing.Count -eq 0) ($missing -join ' | ')
$slots = @($keys | Where-Object { $en.ContainsKey($_) -and ([regex]::Matches($_, '\{\d\}').Count -ne [regex]::Matches($en[$_], '\{\d\}').Count) })
Check 'translations keep their {n} slots' ($slots.Count -eq 0) ($slots -join ' | ')
function Pngs { @(Get-ChildItem $out -Filter *.png -ErrorAction SilentlyContinue | Sort-Object LastWriteTime) }
function WaitNew($before, $ms = 3000) {
  $t = [Diagnostics.Stopwatch]::StartNew()
  while ($t.ElapsedMilliseconds -lt $ms) { $p = Pngs; if ($p.Count -gt $before) { Start-Sleep -Milliseconds 400; return $p[-1] }; Start-Sleep -Milliseconds 100 }
}
function Dim($file) { $i = [Drawing.Image]::FromFile($file.FullName); try { "$($i.Width)x$($i.Height)" } finally { $i.Dispose() } }
function Snap($name, [Drawing.Rectangle]$r) {
  if (-not $Shots) { return }
  $b = New-Object Drawing.Bitmap $r.Width, $r.Height
  $g = [Drawing.Graphics]::FromImage($b); $g.CopyFromScreen($r.Location, [Drawing.Point]::Empty, $r.Size); $g.Dispose()
  $b.Save((Join-Path $Shots "$name.png")); $b.Dispose()
}
function WindowRect($proc) { $r = New-Object In+RECT; [In]::GetWindowRect((AppWindow), [ref]$r) | Out-Null; [Drawing.Rectangle]::FromLTRB($r.L, $r.T, $r.R, $r.B) }
function Start-App { $p = Start-Process $exePath -PassThru; $p.WaitForInputIdle(10000) | Out-Null; Start-Sleep -Milliseconds 500; $p }   # idle = hotkeys registered
# Opens the region overlay; mouse/Esc input is only sent once the overlay owns the foreground,
# so a broken build can never click into whatever else is on screen.
function Region($name, [scriptblock]$drive, [byte[]]$keys = $CtrlShift2) {
  [In]::Keys($keys)
  Start-Sleep -Milliseconds 700
  if ([In]::ForegroundPid() -ne $app.Id) { Check "$name (overlay opened)" $false | Write-Host; return $false }
  & $drive; $true
}
# WinForms on .NET Framework exposes controls to UI Automation by window text only: shortcut boxes and the
# toggle are Panes named after their label; the status line is the last Text element.
# Our visible window: the save dialog if open, else settings.
function AppWindow { foreach ($t in 'Save screenshot', 'Donat Screen') { $h = [In]::TopWindow($app.Id, $t); if ($h -ne [IntPtr]::Zero) { return $h } }; [IntPtr]::Zero }
function UiAll($type) {
  $h = AppWindow; if ($h -eq [IntPtr]::Zero) { return @() }
  $root = [Windows.Automation.AutomationElement]::FromHandle($h)
  @($root.FindAll([Windows.Automation.TreeScope]::Descendants,
    (New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::ControlTypeProperty), $type)))
}
function Ui($name, $nth = 0) { @(UiAll ([Windows.Automation.ControlType]::Pane) | Where-Object { $_.Current.Name -eq $name })[$nth] }
function Names { UiAll ([Windows.Automation.ControlType]::Pane) | ForEach-Object { $_.Current.Name } }
function Title { foreach ($t in 'Save screenshot', 'Donat Screen') { if ([In]::TopWindow($app.Id, $t) -ne [IntPtr]::Zero) { return $t } }; '' }
function TypeText($text) { foreach ($c in $text.ToUpper().ToCharArray()) { [In]::Keys([byte][char]$c) } }
function Status { $all = @(UiAll ([Windows.Automation.ControlType]::Text)); if ($all.Count) { $all[-1].Current.Name } }
# Real click on a settings control, only while our settings window is the foreground window.
function Click($name, $nth = 0) {
  if ([In]::ForegroundPid() -ne $app.Id) { return $false }
  $r = (Ui $name $nth).Current.BoundingRectangle
  [In]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
  [In]::Down(); [In]::Up(); Start-Sleep -Milliseconds 400
  $true
}

# 1. First run: creates the ini and opens settings.
$app = Start-App
foreach ($i in 1..25) { if ((Test-Path $ini) -and $app.MainWindowTitle) { break }; Start-Sleep -Milliseconds 200; $app.Refresh() }   # slow first start
Check 'first run creates ini' (Test-Path $ini)
Check 'first run opens settings' ($app.MainWindowTitle -eq 'Donat Screen') "title='$($app.MainWindowTitle)'"
$lines = Get-Content $ini -ErrorAction SilentlyContinue
Check 'first run is in English' ($lines -contains 'Lang=en' -and $null -ne (Ui 'Start with Windows')) ($lines -join ' | ')
Snap 'settings' (WindowRect $app)
$app.Kill(); $app.WaitForExit()

# 2. v1.0 ini (single relative Folder=) still works + "don't ask" mode saves straight away.
Set-Content $ini 'Folder=shots', 'Ask=0'
$app = Start-App
$vs = [Windows.Forms.SystemInformation]::VirtualScreen
$want = "$($vs.Width)x$($vs.Height)"
[In]::Keys($CtrlShift1)
$f = WaitNew 0
Check 'full screen saved next to exe (relative folder)' ($null -ne $f)
if ($f) { Check 'full screen covers every monitor' ((Dim $f) -eq $want) "got $(Dim $f), want $want" }

# 3. Clipboard: both formats, then real pastes into Word and Edge.
$data = [Windows.Forms.Clipboard]::GetDataObject()
$formats = $data.GetFormats()
Check 'clipboard has PNG + Bitmap' ($formats -contains 'PNG' -and $formats -contains 'Bitmap') ($formats -join ',')
$img = [Windows.Forms.Clipboard]::GetImage()
Check 'clipboard bitmap is the screenshot' ($img -and "$($img.Width)x$($img.Height)" -eq $want)
$png = [Drawing.Image]::FromStream($data.GetData('PNG'))
Check 'clipboard PNG is the screenshot' ("$($png.Width)x$($png.Height)" -eq $want)
try {
  $word = New-Object -ComObject Word.Application
  $doc = $word.Documents.Add(); $word.Selection.Paste()
  Check 'pastes into Word' ($doc.InlineShapes.Count -eq 1) "shapes=$($doc.InlineShapes.Count)"
  $doc.Close(0); $word.Quit()
} catch { "SKIP  Word paste ($($_.Exception.Message))" }
$py = Join-Path $dir 'paste.py'
@'
from playwright.sync_api import sync_playwright
with sync_playwright() as p:
    b = p.chromium.launch(channel="msedge", headless=False)
    pg = b.new_page()
    pg.set_content("""<div id=t contenteditable style="width:400px;height:200px"></div><script>
      window.r = null;
      t.addEventListener('paste', async e => {
        const it = [...e.clipboardData.items].find(i => i.type.startsWith('image/'));
        if (!it) { window.r = 'no image: ' + [...e.clipboardData.types]; return; }
        const bmp = await createImageBitmap(it.getAsFile());
        window.r = bmp.width + 'x' + bmp.height;
      });</script>""")
    pg.click("#t")
    pg.keyboard.press("Control+V")
    pg.wait_for_function("window.r !== null", timeout=8000)
    print(pg.evaluate("window.r"))
    b.close()
'@ | Set-Content $py
$edge = (& python -I $py 2>&1 | Select-Object -Last 1)
Check 'pastes into Edge (browser)' ("$edge" -eq $want) "got '$edge'"

# 4. Region drag.
$x0 = $vs.X + 200; $y0 = $vs.Y + 200
[In]::SetCursorPos($x0, $y0) | Out-Null
$before = (Pngs).Count
if (Region 'region drag' {
  Snap 'overlay-hint' $vs
  [In]::Down(); foreach ($i in 1..10) { [In]::SetCursorPos($x0 + 40 * $i, $y0 + 30 * $i) | Out-Null; Start-Sleep -Milliseconds 20 }
  Start-Sleep -Milliseconds 200
  Snap 'overlay-drag' $vs
  [In]::Up()
}) {
  $f = WaitNew $before
  Check 'region saved' ($null -ne $f)
  if ($f) { Check 'region size matches drag' ((Dim $f) -eq '401x301') "got $(Dim $f)" }
}

# 5. Region click = the monitor under the cursor.
$p = [Windows.Forms.Screen]::PrimaryScreen.Bounds
[In]::SetCursorPos($p.X + $p.Width / 2, $p.Y + $p.Height / 2) | Out-Null
$before = (Pngs).Count
if (Region 'region click' { [In]::Down(); [In]::Up() }) {
  $f = WaitNew $before
  Check 'click captures one monitor' ($f -and (Dim $f) -eq "$($p.Width)x$($p.Height)") "got $(if ($f) { Dim $f })"
}

# 6. Esc cancels.
$before = (Pngs).Count
if (Region 'Esc' { [In]::Keys(0x1B) }) {
  Start-Sleep -Milliseconds 800
  Check 'Esc cancels without saving' ((Pngs).Count -eq $before -and [In]::ForegroundPid() -ne $app.Id) "count=$((Pngs).Count)"
}

# 7. Second launch exits and pops the first instance's settings.
$second = Start-Process $exePath -PassThru
Check 'second instance exits' ($second.WaitForExit(3000))
Start-Sleep -Milliseconds 800; $app.Refresh()
Check 'first instance shows settings' ($app.MainWindowTitle -eq 'Donat Screen') "title='$($app.MainWindowTitle)'"

# 8. Recording shortcuts: Windows-reserved, duplicate, then valid ones (incl. Win).
if (Click 'Capture area') {
  [In]::Keys($WinShiftS); Start-Sleep -Milliseconds 500
  Check 'refuses Win+Shift+S (Windows snip)' ((Status) -match 'built-in Windows') "status='$(Status)'"
  Check 'Win+Shift+S swallowed while recording' ([In]::ForegroundPid() -eq $app.Id)
  [In]::Keys($CtrlShift1); Start-Sleep -Milliseconds 400
  Check 'refuses the other action''s shortcut' ((Status) -match 'Capture full screen') "status='$(Status)'"
  [In]::Keys(0x11, 0x51); Start-Sleep -Milliseconds 400   # Ctrl+Q
  Check 'refuses single-modifier app shortcut' ((Status) -match 'at least two') "status='$(Status)'"
  [In]::Keys($CtrlAltShiftF11); Start-Sleep -Milliseconds 500
  Check 'accepts Ctrl+Alt+Shift+F11' ((Get-Content $ini) -contains 'RegionKey=F11, Shift, Control, Alt') ((Get-Content $ini) -join ' | ')
} else { Check 'settings window in front for recording' $false }
if (Click 'Capture full screen') {
  [In]::Keys($WinShiftZ); Start-Sleep -Milliseconds 500
  Check 'accepts Win+Shift+Z' ((Get-Content $ini) -contains 'FullKey=Z, Shift, Win') ((Get-Content $ini) -join ' | ')
  Snap 'settings-recorded' (WindowRect $app)
} else { Check 'settings window in front for recording' $false }

# 9. Autostart toggle writes / removes the Run entry for this exe.
$toggle = 'Start with Windows'
if (Click $toggle) {
  Check 'autostart on -> Run entry for this exe' ((Get-ItemProperty $runKey).DonatScreen -eq "`"$exePath`" --autostart") "got '$((Get-ItemProperty $runKey).DonatScreen)'"
  Snap 'settings-autostart' (WindowRect $app)
  Click $toggle | Out-Null
  Check 'autostart off -> Run entry removed' ($null -eq (Get-ItemProperty $runKey).DonatScreen)
} else { Check 'settings window in front for autostart' $false }
$app.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 600
Check 'app keeps running in tray after closing settings' (-not $app.HasExited)

# 10. The recorded shortcuts are live.
$before = (Pngs).Count
[In]::Keys($WinShiftZ)
Check 'Win+Shift+Z takes a full screenshot' ($null -ne (WaitNew $before))
if (Region 'Ctrl+Alt+Shift+F11 opens the overlay' { [In]::Keys(0x1B) } $CtrlAltShiftF11) { Check 'Ctrl+Alt+Shift+F11 opens the overlay' $true }
$app.Kill(); $app.WaitForExit()

# 11. Hotkey taken by someone else: app survives, the other hotkey still works.
Set-Content $ini 'Dest=1|shots', 'Ask=0', 'RegionKey=D2, Shift, Control', 'FullKey=D1, Shift, Control'
[In]::RegisterHotKey([IntPtr]::Zero, 7, 6, 0x31) | Out-Null  # we steal Ctrl+Shift+1
$app = Start-App
Check 'app survives a taken hotkey' (-not $app.HasExited)
[In]::SetCursorPos($p.X + 300, $p.Y + 300) | Out-Null
$before = (Pngs).Count
if (Region 'other hotkey still works' { [In]::Down(); [In]::SetCursorPos($p.X + 400, $p.Y + 400) | Out-Null; Start-Sleep -Milliseconds 50; [In]::Up() }) {
  Check 'other hotkey still works' ($null -ne (WaitNew $before))
}
[In]::UnregisterHotKey([IntPtr]::Zero, 7) | Out-Null
$app.Kill(); $app.WaitForExit()

# 12. Save dialog: only ticked folders offered, rename, never overwrite, Esc -> default, invalid names refused.
$kerja = Join-Path $dir 'kerja'
Set-Content $ini 'Dest=1|shots', 'Dest=1|kerja', 'Dest=0|hidden', 'Default=0', 'Ask=1', 'RegionKey=D2, Shift, Control', 'FullKey=D1, Shift, Control'
$app = Start-App
function Dialog { [In]::Keys($CtrlShift1); Start-Sleep -Milliseconds 900; (Title) -eq 'Save screenshot' -and [In]::ForegroundPid() -eq $app.Id }
function Count($folder) { @(Get-ChildItem $folder -Filter *.png -ErrorAction SilentlyContinue).Count }
[Windows.Forms.Clipboard]::Clear()
if (Dialog) {
  Check 'dialog offers ticked folders only' ((Names) -contains 'shots' -and (Names) -contains 'kerja' -and (Names) -notcontains 'hidden') ((Names) -join ' | ')
  Check 'clipboard filled before choosing' ([Windows.Forms.Clipboard]::ContainsImage())
  Snap 'save-dialog' (WindowRect $app)
  TypeText 'laporan'; Click 'kerja' | Out-Null; [In]::Keys(0x0D); Start-Sleep -Milliseconds 700   # default stays ticked + tick kerja
  Check 'rename + two ticked folders -> saved in both' ((Test-Path "$out\laporan.png") -and (Test-Path "$kerja\laporan.png"))
  Check 'dialog closes after saving' ((Title) -ne 'Save screenshot')
} else { Check 'save dialog opens' $false "title='$(Title)'" }
if (Dialog) {
  TypeText 'laporan'; Click 'shots' | Out-Null; Click 'kerja' | Out-Null; [In]::Keys(0x0D); Start-Sleep -Milliseconds 700
  Check 'untick default, tick kerja -> only kerja; same name never overwrites' ((Test-Path "$kerja\laporan (2).png") -and -not (Test-Path "$out\laporan (2).png"))
}
$before = Count $out
if (Dialog) {
  [In]::Keys(0x1B); Start-Sleep -Milliseconds 600
  Check 'Esc saves to default with automatic name' ((Count $out) -eq $before + 1 -and (Pngs)[-1].Name -like 'DonatScreen_*')
}
$before = Count $out
if (Dialog) {
  [In]::Keys(0x41); [In]::Keys(0x10, 0xBA); [In]::Keys(0x42); [In]::Keys(0x0D); Start-Sleep -Milliseconds 500   # a:b
  Check 'invalid name refused, dialog stays open' ((Title) -eq 'Save screenshot' -and (Count $out) -eq $before)
  [In]::Keys(0x1B); Start-Sleep -Milliseconds 600
}
$before = (Count $out) + (Count $kerja)
if (Dialog) {
  Click 'shots' | Out-Null; [In]::Keys(0x0D); Start-Sleep -Milliseconds 500   # nothing ticked
  Check 'nothing ticked -> refused, dialog stays open' ((Title) -eq 'Save screenshot' -and (Count $out) + (Count $kerja) -eq $before)
  [In]::Keys(0x1B); Start-Sleep -Milliseconds 600
}
$before = Count $out
[In]::SetCursorPos($p.X + 300, $p.Y + 300) | Out-Null
if (Region 'region + dialog' { [In]::Down(); [In]::SetCursorPos($p.X + 500, $p.Y + 400) | Out-Null; Start-Sleep -Milliseconds 50; [In]::Up() }) {
  Start-Sleep -Milliseconds 900
  if ((Title) -eq 'Save screenshot') { [In]::Keys(0x0D); Start-Sleep -Milliseconds 600 }
  $f = WaitNew $before
  Check 'region capture goes through the dialog too' ($f -and (Dim $f) -eq '201x101') "got $(if ($f) { Dim $f })"
}

# 13. Folder list in settings: default star, tick, remove, add via the folder picker.
Start-Process $exePath | Out-Null; Start-Sleep -Milliseconds 1200   # second launch -> settings
if ((Title) -eq 'Donat Screen') {
  Snap 'settings-folders' (WindowRect $app)
  Click $HollowStar 0 | Out-Null   # first hollow star = kerja
  Check 'star makes kerja the default' ((Get-Content $ini) -contains 'Default=1')
  Click 'hidden' | Out-Null
  Check 'tick shows hidden in the dialog' ((Get-Content $ini) -contains 'Dest=1|hidden')
  Click $Cross 2 | Out-Null
  Check 'remove drops hidden from the list' (-not ((Get-Content $ini) -match 'hidden'))
  $added = Join-Path $dir 'baru'; New-Item -ItemType Directory $added | Out-Null
  Click 'Add folder' | Out-Null; Start-Sleep -Milliseconds 1200
  # Fill the picker through UI Automation (no keystrokes: they would land in whatever window has focus).
  $A = [Windows.Automation.AutomationElement]
  # UIA nests owned dialogs under their owner, so take the picker straight from the foreground window.
  $picker = if ([In]::ForegroundPid() -eq $app.Id) { $A::FromHandle([In]::Foreground()) }
  if ($picker -and $picker.Current.ClassName -eq '#32770') {
    # Targeted window messages, not keystrokes: WM_SETTEXT into the focused "Folder:" box, BM_CLICK on Select Folder (id 1).
    [In]::SendMessage([IntPtr]$A::FocusedElement.Current.NativeWindowHandle, 0x000C, [IntPtr]::Zero, $added) | Out-Null
    $ok = $picker.FindFirst([Windows.Automation.TreeScope]::Children, (New-Object Windows.Automation.PropertyCondition ($A::AutomationIdProperty), '1'))
    [In]::SendMessage([IntPtr]$ok.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, $null) | Out-Null
    Start-Sleep -Milliseconds 1200
    Check 'Add folder adds the picked folder (stored relative)' ((Get-Content $ini) -contains 'Dest=1|baru') ((Get-Content $ini) -join ' | ')
  } else { Check 'folder picker opens' $false }
  Start-Sleep -Milliseconds 500

  # Path line is editable: click it like a user (guarded), then WM_SETTEXT + WM_KEYDOWN(Enter) straight to the edit box.
  function EditPath($row, $from, $to) {
    if ([In]::ForegroundPid() -ne $app.Id) { return $false }
    $r = (Ui $row).Current.BoundingRectangle   # path line = lower half of the row
    [In]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height * 3 / 4)) | Out-Null
    [In]::Down(); [In]::Up(); Start-Sleep -Milliseconds 300
    $edit = [In]::ChildWithText($app.Id, $from)
    if ($edit -eq [IntPtr]::Zero) { return $false }
    [In]::SendMessage($edit, 0x000C, [IntPtr]::Zero, $to) | Out-Null
    [In]::PostMessage($edit, 0x0100, [IntPtr]0x0D, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 800
    $true
  }
  $moved = Join-Path $dir 'ganti'; New-Item -ItemType Directory $moved | Out-Null
  if (EditPath 'baru' $added $moved) {
    Check 'editing the path repoints the folder' ((Get-Content $ini) -contains 'Dest=1|ganti' -and -not ((Get-Content $ini) -match 'baru'))
    Check 'folder name follows the new path' ((Names) -contains 'ganti') ((Names) -join ' | ')
  } else { Check 'path editor found' $false }
  $before = (Get-Content $ini) -join '|'
  if (EditPath 'ganti' $moved (Join-Path $dir 'tidak-ada')) {
    Check 'missing folder refused, setting kept' ((Status) -match 'not found' -and ((Get-Content $ini) -join '|') -eq $before) "status='$(Status)'"
    Check 'refused edit reverts the text' ([In]::ChildWithText($app.Id, $moved) -ne [IntPtr]::Zero)
  }
  if (EditPath 'ganti' $moved $kerja) { Check 'duplicate folder refused' ((Status) -match 'already in the list' -and ((Get-Content $ini) -join '|') -eq $before) "status='$(Status)'" }
  Snap 'settings-folders-after' (WindowRect $app)
} else { Check 'settings opens for folder tests' $false "title='$(Title)'" }
$app.Kill(); $app.WaitForExit()

# 14. More than 5 folders: settings list and save dialog scroll instead of growing.
$names = 1..7 | ForEach-Object { "f$_" }
$names | ForEach-Object { New-Item -ItemType Directory (Join-Path $dir $_) -Force | Out-Null }
Set-Content $ini (($names | ForEach-Object { "Dest=1|$_" }) + 'Default=0', 'Ask=1', 'RegionKey=D2, Shift, Control', 'FullKey=D1, Shift, Control')
$app = Start-App
Start-Process $exePath | Out-Null
foreach ($i in 1..20) { if ((Title) -eq 'Donat Screen') { break }; Start-Sleep -Milliseconds 200 }
function ListScrolls { $row = Ui 'f1'; $row -and [In]::HasVScroll([In]::GetParent([IntPtr]$row.Current.NativeWindowHandle)) }
if ((Title) -eq 'Donat Screen') {
  Check 'settings: 7 folders -> list scrolls' (ListScrolls)
  Check 'settings: rows beyond 5 still exist' ($null -ne (Ui 'f7'))
  [In]::PostMessage((AppWindow), 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep -Milliseconds 600   # WM_CLOSE
} else { Check 'settings opens for scroll test' $false }
if (Dialog) {
  Check 'save dialog: 7 folders -> list scrolls' (ListScrolls)
  [In]::Keys(0x1B); Start-Sleep -Milliseconds 500
} else { Check 'save dialog opens for scroll test' $false "title='$(Title)'" }
$app.Kill(); $app.WaitForExit()

# 15. Language: Lang=id shows Indonesian; the English button switches live and is saved.
Set-Content $ini 'Dest=1|shots', 'Ask=0', 'RegionKey=D2, Shift, Control', 'FullKey=D1, Shift, Control', 'Lang=id'
$app = Start-App
Start-Process $exePath | Out-Null
foreach ($i in 1..20) { if ((Title) -eq 'Donat Screen') { break }; Start-Sleep -Milliseconds 200 }
if ((Title) -eq 'Donat Screen') {
  Check 'Lang=id shows Indonesian' ($null -ne (Ui 'Jalankan saat Windows menyala')) ((Names) -join ' | ')
  Snap 'settings-id' (WindowRect $app)
  if (Click 'English') {
    Check 'English button switches the window live' ($null -ne (Ui 'Start with Windows')) ((Names) -join ' | ')
    Check 'language choice saved' ((Get-Content $ini) -contains 'Lang=en') ((Get-Content $ini) -join ' | ')
  } else { Check 'settings window in front for language' $false }
} else { Check 'settings opens for language test' $false "title='$(Title)'" }
$app.Kill(); $app.WaitForExit()

if ($savedRun) { Set-ItemProperty $runKey DonatScreen $savedRun }
if ($fails) { "`n$fails check(s) failed"; exit 1 } else { "`nAll checks passed" }
