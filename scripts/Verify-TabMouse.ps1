param([ValidateSet("middle","right","xButton1","xButton2","none","invalid")][string]$Profile="middle",[switch]$AdvancedOnly)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class TabInput {
 public static uint Target;
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 public static void Move(int x,int y) {
  uint pid; GetWindowThreadProcessId(GetForegroundWindow(),out pid);
  if(pid!=Target) throw new InvalidOperationException("検証対象が前面にありません。");
  SetCursorPos(x,y);
 }
 public static void Mouse(uint flags,uint data=0) {
  uint pid; GetWindowThreadProcessId(GetForegroundWindow(),out pid);
  if(pid!=Target) throw new InvalidOperationException("検証対象が前面にありません。");
  if(SendInput(1,new[]{new Input{Type=0,MouseFlags=flags,MouseData=data}},40)!=1) throw new InvalidOperationException("マウス入力失敗");
 }
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [StructLayout(LayoutKind.Explicit,Size=40)] public struct Input {
  [FieldOffset(16)] public uint MouseData; [FieldOffset(20)] public uint MouseFlags; [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(12)] public uint Flags;
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
$root = Split-Path -Parent $PSScriptRoot
$trial = Join-Path $root ('artifacts\mouse-' + [Guid]::NewGuid().ToString('N'))
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
 $deadline = [DateTime]::UtcNow.AddSeconds(12)
 do { if (& $check) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
 throw ("操作結果の待機がタイムアウトしました。" + (Get-PSCallStack | Out-String) + ((Elements | Where-Object { $_.Current.AutomationId -like '*Status' } | ForEach-Object { $_.Current.AutomationId + '=' + $_.Current.Name }) -join ';'))
}
function Elements { @($script:window.FindAll($scope,$all)) }
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
$settings = Join-Path $trial 'keys.json'
$json = if ($CustomKeys) { '{"version":1,"bindings":{"nextTab":["F8"],"previousTab":["F7"],"back":["F9"]}}' } else { '{"version":1,"bindings":{}}' }
Set-Content $settings $json -Encoding utf8
$env:EXPLORER_COVER_SHORTCUTS=$settings
$log = Join-Path $trial 'app.log'
$env:EXPLORER_COVER_LOG=$log
$dll=Join-Path $root 'src\ExplorerCover\bin\Release\net10.0-windows\explorer_cover.dll'
$oldMouse=$env:EXPLORER_COVER_MOUSE
$mousePath=Join-Path $trial 'mouse.json'
$button=if($Profile -eq 'invalid') { 'left' } else { $Profile }
Set-Content $mousePath ('{"version":1,"closeTabButton":"'+$button+'"}') -Encoding utf8
$env:EXPLORER_COVER_MOUSE=$mousePath
$app=Start-Process (Join-Path $root '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"'),('"'+$left+'"'),('"'+$right+'"')) -WindowStyle Hidden -PassThru
$script:window=$null
function Point($el) { $r=$el.Current.BoundingRectangle; @([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2)) }
function Click([int[]]$point,[uint32]$down=2,[uint32]$up=4,[uint32]$data=0) {
 [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null; Start-Sleep -Milliseconds 100
 [TabInput]::Move($point[0],$point[1]); [TabInput]::Mouse($down,$data); [TabInput]::Mouse($up,$data); Start-Sleep -Milliseconds 150
}
function Drag([int[]]$from,[int[]]$to,[switch]$Escape) {
 [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null; Start-Sleep -Milliseconds 100
 [TabInput]::Move($from[0],$from[1]); [TabInput]::Mouse(2)
 try {
  1..12 | ForEach-Object { [TabInput]::Move([int]($from[0]+($to[0]-$from[0])*$_/12),[int]($from[1]+($to[1]-$from[1])*$_/12)); Start-Sleep -Milliseconds 30 }
  if($Escape) { Press @(0x1B) }
 } finally { [TabInput]::Mouse(4) }
 Start-Sleep -Milliseconds 250
}
function Order { ((Tabs '左') | ForEach-Object { $_.Current.AutomationId }) -join ',' }
try {
 Wait-Until {
  $script:window=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer_cover*' } | Select-Object -First 1
  $null -ne $script:window
 }
 At '左' $left; At '右' $right
 [TabInput]::Target=[uint32]$app.Id
 [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
 Start-Sleep -Milliseconds 300
 Invoke '左.newTab'; At '左' $left; Go '左' $a
 Invoke '左.newTab'; At '左' $left; Go '左' $b
 if($Profile -eq 'middle' -and !$AdvancedOnly) {
  $before=Tabs '左'
  $ids=@($before | ForEach-Object { $_.Current.AutomationId })
  $end=$before[2].Current.BoundingRectangle
  Drag (Point $before[0]) @(([int]$end.Right-2),[int]($end.Y+$end.Height/2))
  Assert ((Order) -eq ($ids[1],$ids[2],$ids[0] -join ',')) '末尾への並べ替えに失敗'
  At '左' $left
  Press @(0x11,0x09); At '左' $a
  Invoke '左.back'; At '左' $left
  Invoke '左.forward'; At '左' $a
  'PASS: 末尾への並べ替え、Ctrl+Tabの新順序、履歴保持'
  $before=Tabs '左'; $order=Order
  Drag (Point $before[0]) (Point $before[2]) -Escape
  Assert ((Order) -eq $order) 'Escapeで順序が変わった'
  $outside=Point $before[0]; $outside[1]+=85
  Drag (Point $before[0]) $outside
  Assert ((Order) -eq $order) 'バー外ドロップで順序が変わった'
  Assert ((Tabs '左').Count -eq 3) 'ドラッグ終了でタブが増えた'
  $start=$before[0].Current.BoundingRectangle
  Drag (Point $before[2]) @(([int]$start.Left+2),[int]($start.Y+$start.Height/2))
  Assert ((Order) -eq ($ids -join ',')) '先頭への並べ替えに失敗'
  'PASS: 先頭への並べ替え、Escape・バー外ドロップの取消'
  $point=Point ((Tabs '左')[0]); Click $point; Click $point
  Assert ((Tabs '左').Count -eq 3) '見出しのダブルクリックで追加された'
  $plus=(Find '左.newTab').Current.BoundingRectangle
  $blank=@(([int]$plus.Left-18),$point[1])
  Click $blank; Start-Sleep -Milliseconds 600
  Assert ((Tabs '左').Count -eq 3) '空白のシングルクリックで追加された'
  Click $blank; Click $blank
  Wait-Until { (Tabs '左').Count -eq 4 }
  At '左' $left
  Assert ((Tabs '右').Count -eq 1) '別ペインに追加された'
  Invoke '左.closeTab'
  'PASS: 空白ダブルクリックだけで対象ペインに新規タブ'
 }
 (Tabs '左')[-1].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 $current=(Value '左').Current.Value
 $target=(Tabs '左')[0]
 if($Profile -ne 'middle' -and $Profile -ne 'invalid') {
  Click (Point $target) 0x20 0x40
  Assert ((Tabs '左').Count -eq 3) '変更前の中ボタンが残っている'
 }
 $effective=if($Profile -eq 'invalid') { 'middle' } else { $Profile }
 switch($effective) {
  'middle' { Click (Point $target) 0x20 0x40 }
  'right' { Click (Point $target) 8 16 }
  'xButton1' { Click (Point $target) 0x80 0x100 1 }
  'xButton2' { Click (Point $target) 0x80 0x100 2 }
  'none' {
   Click (Point $target) 8 16
   Click (Point $target) 0x80 0x100 1
   Click (Point $target) 0x80 0x100 2
   Assert ((Tabs '左').Count -eq 3) '無効化したクリックで閉じた'
   Invoke '左.closeTab'
   $current=(Value '左').Current.Value
  }
 }
 Wait-Until { (Tabs '左').Count -eq 2 }
 Assert ((Value '左').Current.Value -eq $current) '非選択タブを閉じて表示が切り替わった'
 if($Profile -eq 'invalid') { Assert ([bool]((Elements) | Where-Object { $_.Current.Name -like 'マウス設定を読み込めないため*' })) '不正設定の警告がない' }
 Invoke '左.closeTab'
 $lastPoint=Point ((Tabs '左')[0])
 switch($effective) {
  'middle' { Click $lastPoint 0x20 0x40 }
  'right' { Click $lastPoint 8 16 }
  'xButton1' { Click $lastPoint 0x80 0x100 1 }
  'xButton2' { Click $lastPoint 0x80 0x100 2 }
  'none' { Click $lastPoint 0x20 0x40 }
 }
 Assert ((Tabs '左').Count -eq 1) '最後のタブが閉じた'
 "PASS: $Profile の設定反映・対象タブの終了・最後のタブ保護"
 if($Profile -eq 'middle') {
  # 見出し外で離した中クリックを終了操作にしない。
  Invoke '左.newTab'; At '左' $left
  $point=Point ((Tabs '左')[0])
  [TabInput]::Move($point[0],$point[1]); [TabInput]::Mouse(0x20)
  try { [TabInput]::Move($point[0],$point[1]+80); Start-Sleep -Milliseconds 200 }
  finally { [TabInput]::Mouse(0x40) }
  Assert ((Tabs '左').Count -eq 2) '見出し外で離したクリックで閉じた'
  # フォーカス喪失時は並べ替えを確定しない。
  $order=Order; $from=Point ((Tabs '左')[0]); $to=Point ((Tabs '左')[1])
  [TabInput]::Move($from[0],$from[1]); [TabInput]::Mouse(2)
  $windowPattern=$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
  try {
   Start-Sleep -Milliseconds 150
   [TabInput]::Move($to[0],$to[1]); Start-Sleep -Milliseconds 200
   $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
   Start-Sleep -Milliseconds 250
  } finally {
   $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
   [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
   Start-Sleep -Milliseconds 250
   [TabInput]::Mouse(4)
  }
  Assert ((Order) -eq $order) '非アクティブ化で順序が変わった'
  'PASS: 見出し外での終了取消、非アクティブ化でのドラッグ取消'
  1..7 | ForEach-Object { Invoke '左.newTab'; At '左' $left }
  $last=(Tabs '左')[-1]; $lastId=$last.Current.AutomationId
  Assert (!$last.Current.IsOffscreen) '追加したタブが表示範囲外'
  $from=Point $last
  $edge=@([int]($window.Current.BoundingRectangle.Left+20),$from[1])
  Write-Output ("Edge drag: from="+($from -join ',')+"; to="+($edge -join ',')+"; id="+$lastId)
  [TabInput]::Move($from[0],$from[1]); [TabInput]::Mouse(2)
  try {
   Start-Sleep -Milliseconds 150
   1..12 | ForEach-Object { [TabInput]::Move([int]($from[0]+($edge[0]-$from[0])*$_/12),$from[1]); Start-Sleep -Milliseconds 30 }
   Start-Sleep -Milliseconds 2200
  } finally { [TabInput]::Mouse(4) }
  Wait-Until { (Tabs '左')[0].Current.AutomationId -eq $lastId }
  'PASS: バー端の自動スクロールで末尾から先頭へ移動'
  $long=Join-Path $left 'scroll'
  New-Item -ItemType Directory $long | Out-Null
  1..80 | ForEach-Object { Set-Content (Join-Path $long ('file-{0:D3}.txt' -f $_)) 'scroll test' }
  Go '左' $long
  $list=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and !$_.Current.IsOffscreen } | Select-Object -First 1
  $scroll=$list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
  Wait-Until { $scroll.Current.VerticallyScrollable }
  $scroll.SetScrollPercent(-1,70); Start-Sleep -Milliseconds 200
  $item=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -like 'file-*' -and !$_.Current.IsOffscreen } | Select-Object -First 1
  $selection=$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
  $selection.Select(); $percent=$scroll.Current.VerticalScrollPercent
  $visible=Tabs '左'; $to=Point $visible[1]; $to[0]+=[int]($visible[1].Current.BoundingRectangle.Width/2)-2
  Drag (Point $visible[0]) $to
  Assert ((Tabs '左')[1].Current.AutomationId -eq $lastId) '保持検証の並べ替え失敗'
  Assert ($selection.Current.IsSelected) '並べ替えで一覧の選択が失われた'
  Assert ([Math]::Abs($scroll.Current.VerticalScrollPercent-$percent) -lt 1) '並べ替えでスクロール位置が失われた'
  'PASS: 並べ替え後の一覧の選択・スクロール保持'
 }
} finally {
 if($window) { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
 if(!$app.WaitForExit(10000)) { throw '終了待機タイムアウト' }
 if($app.ExitCode -ne 0) { throw "異常終了: $($app.ExitCode)" }
 $env:EXPLORER_COVER_SHORTCUTS=$oldConfig; $env:EXPLORER_COVER_LOG=$oldLog; $env:EXPLORER_COVER_MOUSE=$oldMouse
}
$lines=Get-Content $log
Assert (@($lines | Where-Object { $_ -like '*Creating ExplorerBrowser*' }).Count -eq @($lines | Where-Object { $_ -like '*ExplorerBrowser released*' }).Count) 'ビューの生成・解放数が不一致'
"検証記録: $trial"
