param([switch]$CustomKeys, [switch]$Missing)
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
 public static uint ForegroundProcess() { uint pid; GetWindowThreadProcessId(GetForegroundWindow(),out pid); return pid; }
 [StructLayout(LayoutKind.Explicit,Size=40)] public struct Input {
  [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(12)] public uint Flags;
 }
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
 public static void Key(ushort key,bool down) {
  uint current; GetWindowThreadProcessId(GetForegroundWindow(),out current);
  if(down && current!=Target) throw new InvalidOperationException("検証対象が前面にありません。");
  var input=new[]{new Input{Type=1,Key=key,Flags=down?0u:2u}};
  if(SendInput(1,input,40)!=1) throw new InvalidOperationException("入力送信に失敗しました。");
 }
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
$trial = Join-Path $root ('artifacts\quicklook-' + [Guid]::NewGuid().ToString('N'))
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
 [TabInput]::Chord($keys); Start-Sleep -Milliseconds 200
}
function At([string]$side,[string]$path) { Wait-Until { (Find ($side+'Status')).Current.Name -eq $path }; Start-Sleep -Milliseconds 150 }
function Go([string]$side,[string]$path) { (Value $side).SetValue($path); Invoke ($side+'.navigateAddress'); At $side $path }
function Tabs([string]$side) { @((Elements) | Where-Object { $_.Current.AutomationId -like ($side+'.tab.*') }) }
function Assert([bool]$value,[string]$message) { if (!$value) { throw $message } }
$sample = Join-Path $left '日本語 quick view.txt'
Set-Content -LiteralPath $sample 'QuickLook integration sample' -Encoding utf8
$oldConfig=$env:EXPLORER_COVER_SHORTCUTS; $oldLog=$env:EXPLORER_COVER_LOG; $oldQuick=$env:EXPLORER_COVER_QUICKLOOK
$settings=Join-Path $trial 'keys.json'
$json=if($CustomKeys){'{"version":1,"bindings":{"quickView":["F8"]}}'}else{'{"version":1,"bindings":{}}'}
Set-Content $settings $json -Encoding utf8
$env:EXPLORER_COVER_SHORTCUTS=$settings
$quickSettings=Join-Path $trial 'quicklook.json'
$json=if($Missing){ @{version=1;executablePath=(Join-Path $trial 'missing.exe')} | ConvertTo-Json }else{'{"version":1}'}
Set-Content $quickSettings $json -Encoding utf8
$env:EXPLORER_COVER_QUICKLOOK=$quickSettings
$log=Join-Path $trial 'app.log'; $env:EXPLORER_COVER_LOG=$log
$dll=Join-Path $root 'src\ExplorerCover\bin\Release\net10.0-windows\explorer_cover.dll'
$app=Start-Process (Join-Path $root '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"'),('"'+$left+'"'),('"'+$right+'"')) -WindowStyle Hidden -PassThru
$script:window=$null
$previewKey=if($CustomKeys){0x77}else{0x20}
function PreviewWindow {
 @([System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all)) |
  Where-Object { $_.Current.ProcessId -in @(Get-Process QuickLook -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) -and $_.Current.Name -like '*日本語 quick view*' } | Select-Object -First 1
}
function RequestCount { @(Select-String -LiteralPath $log -Pattern 'QuickLook request sent').Count }
function SampleItem { (Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語 quick view.txt' } | Select-Object -First 1 }
try {
 Wait-Until {
  $script:window=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer_cover*' } | Select-Object -First 1
  $null -ne $script:window
 }
 At '左' $left; At '右' $right
 [TabInput]::Target=[uint32]$app.Id
 [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
 (Find '左Address').SetFocus(); Start-Sleep -Milliseconds 250
 Press @(0x1B)
 $item=SampleItem
 $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
 $item.SetFocus(); Start-Sleep -Milliseconds 200
 if($CustomKeys) {
  Press @(0x20); Start-Sleep -Milliseconds 300
  Assert ((RequestCount) -eq 0) 'キー変更後も旧Spaceで呼び出した'
 }
 if(!$Missing) {
  # タブを変えたら押下中の要求を捨てる。
  [TabInput]::Key($previewKey,$true)
  try { Start-Sleep -Milliseconds 100; Press @(0x11,0x54) } finally { [TabInput]::Key($previewKey,$false) }
  Start-Sleep -Milliseconds 200
  Assert ((RequestCount) -eq 0) 'タブ変更後に古い選択をプレビューした'
  Press @(0x11,0x57)
  $item=SampleItem; $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus()
  [TabInput]::Key($previewKey,$true)
  try {
   1..10 | ForEach-Object { Start-Sleep -Milliseconds 100; [TabInput]::Key($previewKey,$true) }
   Assert ((RequestCount) -eq 0) 'キーを離す前に表示した'
  } finally { [TabInput]::Key($previewKey,$false) }
  'PASS: 長押しは離した時だけ実行、押下中のタブ変更は取消'
 } else { Press @($previewKey) }
 if($Missing) {
  Wait-Until { (Find '左Status').Current.Name -like 'QuickLookを開けません:*' }
  Assert ((RequestCount) -eq 0) '無効な起動先で送信した'
  Press @(0x11,0x54)
  Assert ((Tabs '左').Count -eq 2) '失敗後に操作できない'
  'PASS: 起動先が見つからない場合の通知・操作継続'
 } else {
  Wait-Until { $null -ne (PreviewWindow) }
  Start-Sleep -Milliseconds 1000
  Assert ($null -ne (PreviewWindow)) '二重トグルによりプレビューが閉じた'
  Assert ((RequestCount) -eq 1) 'プレビュー要求が重複した'
  'PASS: 日本語・空白パスをQuickLookで表示、二重トグルなし'
  $alternate=Join-Path $left '日本語 quick view 2.txt'
  Set-Content -LiteralPath $alternate 'Another preview content' -Encoding utf8
  Wait-Until { $null -ne ((Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語 quick view 2.txt' } | Select-Object -First 1) }
  $item2=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語 quick view 2.txt' } | Select-Object -First 1
  $item2.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item2.SetFocus()
  Wait-Until { $null -ne (PreviewWindow) -and (PreviewWindow).Current.Name -like '*日本語 quick view 2.txt*' }
  Assert ((RequestCount) -eq 1) '選択変更でToggleを送った'
  Press @(0x28)
  $selected=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected } | Select-Object -First 1
  $selectedName=$selected.Current.Name
  Wait-Until { $null -ne (PreviewWindow) -and (PreviewWindow).Current.Name.Contains($selectedName) }
  $original=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語 quick view.txt' } | Select-Object -First 1
  $original.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $original.SetFocus()
  Wait-Until { $null -ne (PreviewWindow) -and (PreviewWindow).Current.Name -like '*日本語 quick view.txt*' }
  'PASS: 選択変更・矢印キーに追従して表示内容が切り替わる'
  $preview=PreviewWindow
  [TabInput]::Target=[uint32]$preview.Current.ProcessId
  [TabInput]::SetForegroundWindow([IntPtr]$preview.Current.NativeWindowHandle) | Out-Null
  Start-Sleep -Milliseconds 1200
  Press @(0x1B)
  Wait-Until { $null -eq (PreviewWindow) }
  [TabInput]::Target=[uint32]$app.Id
  Start-Sleep -Milliseconds 300
  $item2.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item2.SetFocus()
  Start-Sleep -Milliseconds 600
  Assert ($null -eq (PreviewWindow)) '閉じたプレビューが選択変更で再表示された'
  $original.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $original.SetFocus()
  Start-Sleep -Milliseconds 300
  'PASS: 閉じた後は選択を変えても再表示しない'
  Press @(0x11,0x4C)
  Assert ((Find '左Address').Current.HasKeyboardFocus) '閉じた後に一覧から操作できない'
  'PASS: Escapeで閉じて元のアプリからCtrl+Lで操作継続'
  (Value '左').SetValue('edit'); Press @(0x20)
  Assert ((Value '左').Current.Value -like '* *') 'パス欄でSpace入力ができない'
  Assert ((RequestCount) -eq 1) 'パス入力でQuickLookを呼んだ'
  Press @(0x1B)
  $item=SampleItem; $item.SetFocus(); $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Press @(0x71); Press @(0x20)
  Assert ((RequestCount) -eq 1) '名前変更でQuickLookを呼んだ'
  Press @(0x1B)
  'PASS: パス入力・名前変更中のSpaceを保護'
  Press @(0x11,0x41); Press @($previewKey)
  Assert ((RequestCount) -eq 1) '複数選択で呼び出した'
  (Find '右Address').SetFocus(); Press @(0x1B); Press @($previewKey)
  Assert ((RequestCount) -eq 1) '未選択で呼び出した'
  'PASS: 複数選択・未選択では呼び出さない'
  (Find '左Address').SetFocus(); Press @(0x1B)
  $folder=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語 A' } | Select-Object -First 1
  $folder.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $folder.SetFocus()
  Press @($previewKey)
  Assert ((RequestCount) -eq 1) 'フォルダーで呼び出した'
  'PASS: フォルダーは対象外'
  $item=SampleItem; $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus()
  Press @($previewKey)
  Wait-Until { $null -ne (PreviewWindow) }
  $preview=PreviewWindow
  Start-Sleep -Milliseconds 1200
  $foreground=[TabInput]::ForegroundProcess()
  Assert ($foreground -in @($app.Id,$preview.Current.ProcessId)) '検証対象以外が前面にある'
  [TabInput]::Target=$foreground
  if($foreground -eq $app.Id){Press @($previewKey)}else{Press @(0x20)}
  Wait-Until { $null -eq (PreviewWindow) }
  [TabInput]::Target=[uint32]$app.Id
  Press @(0x11,0x4C)
  'PASS: 再表示後にプレビューキーで閉じて操作継続（前面化操作なし）'
  $countBeforeIme=RequestCount
  powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Verify-Ime.ps1') -ProcessId $app.Id
  if($LASTEXITCODE -ne 0){throw 'パス欄のIME検証が失敗'}
  powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Verify-ImeRenameAndMove.ps1') -ProcessId $app.Id
  if($LASTEXITCODE -ne 0){throw '名前変更のIME検証が失敗'}
  Assert ((RequestCount) -eq $countBeforeIme) 'IME変換中にQuickLookを呼び出した'
  $zip=Join-Path $left '日本語 quick view.zip'
  Compress-Archive -LiteralPath $sample -DestinationPath $zip
  (Find '左Address').SetFocus(); Press @(0x1B); Press @(0x74)
  Wait-Until { $null -ne ((Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語 quick view.zip' } | Select-Object -First 1) }
  $zipItem=(Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq '日本語 quick view.zip' } | Select-Object -First 1
  $zipItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $zipItem.SetFocus()
  Press @($previewKey)
  Wait-Until { $null -ne (PreviewWindow) }
  Assert ((PreviewWindow).Current.Name -like '*.zip*') 'ZIPを表示していない'
  Assert ((RequestCount) -eq ($countBeforeIme+1)) 'ZIPの要求が正しく送信されていない'
  (PreviewWindow).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
  'PASS: フォルダー属性を持つZIPもファイルとしてプレビュー'
 }
} finally {
 $ownPreview=PreviewWindow
 if($ownPreview){try{$ownPreview.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}catch{}}
 if($script:window){try{$script:window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}catch{}}
 if(!$app.WaitForExit(5000)){throw '検証アプリの終了待機に失敗'}
 $env:EXPLORER_COVER_SHORTCUTS=$oldConfig; $env:EXPLORER_COVER_LOG=$oldLog; $env:EXPLORER_COVER_QUICKLOOK=$oldQuick
 Write-Output ('検証ログ: '+$log)
}
