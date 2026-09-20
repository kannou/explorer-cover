param([switch]$CustomKeys)
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
$trial = Join-Path $root ('artifacts\tabs-' + [Guid]::NewGuid().ToString('N'))
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
$app=Start-Process (Join-Path $root '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"'),('"'+$left+'"'),('"'+$right+'"')) -WindowStyle Hidden -PassThru
$script:window=$null
try {
 Wait-Until {
  $script:window=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer_cover*' } | Select-Object -First 1
  $null -ne $script:window
 }
 At '左' $left; At '右' $right
 $app.Refresh(); Write-Output ('2タブのワーキングセット: {0:N1} MiB' -f ($app.WorkingSet64 / 1MB))
 [TabInput]::Target=[uint32]$app.Id
 [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
 (Find '左Address').SetFocus(); Start-Sleep -Milliseconds 250
 Assert (!(Find '左.back').Current.IsEnabled) '初回の戻るが有効'
 Press @(0x11,0x57)
 Assert ((Tabs '左').Count -eq 1) '最後のタブが閉じられた'
 Go '左' $a; Go '左' $b
 Press @(0x12,0x25); At '左' $a
 Invoke '左.forward'; At '左' $b
 Invoke '左.back'; At '左' $a
 Go '左' $c
 Assert (!(Find '左.forward').Current.IsEnabled) '分岐後に進むが有効'
 (Value '左').SetValue((Join-Path $trial 'missing')); Invoke '左.navigateAddress'
 Wait-Until { (Find '左Status').Current.Name -like '移動できません:*' }
 Go '左' $c # 同じ現在地を再入力してもエラーを解消する。
 Invoke '左.back'; At '左' $a
 'PASS: 戻る・進む、履歴分岐、無効パス後の履歴維持'
 # 削除された履歴先への「戻る」もカーソルを進めない。
 Go '左' $b
 Remove-Item -LiteralPath $a # この検証が作った空フォルダーだけ
 Invoke '左.back'
 Wait-Until { (Find '左Status').Current.Name -like '移動できません:*' }
 New-Item -ItemType Directory $a | Out-Null
 Invoke '左.back'; At '左' $a
 'PASS: 履歴先が消えた場合の失敗・再試行'
 Press @(0x11,0x10,0x54); At '左' $a
 Assert ((Tabs '左').Count -eq 2) '複製されていない'
 Assert (!(Find '左.back').Current.IsEnabled) '複製に別タブの履歴が混入'
 Go '左' $c
 Press @(0x11,0x10,0x09); At '左' $a
 Assert ((Find '左.forward').Current.IsEnabled) '元タブの進む履歴が失われた'
 Press @(0x11,0x09); At '左' $c
 Assert (!(Find '左.forward').Current.IsEnabled) '別タブの履歴が混入'
 Invoke '左.parent'; At '左' $left
 # 一覧の選択を保持したまま切り替える。
 $item=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -like 'selection*' } | Select-Object -First 1
 $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 Press @(0x11,0x09); At '左' $a
 Press @(0x11,0x09); At '左' $left
 $item=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -like 'selection*' -and !$_.Current.IsOffscreen } | Select-Object -First 1
 Assert ($item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) 'タブの選択状態が失われた'
 'PASS: 複製、両方向の切替、独立した履歴、一覧の選択保持'
 Press @(0x11,0x54); At '左' $left
 Assert ((Tabs '左').Count -eq 3) '新規タブ未追加'
 Press @(0x75); Press @(0x11,0x54); At '右' $right
 Assert ((Tabs '右').Count -eq 2) '右タブ未追加'
 Assert ((Tabs '左').Count -eq 3) '別ペインのタブ数が変化'
 $app.Refresh(); Write-Output ('5タブのワーキングセット: {0:N1} MiB' -f ($app.WorkingSet64 / 1MB))
 Go '右' $c
 Press @(0x12,0x26); At '右' $left
 Press @(0x11,0x57); At '右' $right
 Assert ((Tabs '右').Count -eq 1) '右タブ未削除'
 (Find '左Address').SetFocus(); Start-Sleep -Milliseconds 250
 Press @(0x11,0x57); At '左' $left
 Press @(0x11,0x57); At '左' $a
 Assert ((Tabs '左').Count -eq 1) '左タブ未削除'
 Go '左' ([IO.Path]::GetPathRoot($trial))
 Assert (!(Find '左.parent').Current.IsEnabled) 'ルートの親ボタンが有効'
 Assert (!(Find '左.closeTab').Current.IsEnabled) '最後のタブを閉じるボタンが有効'
 'PASS: 左右独立の追加・閉じる、Alt+Up、ルートと最後のタブの保護'
 Invoke '左.newTab'; At '左' $left
 $first=(Tabs '左')[0]
 $first.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 At '左' ([IO.Path]::GetPathRoot($trial))
 Invoke '左.closeTab'; At '左' $left
 'PASS: タブ見出しで切替・ボタンで閉じる'
 $long=Join-Path $left 'scroll'
 New-Item -ItemType Directory $long | Out-Null
 1..80 | ForEach-Object { Set-Content (Join-Path $long ('file-{0:D3}.txt' -f $_)) 'scroll test' }
 Go '左' $long
 $list=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and !$_.Current.IsOffscreen } | Select-Object -First 1
 $scroll=$list.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
 Wait-Until { $scroll.Current.VerticallyScrollable }
 $scroll.SetScrollPercent(-1,70)
 $percent=$scroll.Current.VerticalScrollPercent
 Invoke '左.newTab'; At '左' $left
 Press @(0x11,0x10,0x09); At '左' $long
 Assert ([Math]::Abs($scroll.Current.VerticalScrollPercent-$percent) -lt 1) 'スクロール位置が失われた'
 Invoke '左.closeTab'; At '左' $left
 'PASS: 一覧のスクロール位置をタブ切替後も保持'
 if ($CustomKeys) {
  Add-Type -AssemblyName System.Drawing
  $bounds=$window.Current.BoundingRectangle
  $bitmap=New-Object System.Drawing.Bitmap ([int]$bounds.Width),([int]$bounds.Height)
  $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
  try {
   $graphics.CopyFromScreen([int]$bounds.X,[int]$bounds.Y,0,0,$bitmap.Size)
   $bitmap.Save((Join-Path $trial 'window.png'))
  } finally { $graphics.Dispose(); $bitmap.Dispose() }
 }
 if (!$CustomKeys) {
  & (Join-Path $PSScriptRoot 'Verify-Ime.ps1') -ProcessId $app.Id
  & (Join-Path $PSScriptRoot 'Verify-ImeRenameAndMove.ps1') -ProcessId $app.Id
 }
} finally {
 if ($window) { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
 if (!$app.WaitForExit(10000)) { throw '終了待機タイムアウト' }
 if ($app.ExitCode -ne 0) { throw "異常終了: $($app.ExitCode)" }
 $env:EXPLORER_COVER_SHORTCUTS=$oldConfig; $env:EXPLORER_COVER_LOG=$oldLog
}
$lines=Get-Content $log
Assert (@($lines | Where-Object { $_ -like '*Creating ExplorerBrowser*' }).Count -eq @($lines | Where-Object { $_ -like '*ExplorerBrowser released*' }).Count) 'ビューの生成・解放数が不一致'
'PASS: 閉じたタブと終了時のビュー解放'
"検証記録: $trial"
