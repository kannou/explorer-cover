
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
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 public static void Click(int x,int y,uint down,uint up) {
  uint current; GetWindowThreadProcessId(GetForegroundWindow(),out current);
  if(current!=Target) throw new InvalidOperationException("検証対象が前面にありません。");
  SetCursorPos(x,y); SendInput(1,new[]{new Input{MouseFlags=down}},40);
  System.Threading.Thread.Sleep(80); SendInput(1,new[]{new Input{MouseFlags=up}},40);
 }
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
[TabInput]::SetProcessDPIAware() | Out-Null
$root = Split-Path -Parent $PSScriptRoot
$trial = Join-Path $root ('artifacts\settings-' + [Guid]::NewGuid().ToString('N'))
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
$seed=@{version=1;left=@{paths=@($left);selectedIndex=0};right=@{paths=@($right);selectedIndex=0};activePane='right';leftPaneRatio=0.6;sidebarWidth=300;bookmarks=@()}
$seed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8
function Start-App {
 $script:app=Start-Process (Join-Path $root '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"')) -WindowStyle Hidden -PassThru
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
function Saved { Get-Content -LiteralPath $statePath -Raw -Encoding utf8 | ConvertFrom-Json }
$oldInput=$env:EXPLORER_COVER_SETTINGS; $inputPath=Join-Path $trial 'input.json'; $env:EXPLORER_COVER_SETTINGS=$inputPath
$seed | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $statePath -Encoding utf8
Add-Type -AssemblyName System.Windows.Forms
function Dialog { $window.FindAll($scope,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Window)) | Where-Object {$_.Current.Name -eq '設定'} | Select-Object -First 1 }
function Setting([string]$id) { (Dialog).FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'Settings.'+$id)) }
function Set-Key([string]$id,[string]$key) { (Setting ('Key.'+$id)).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($key) }
function Click-Setting([string]$id) { (Setting $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 200 }
function Open-Settings { Invoke 'OpenSettings'; Wait-Until {$null -ne (Dialog)} }
function Lists { @((Elements) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and !$_.Current.IsOffscreen} | Sort-Object {$_.Current.BoundingRectangle.X}) }
function Item([int]$side,[string]$name) { (Lists)[$side].FindAll($scope,$all) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq $name} | Select-Object -First 1 }
function Select-Item([int]$side,[string]$name) { Wait-Until {$null -ne (Item $side $name)}; $item=Item $side $name; $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus(); Start-Sleep -Milliseconds 150 }
try {
 Start-App; At '左' $left; At '右' $right
 Assert ((Find 'OpenSettings').Current.BoundingRectangle.X -lt ($window.Current.BoundingRectangle.X+80)) '設定ボタンが左下にない'
 Open-Settings
 Add-Type -AssemblyName System.Drawing
 $dlg=Dialog; $rect=$dlg.Current.BoundingRectangle
 $bitmap=[Drawing.Bitmap]::new([int]$rect.Width,[int]$rect.Height); $graphics=[Drawing.Graphics]::FromImage($bitmap); $dc=$graphics.GetHdc()
 try { [TabInput]::PrintWindow([IntPtr]$dlg.Current.NativeWindowHandle,$dc,2) | Out-Null } finally { $graphics.ReleaseHdc($dc) }
 $bitmap.Save((Join-Path $trial 'settings.png')); $graphics.Dispose(); $bitmap.Dispose()
 (Setting 'QuickLookPath').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('relative.exe')
 Assert (!(Setting 'Save').Current.IsEnabled) '相対パスを受け入れた'
 (Setting 'QuickLookPath').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('')
 Set-Key 'newTab' 'Ctrl+W'; Assert (!(Setting 'Save').Current.IsEnabled) '競合設定を保存できてしまう'
 Set-Key 'newTab' 'A'; Assert (!(Setting 'Save').Current.IsEnabled) '文字入力を奪うキーを許可した'
 Set-Key 'newTab' 'F8'
 (Setting 'Key.newTab').SetFocus(); Press @(0x11,0x54)
 Assert ((Tabs '左').Count -eq 1) '設定編集中に背後のタブを追加した'
 Click-Setting 'Cancel'; Assert (!(Test-Path $inputPath)) 'キャンセルで保存した'
 'PASS: 競合・入力保護とキャンセル、設定編集中は背後の操作を実行しない'
 Open-Settings
 Set-Key 'newTab' 'F8'; Set-Key 'copy' 'F7'; Set-Key 'paste' 'F9'; Set-Key 'cut' 'F11'; Set-Key 'rename' 'F12'; Set-Key 'delete' 'Ctrl+D'
 Set-Key 'back' '['; Set-Key 'forward' 'Ctrl+]'
 (Setting 'QuickLookPath').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('C:\Apps\QuickLook.exe')
 $combo=Setting 'CloseTabButton'
 $combo.SetFocus(); Press @(0x24); Press @(0x28)
 Click-Setting 'Save'; Wait-Until {$null -eq (Dialog)}
 Assert ((Get-Content $inputPath -Raw | ConvertFrom-Json).mouse.closeTabButton -eq 'right') 'マウス設定が保存されない'
 Go '左' $a; Go '左' $b
 (Find '左Address').SetFocus(); Press @(0x1B); Press @(0xDB); At '左' $a
 (Find '左Address').SetFocus(); Press @(0xDB)
 Assert ((Find '左Status').Current.Name -eq $a) '括弧の文字入力で移動した'
 Press @(0x1B); Press @(0x11,0xDD); At '左' $b
 Go '左' $left
 'PASS: 単独 [ と Ctrl+] で履歴移動、パス編集中は単独キーを保護'
 (Find '左Address').SetFocus(); Press @(0x1B); Press @(0x11,0x54)
 Assert ((Tabs '左').Count -eq 1) '旧キーでタブが増えた'
 Press @(0x77); Wait-Until {(Tabs '左').Count -eq 2}; At '左' $left
 $tab=(Tabs '左')[-1].Current.BoundingRectangle
 [TabInput]::Click([int]($tab.X+$tab.Width/2),[int]($tab.Y+$tab.Height/2),32,64)
 Assert ((Tabs '左').Count -eq 2) '旧中ボタンで閉じた'
 [TabInput]::Click([int]($tab.X+$tab.Width/2),[int]($tab.Y+$tab.Height/2),8,16)
 Wait-Until {(Tabs '左').Count -eq 1}
 Open-Settings; Set-Key 'newTab' 'F11'; Set-Key 'cut' 'Ctrl+X'
 $before=Get-Content $inputPath -Raw
 $locked=[IO.FileStream]::new($inputPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
 try {
  Click-Setting 'Save'; Wait-Until {(Setting 'Save').Current.IsEnabled}
  Assert ($null -ne (Dialog)) '失敗時に画面が閉じた'
  Assert ((Get-Content $inputPath -Raw) -eq $before) '失敗時にファイルを変更した'
  Assert ($null -ne ((Dialog).FindAll($scope,$all) | Where-Object {$_.Current.Name -like '設定を保存できません*'})) '保存エラーが表示されない'
 } finally { $locked.Dispose() }
 Click-Setting 'Cancel'
 (Find '左Address').SetFocus(); Press @(0x1B); Press @(0x77); Wait-Until {(Tabs '左').Count -eq 2}
 'PASS: キー・マウスの即時変更、旧ボタン解除、保存失敗時は旧設定と原本を保持'
 Select-Item 0 'selection.txt'; Press @(0x76)
 (Find '右Address').SetFocus(); Press @(0x1B); Press @(0x11,0x56); Start-Sleep -Milliseconds 300
 Assert (!(Test-Path (Join-Path $right 'selection.txt'))) '旧貼り付けキーが有効'
 Press @(0x78); Wait-Until {Test-Path (Join-Path $right 'selection.txt')}
 Select-Item 1 'selection.txt'; Press @(0x7B); [System.Windows.Forms.SendKeys]::SendWait('^arenamed.txt{ENTER}')
 Wait-Until {Test-Path (Join-Path $right 'renamed.txt')}
 Select-Item 1 'renamed.txt'; Press @(0x7A)
 (Find '左Address').SetFocus(); Press @(0x1B); Press @(0x78)
 Wait-Until {(Test-Path (Join-Path $left 'renamed.txt')) -and !(Test-Path (Join-Path $right 'renamed.txt'))}
 'PASS: 変更したキーでShellのコピー・貼り付け・名前変更・切り取りを実行'
 Select-Item 0 'renamed.txt'; Press @(0x11,0x44)
 Start-Sleep -Milliseconds 300
 $confirm=(Elements) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Window -and $_.Current.Name -eq 'ファイルの削除'} | Select-Object -First 1
 if($confirm) {
  Assert ($null -ne ($confirm.FindAll($scope,$all) | Where-Object {$_.Current.Name -eq 'renamed.txt'})) '検証用ファイル以外の削除確認'
  $yes=$confirm.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'6'))
  $yes.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 }
 Wait-Until {!(Test-Path (Join-Path $left 'renamed.txt'))}
 'PASS: 変更したキーでShellの削除を実行'
 Close-App; Start-App; At '左' $left
 Open-Settings
 Assert ((Setting 'Key.newTab').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'F8') '再起動でキーを失った'
 Assert ((Setting 'QuickLookPath').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'C:\Apps\QuickLook.exe') '再起動でQuickLook起動先を失った'
 Click-Setting 'Reset'
 Assert ((Setting 'Key.newTab').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq 'Ctrl+T') '初期値へ戻らない'
 Set-Key 'quickView' ''; Click-Setting 'Save'; Wait-Until {$null -eq (Dialog)}
 Assert (@((Get-Content $inputPath -Raw | ConvertFrom-Json).shortcuts.bindings.quickView).Count -eq 0) '割当解除が保存されない'
 'PASS: 再起動で設定保持、初期値復帰、割当解除'
 Close-App
} catch { Write-Output $_.ScriptStackTrace; throw } finally {
 if($script:window){try{if(Dialog){Click-Setting 'Cancel'};Close-App}catch{Write-Warning $_}}
 $env:EXPLORER_COVER_SETTINGS=$oldInput; $env:EXPLORER_COVER_STATE=$oldState; $env:EXPLORER_COVER_SHORTCUTS=$oldConfig; $env:EXPLORER_COVER_LOG=$oldLog
 Write-Output ('検証記録: '+$trial)
}
