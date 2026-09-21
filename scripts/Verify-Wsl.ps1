param([string]$Distro = "Ubuntu-24.04")

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
$trial = Join-Path $root ('artifacts\wsl-' + [Guid]::NewGuid().ToString('N'))
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
function Invoke([string]$id) { Wait-Until { (Find $id).Current.IsEnabled }; (Find $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep -Milliseconds 150 }
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
function At([string]$side,[string]$path) { Wait-Until { (Find ($side+'Status')).Current.Name.TrimEnd('\') -ceq $path.TrimEnd('\') }; Start-Sleep -Milliseconds 150 }
function Go([string]$side,[string]$path) { (Value $side).SetValue($path); Invoke ($side+'.navigateAddress'); At $side $path }
function Tabs([string]$side) { @((Elements) | Where-Object { $_.Current.AutomationId -like ($side+'.tab.*') }) }
function Assert([bool]$value,[string]$message) { if (!$value) { throw $message } }
$oldConfig=$env:EXPLORER_COVER_SHORTCUTS; $oldLog=$env:EXPLORER_COVER_LOG
$settings=Join-Path $trial 'keys.json'; Set-Content $settings '{"version":1,"bindings":{}}' -Encoding utf8
$env:EXPLORER_COVER_SHORTCUTS=$settings; $log=Join-Path $trial 'app.log'; $env:EXPLORER_COVER_LOG=$log
$dll=Join-Path $root 'src\ExplorerCover\bin\Release\net10.0-windows\explorer-cover.dll'
$app=Start-Process (Join-Path $root '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"'),('"'+$left+'"'),('"'+$right+'"')) -WindowStyle Hidden -PassThru
$script:window=$null
function Bookmark([string]$name) { (Elements) | Where-Object { $_.Current.AutomationId -like 'Sidebar.Bookmark.*' -and $_.Current.Name -ceq $name } | Select-Object -First 1 }
function Menu([string]$name) { foreach($top in [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all)){ if($top.Current.ProcessId -eq $app.Id){$found=$top.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)); if($found){return $found}} } }
function Capture([string]$name) {
 Add-Type -AssemblyName System.Drawing
 $bounds=$window.Current.BoundingRectangle
 $bitmap=New-Object System.Drawing.Bitmap ([int]$bounds.Width),([int]$bounds.Height)
 $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
 try {
  $hdc=$graphics.GetHdc()
  try { $captured=[TabInput]::PrintWindow([IntPtr]$window.Current.NativeWindowHandle,$hdc,2) } finally { $graphics.ReleaseHdc($hdc) }
  if(!$captured){throw '画面の取得に失敗'}
  $bitmap.Save((Join-Path $trial $name))
 } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
try {
 Wait-Until {
  if($app.HasExited){throw '検証アプリが終了した'}
  $script:window=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer-cover*' } | Select-Object -First 1
  $null -ne $script:window
 }
 At '左' $left; At '右' $right
 [TabInput]::Target=[uint32]$app.Id
 [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
 $linux=(& wsl -d $Distro -- mktemp -d /tmp/explorer-cover-wsl-XXXXXXXX).Trim()
 if($LASTEXITCODE -ne 0 -or $linux -notmatch '^/tmp/explorer-cover-wsl-[a-zA-Z0-9]+$'){throw 'WSL試験フォルダー作成失敗'}
 & wsl -d $Distro -- mkdir -p "$linux/Case" "$linux/case" "$linux/日本語 空白" "$linux/denied"
 & wsl -d $Distro -- ln -s Case "$linux/link"
 & wsl -d $Distro -- chmod 000 "$linux/denied"
 $wslRoot='\\wsl.localhost\'+$Distro+$linux.Replace('/','\')
 Set-Content (Join-Path $trial 'wsl-root.txt') $wslRoot
 $upper=$wslRoot+'\Case'; $lower=$wslRoot+'\case'
 Go '左' $upper; Invoke 'Sidebar.AddBookmark'
 Go '左' $lower; Invoke 'Sidebar.AddBookmark'
 Assert ($null -ne (Bookmark 'Case')) 'Caseブックマークなし'
 Assert ($null -ne (Bookmark 'case')) 'caseブックマークなし'
 Assert (@((Elements) | Where-Object { $_.Current.AutomationId -like 'Sidebar.Bookmark.*' -and $_.Current.Name -cin @('Case','case') }).Count -eq 2) '大小文字を同一視した'
 Invoke '左.back'; At '左' $upper
 Invoke '左.forward'; At '左' $lower
 Invoke '左.duplicateTab'; At '左' $lower
 Assert ((Tabs '左').Count -eq 2) 'WSLタブ複製失敗'
 Invoke '左.parent'; At '左' $wslRoot
 Go '左' ($wslRoot+'\日本語 空白')
 Go '左' ($wslRoot.Replace('wsl.localhost','wsl$')+'\Case')
 Write-Output ('Shellが返した別名パス: '+(Find '左Status').Current.Name)
 Go '左' ('\\wsl.localhost\'+$Distro+'\')
 Assert (!(Find '左.parent').Current.IsEnabled) 'ディストリビューションルートの親が有効'
 (Value '左').SetValue($wslRoot+'\link'); Invoke '左.navigateAddress'
 Wait-Until { (Find '左Status').Current.Name -like '移動できません:*' }
 Write-Output ('リンク移動後: '+(Find '左Status').Current.Name)
 Go '左' $wslRoot
 (Value '左').SetValue($wslRoot+'\missing'); Invoke '左.navigateAddress'
 Wait-Until {(Find '左Status').Current.Name -like '移動できません:*'}
 Assert ((Value '右').Current.Value -eq $right) '他ペインを変更した'
 Go '左' $wslRoot
 (Value '左').SetValue($wslRoot+'\denied'); Invoke '左.navigateAddress'
 Wait-Until { (Find '左Status').Current.Name -like '移動できません:*' }
 Write-Output ('権限不足: '+(Find '左Status').Current.Name)
 Go '左' $wslRoot
 'PASS: WSL大小文字・履歴・ブックマーク・タブ複製・親・別名・日本語・リンク・存在しない場所'
 # WSL側のコピー元だけを用意し、ファイル操作はシェルで行う。
 Set-Content -LiteralPath ($wslRoot+'\preview.txt') 'WSL preview test'
 Go '左' $upper; Go '左' $wslRoot
 Wait-Until { $null -ne ((Elements) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -like 'preview*'} | Select-Object -First 1) }
 $file=(Elements) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -like 'preview*'} | Select-Object -First 1
 $file.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $file.SetFocus()
 Press @(0x5D); Wait-Until {$null -ne (Menu 'コピー(C)')}; Press @(0x1B)
 Press @(0x11,0x43)
 (Find '右Address').SetFocus(); Press @(0x1B); Press @(0x11,0x56)
 Wait-Until {Test-Path -LiteralPath ($right+'\preview.txt')}
 'PASS: WSLの右クリックとWindows側へのコピー・貼り付け'
 function ConfirmTestCopy {
  # このスクリプトが作ったテキストのコピーに対するWindows標準確認だけを処理する。
  $until=[DateTime]::UtcNow.AddSeconds(3)
  do {
   $texts=@((Elements) | Where-Object {$_.Current.Name -eq 'これらのファイルは、コンピューターに害を及ぼす可能性があります'})
   if($texts.Count -gt 0){$ok=(Elements) | Where-Object {$_.Current.Name -eq 'OK' -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button} | Select-Object -First 1; $ok.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return}
   Start-Sleep -Milliseconds 100
  } while([DateTime]::UtcNow -lt $until)
 }
 function Lists { @((Elements) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and !$_.Current.IsOffscreen } | Sort-Object {$_.Current.BoundingRectangle.X}) }
 function Item([int]$side,[string]$name) { (Lists)[$side].FindAll($scope,$all) | Where-Object {$_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -ceq $name} | Select-Object -First 1 }
 $file=Item 0 'preview.txt'; $file.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $file.SetFocus(); Press @(0x71)
 Add-Type -AssemblyName System.Windows.Forms
 [System.Windows.Forms.SendKeys]::SendWait('^awsl-renamed.txt{ENTER}')
 Wait-Until {Test-Path -LiteralPath ($wslRoot+'\wsl-renamed.txt')}
 'PASS: WSLファイルをShellで名前変更'
 Wait-Until {$null -ne (Item 0 'wsl-renamed.txt')}
 $file=Item 0 'wsl-renamed.txt'; $file.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $file.SetFocus(); Press @(0x20)
 function Preview { [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object {$_.Current.ProcessId -in @(Get-Process QuickLook -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id) -and $_.Current.Name -like '*wsl-renamed*'} | Select-Object -First 1 }
 Wait-Until {$null -ne (Preview)}
 $preview=Preview; [TabInput]::Target=[uint32]$preview.Current.ProcessId; [TabInput]::SetForegroundWindow([IntPtr]$preview.Current.NativeWindowHandle) | Out-Null; Press @(0x1B)
 [TabInput]::Target=[uint32]$app.Id; [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
 'PASS: WSLファイルをQuickLookで表示・閉じる'
 $point=(Item 0 'wsl-renamed.txt').GetClickablePoint(); $rect=(Lists)[1].Current.BoundingRectangle
 [TabInput]::Drag([int]$point.X,[int]$point.Y,[int]($rect.X+$rect.Width/2),[int]($rect.Bottom-50))
 ConfirmTestCopy
 Wait-Until {Test-Path -LiteralPath ($right+'\wsl-renamed.txt')}
 Assert (Test-Path -LiteralPath ($wslRoot+'\wsl-renamed.txt')) 'WSLからWindowsの通常ドラッグがコピーではなかった'
 $point=(Item 1 'preview.txt').GetClickablePoint(); $rect=(Lists)[0].Current.BoundingRectangle
 [TabInput]::Drag([int]$point.X,[int]$point.Y,[int]($rect.X+$rect.Width/2),[int]($rect.Bottom-50))
 ConfirmTestCopy
 Wait-Until {Test-Path -LiteralPath ($wslRoot+'\preview.txt')}
 'PASS: WSLとWindowsの両方向ドラッグでコピー'
 (Find '右Address').SetFocus(); $bookmark=Bookmark 'Case'; $bookmark.SetFocus(); $bookmark.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); At '右' $upper
 & wsl -d $Distro -- rmdir "$linux/Case"
 $bookmark.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Wait-Until {(Find '右Status').Current.Name -like '移動できません:*'}
 Assert ($null -ne (Bookmark 'Case')) '到達不能なブックマークを消した'
 & wsl -d $Distro -- mkdir "$linux/Case"
 Go '右' $right
 'PASS: ブックマークを直前の右タブで開き、到達不能時も登録と履歴を保持'
 Capture 'wsl.png'
} finally {
 if($linux -match '^/tmp/explorer-cover-wsl-[a-zA-Z0-9]+$'){ & wsl -d $Distro -- chmod 700 "$linux/denied" }
 if($script:window){try{$script:window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}catch{}}
 if(!$app.HasExited){$app.CloseMainWindow() | Out-Null}
 if(!$app.WaitForExit(5000)){Write-Warning ('検証アプリの終了待機に失敗: '+$app.Id)}
 $env:EXPLORER_COVER_SHORTCUTS=$oldConfig; $env:EXPLORER_COVER_LOG=$oldLog
 Write-Output ('検証ログ: '+$log)
}