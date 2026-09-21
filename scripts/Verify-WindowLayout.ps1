
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TabInput {
 public static uint Target;
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [StructLayout(LayoutKind.Explicit,Size=40)] public struct Input {
  [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(12)] public uint Flags;
  [FieldOffset(20)] public uint MouseFlags;
 }
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 public static void Hover(int x,int y) { SetCursorPos(x,y); System.Threading.Thread.Sleep(250); }
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 public static void Drag(int x,int y,int targetX,int targetY) {
  uint current; GetWindowThreadProcessId(GetForegroundWindow(),out current);
  if(current!=Target) throw new InvalidOperationException("検証対象が前面にありません。");
  if(!SetCursorPos(x,y)) throw new InvalidOperationException("ポインタを移動できません。");
  System.Threading.Thread.Sleep(100);
  if(SendInput(1,new[]{new Input{MouseFlags=2}},40)!=1) throw new InvalidOperationException("入力送信失敗");
  try { System.Threading.Thread.Sleep(150); for(int i=1;i<=20;i++){SetCursorPos(x+(targetX-x)*i/20,y+(targetY-y)*i/20);System.Threading.Thread.Sleep(20);} }
  finally { SendInput(1,new[]{new Input{MouseFlags=4}},40); }
 }
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
 public static void Chord(params ushort[] keys) {
  uint current; GetWindowThreadProcessId(GetForegroundWindow(),out current);
  if(current!=Target) throw new InvalidOperationException("検証対象が前面にありません。");
  var inputs=new Input[keys.Length*2];
  // 左Ctrl+Tabを常駐ツールが置き換える環境でも、アプリの標準割り当てを検証する。
  if (Array.IndexOf(keys,(ushort)9)>=0)
   for(int i=0;i<keys.Length;i++) if(keys[i]==0x11) keys[i]=0xA3;
  for(int i=0;i<keys.Length;i++) {
   var down=keys[i]; var up=keys[keys.Length-i-1];
   inputs[i]=new Input{Type=1,Key=down,Flags=down==0xA3 ? 1u : 0u};
   inputs[keys.Length+i]=new Input{Type=1,Key=up,Flags=2u | (up==0xA3 ? 1u : 0u)};
  }
  if(SendInput((uint)inputs.Length,inputs,40)!=inputs.Length) throw new InvalidOperationException("入力送信に失敗しました。");
 }
}
"@
[TabInput]::SetThreadDpiAwarenessContext([IntPtr](-4)) | Out-Null
$root = Split-Path -Parent $PSScriptRoot
$env:DOTNET_ROOT=Join-Path $root '.tools\dotnet'
$trial = Join-Path $root ('artifacts\window-layout-' + [Guid]::NewGuid().ToString('N'))
$left = Join-Path $trial 'left'
$right = Join-Path $trial 'right'
$a = Join-Path $left '日本語 A'
$b = Join-Path $left 'B'
$c = Join-Path $left 'C'
New-Item -ItemType Directory -Force $a,$b,$c,$right | Out-Null
Set-Content (Join-Path $left 'selection.txt') 'tab selection test'
$scope = [System.Windows.Automation.TreeScope]::Descendants
$all = [System.Windows.Automation.Condition]::TrueCondition
function Wait-Until([scriptblock]$check) {
 $deadline = [DateTime]::UtcNow.AddSeconds(30)
 do { if (& $check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
 throw ("操作結果の待機がタイムアウトしました。" + (Get-PSCallStack | Out-String) + ((Elements | Where-Object { $_.Current.AutomationId -like '*Status' } | ForEach-Object { $_.Current.AutomationId + '=' + $_.Current.Name }) -join ';'))
}
function Elements { if ($null -ne $script:window) { @($script:window.FindAll($scope,$all)) } }
function Find([string]$id) { $script:window.FindFirst($scope, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)) }
function Value([string]$side) { (Find ($side+'Address')).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern) }
function Invoke([string]$id) { (Find $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 150 }
function Press([uint16[]]$keys) {
 if ($CustomKeys) {
  switch ($keys -join ',') {
   '17,9' { $keys = @(0x77) }
   '17,16,9' { $keys = @(0x76) }
   '18,37' { $keys = @(0x78) }
  }
 }
 [TabInput]::Chord($keys); Start-Sleep -Milliseconds 200
}
function At([string]$side,[string]$path) { Wait-Until { (Find ($side+'Status')).Current.Name -eq $path }; Start-Sleep -Milliseconds 150 }
function Go([string]$side,[string]$path) { (Value $side).SetValue($path); Invoke ($side+'.navigateAddress'); At $side $path }
function Tabs([string]$side) { @((Elements) | Where-Object { $_.Current.AutomationId -like ($side+'.tab.*') }) }
function Assert([bool]$value,[string]$message) { if (!$value) { throw $message } }
$oldConfig=$env:EXPLORER_COVER_SHORTCUTS; $oldLog=$env:EXPLORER_COVER_LOG
$settings=Join-Path $trial 'keys.json'; Set-Content $settings '{"version":1,"bindings":{}}' -Encoding utf8
$env:EXPLORER_COVER_SHORTCUTS=$settings; $log=Join-Path $trial 'app.log'; $env:EXPLORER_COVER_LOG=$log
$dll=Join-Path $root 'src\ExplorerCover\bin\Release\net10.0-windows\explorer-cover.dll'
$oldState=$env:EXPLORER_COVER_STATE
$statePath=Join-Path $trial 'workspace.json'; $env:EXPLORER_COVER_STATE=$statePath
$missing='\\wsl.localhost\Ubuntu-24.04\home\explorer-cover-missing-'+[Guid]::NewGuid().ToString('N')
$seed=@{version=1;left=@{paths=@($a,$missing,$b);selectedIndex=2};right=@{paths=@($right);selectedIndex=0};activePane='right';leftPaneRatio=0.6;sidebarWidth=300;bookmarks=@()}
$seed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8
function Start-App {
 $script:app=Start-Process ([IO.Path]::ChangeExtension($dll,'.exe')) -WindowStyle Hidden -PassThru
 $script:window=$null
 Wait-Until {
  if($app.HasExited){throw '起動に失敗'}
  $script:window=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object {$_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer-cover*'} | Select-Object -First 1
  $null -ne $script:window
 }
 [TabInput]::Target=[uint32]$app.Id
 [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
}
function Close-App {
 $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 Assert ($app.WaitForExit(10000)) '終了時の保存が完了しない'
 $script:window=$null
}
function Saved {
 for($attempt=0;$attempt -lt 10;$attempt++) {
  try { return ([IO.File]::ReadAllText($statePath) | ConvertFrom-Json) }
  catch [IO.IOException] { Start-Sleep -Milliseconds 30 }
 }
 throw '状態ファイルを読み取れない'
}
$seed.left=@{paths=@($a);selectedIndex=0}; $seed.right=@{paths=@($right);selectedIndex=0}
$seed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8
Add-Type -AssemblyName System.Windows.Forms
function VisualState { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern) }
function BoundsMatch($expected) {
 $rect=$window.Current.BoundingRectangle
 [Math]::Abs($rect.Left-$expected.left) -lt 3 -and [Math]::Abs($rect.Top-$expected.top) -lt 3 -and [Math]::Abs($rect.Width-$expected.width) -lt 3 -and [Math]::Abs($rect.Height-$expected.height) -lt 3
}
try {
 Start-App; At '左' $a; At '右' $right
 $work=[System.Windows.Forms.Screen]::FromHandle([IntPtr]$window.Current.NativeWindowHandle).WorkingArea
 $transform=$window.GetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern)
 $transform.Resize(1000,650); $transform.Move($work.Left+45,$work.Top+55)
 Wait-Until {(Saved).window.width -eq 1000 -and (Saved).window.height -eq 650}
 $normal=(Saved).window
 Close-App; Start-App; At '左' $a
 Wait-Until {BoundsMatch $normal}
 'PASS: 旧形式の起動と、通常サイズ・位置を再起動で復元'
 (VisualState).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
 Wait-Until {(Saved).window.maximized}
 Assert ((Saved).window.width -eq $normal.width) '最大化サイズを通常サイズとして保存した'
 Close-App; Start-App; At '左' $a
 Wait-Until {(VisualState).Current.WindowVisualState -eq [System.Windows.Automation.WindowVisualState]::Maximized}
 (VisualState).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
 Wait-Until {BoundsMatch $normal}
 'PASS: 最大化を復元し、通常表示へ戻した位置とサイズも保持'
 (VisualState).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
 Close-App; Start-App; At '左' $a
 Assert ((VisualState).Current.WindowVisualState -eq [System.Windows.Automation.WindowVisualState]::Normal) '最小化を復元してしまった'
 Wait-Until {BoundsMatch $normal}
 (VisualState).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
 (VisualState).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
 Close-App; Start-App; At '左' $a
 Assert ((VisualState).Current.WindowVisualState -eq [System.Windows.Automation.WindowVisualState]::Maximized) '最大化から最小化した状態を最大化に戻せない'
 'PASS: 最小化では起動せず、最小化前の通常・最大化を復元'
 Close-App
 $saved=Saved; $saved.window.left=500000; $saved.window.top=500000; $saved.window.width=8000; $saved.window.height=8000; $saved.window.maximized=$false
 $saved | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8
 Start-App; At '左' $a
 $work=[System.Windows.Forms.Screen]::FromHandle([IntPtr]$window.Current.NativeWindowHandle).WorkingArea
 $rect=$window.Current.BoundingRectangle
 Assert ($rect.Left -ge $work.Left-2 -and $rect.Top -ge $work.Top-2 -and $rect.Right -le $work.Right+2 -and $rect.Bottom -le $work.Bottom+2) '画面外の位置・過大サイズを作業領域に収められない'
 'PASS: 画面外の保存位置と過大サイズを現在のモニターへ補正'
 Close-App
 foreach($screen in [System.Windows.Forms.Screen]::AllScreens) {
  $work=$screen.WorkingArea
  $expected=@{left=$work.Left+50;top=$work.Top+50;width=1400;height=800;maximized=$false}
  $saved=Saved; $saved.window=$expected
  $saved | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8
  for($repeat=0;$repeat -lt 4;$repeat++) {
   Start-App
   Wait-Until {BoundsMatch $expected}
   Close-App
   $actual=(Saved).window
   Assert ($actual.left -eq $expected.left -and $actual.top -eq $expected.top -and $actual.width -eq $expected.width -and $actual.height -eq $expected.height) ('起動直後の終了で矩形が変化: '+($actual | ConvertTo-Json -Compress))
  }
  Start-App
  (VisualState).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
  Close-App; Start-App
  Assert ((VisualState).Current.WindowVisualState -eq [System.Windows.Automation.WindowVisualState]::Maximized) '別モニターの最大化を復元できない'
  (VisualState).SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
  Wait-Until {BoundsMatch $expected}
  Close-App
  Write-Output ('PASS: '+$screen.DeviceName+' 起動直後の終了4回で位置・サイズ不変、最大化からも同じ矩形へ復帰')
 }
 Start-App
 Close-App
} finally {
 if($script:window){try{Close-App}catch{Write-Warning $_}}
 $env:EXPLORER_COVER_STATE=$oldState; $env:EXPLORER_COVER_SHORTCUTS=$oldConfig; $env:EXPLORER_COVER_LOG=$oldLog
 Write-Output ('検証記録: '+$trial)
}