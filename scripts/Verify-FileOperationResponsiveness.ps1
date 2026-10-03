param([ValidateSet('Start','Restart','CtrlPaste','MenuPaste','TabDrop','Inspect','Close')][string]$Step = 'Inspect', [int]$SizeMiB = 1024, [switch]$CloseDuringCopy)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class TransferInput {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
 [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,UIntPtr wp,IntPtr lp,uint flags,uint timeout,out UIntPtr result);
 public static void Check(int pid) { uint current; GetWindowThreadProcessId(GetForegroundWindow(),out current); if(current!=pid) throw new InvalidOperationException("検証対象が前面にありません。"); }
 public static bool Responds(IntPtr hwnd) { UIntPtr result; return SendMessageTimeout(hwnd,0,UIntPtr.Zero,IntPtr.Zero,2,300,out result)!=IntPtr.Zero && IsWindowEnabled(hwnd); }
 public static void Chord(int pid, params byte[] keys) { Check(pid); foreach(var k in keys) keybd_event(k,0,0,UIntPtr.Zero); for(int i=keys.Length-1;i>=0;i--) keybd_event(keys[i],0,2,UIntPtr.Zero); }
 public static void Click(int pid,int x,int y,bool right) { Check(pid); SetCursorPos(x,y); mouse_event(right?8u:2u,0,0,0,UIntPtr.Zero); mouse_event(right?16u:4u,0,0,0,UIntPtr.Zero); }
 public static void Drag(int pid,int x1,int y1,int x2,int y2) {
  Check(pid); SetCursorPos(x1,y1); keybd_event(0x11,0,0,UIntPtr.Zero); mouse_event(2,0,0,0,UIntPtr.Zero);
  try { for(int i=1;i<=25;i++) { SetCursorPos(x1+(x2-x1)*i/25,y1+(y2-y1)*i/25); System.Threading.Thread.Sleep(20); } System.Threading.Thread.Sleep(100); }
  finally { mouse_event(4,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(200); keybd_event(0x11,0,2,UIntPtr.Zero); }
 }
 public static bool Ready(string path,long size) { try { using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.None)) return f.Length==size; } catch(IOException) { return false; } }
}
'@
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$record = Join-Path $workspace 'artifacts\file-operation-session.json'
if ($Step -in @('Start','Restart')) {
 if ($Step -eq 'Start') {
 $trial = Join-Path $workspace ('artifacts\file-operations-' + [Guid]::NewGuid().ToString('N'))
 $left = Join-Path $trial 'left'; $right = Join-Path $trial 'right'
 New-Item -ItemType Directory -Path $left,$right | Out-Null
 $file = Join-Path $left 'large-source.bin'
 $buffer = [byte[]]::new(1MB); [Random]::new(42).NextBytes($buffer)
 $stream = [IO.File]::OpenWrite($file)
 try { for($i=0;$i -lt $SizeMiB;$i++){ $stream.Write($buffer,0,$buffer.Length) } } finally { $stream.Dispose() }
 Set-Content -LiteralPath (Join-Path $left 'keep.txt') -Value 'コピー検証専用'
 } else {
  $oldSession = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
  $trial = [IO.Path]::GetFullPath($oldSession.trial)
  if(!$trial.StartsWith((Join-Path $workspace 'artifacts\file-operations-'),[StringComparison]::OrdinalIgnoreCase)){ throw '専用の検証フォルダーではありません。' }
  if(Get-Process -Id $oldSession.pid -ErrorAction SilentlyContinue){ throw '前の検証アプリが動いています。' }
  $left=$oldSession.left; $right=$oldSession.right; $file=$oldSession.file; $SizeMiB=[int]($oldSession.size/1MB)
 }
 $env:EXPLORER_COVER_LOG = Join-Path $trial 'app.log'
 $env:EXPLORER_COVER_SETTINGS = Join-Path $trial 'input.json'
 Set-Content -LiteralPath $env:EXPLORER_COVER_SETTINGS -Value '{"version":1,"shortcuts":{"version":1,"bindings":{}},"mouse":{"version":1}}'
 $env:EXPLORER_COVER_STATE = Join-Path $trial 'workspace.json'
 $dll = Join-Path $workspace 'src\ExplorerCover\bin\Release\net10.0-windows\explorer-cover.dll'
 $app = Start-Process -FilePath (Join-Path $workspace '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"'),('"'+$left+'"'),('"'+$right+'"')) -WindowStyle Normal -PassThru
 @{ pid=$app.Id; trial=$trial; left=$left; right=$right; file=$file; size=([long]$SizeMiB*1MB) } | ConvertTo-Json | Set-Content -LiteralPath $record
 Start-Sleep -Seconds 3
}
$session = Get-Content -LiteralPath $record -Raw | ConvertFrom-Json
$trialPath = [IO.Path]::GetFullPath($session.trial)
if (!$trialPath.StartsWith((Join-Path $workspace 'artifacts\file-operations-'),[StringComparison]::OrdinalIgnoreCase)) { throw '専用の検証フォルダーではありません。' }
$targetProcess = Get-CimInstance Win32_Process -Filter ('ProcessId='+$session.pid)
if(!$targetProcess -or $targetProcess.Name -ne 'dotnet.exe' -or !$targetProcess.CommandLine.Contains($session.left) -or !$targetProcess.CommandLine.Contains($session.right)) { throw 'この検証で起動したプロセスではありません。' }
$scope = [System.Windows.Automation.TreeScope]::Descendants
$all = [System.Windows.Automation.Condition]::TrueCondition
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$window = $null
$startDeadline = [DateTime]::UtcNow.AddSeconds(30)
do {
 $window = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $session.pid -and $_.Current.Name -like 'explorer-cover*' } | Select-Object -First 1
 if($window){ break }; Start-Sleep -Milliseconds 100
} while($Step -in @('Start','Restart') -and [DateTime]::UtcNow -lt $startDeadline)
if (!$window) { throw '専用の検証ウィンドウが見つかりません。' }
$hwnd = [IntPtr]$window.Current.NativeWindowHandle
function Find([string]$id) { $window.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)) }
function Elements { @($window.FindAll($scope,$all)) }
function Assert([bool]$value,[string]$message) { if(!$value){ throw $message } }
function Until([scriptblock]$check) {
 $deadline = [DateTime]::UtcNow.AddSeconds(45)
 do { if (& $check) { return }; Start-Sleep -Milliseconds 100 } while([DateTime]::UtcNow -lt $deadline)
 throw '検証の待機がタイムアウトしました。'
}
if ($Step -in @('Start','Restart','CtrlPaste','MenuPaste','TabDrop')) {
 [TransferInput]::SetForegroundWindow($hwnd) | Out-Null
 Until { (Find '左Status').Current.Name -eq $session.left -and (Find '右Status').Current.Name -eq $session.right }
}
if ($Step -in @('CtrlPaste','MenuPaste','TabDrop')) {
 Until { [TransferInput]::Ready($session.file,$session.size) }
 Assert ((Find '左Address').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -eq $session.left) 'コピー元の表示先が専用フォルダーと一致しません。'
 $items = Elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and $_.Current.Name -eq 'large-source.bin' }
 $item = $items | Select-Object -First 1
 if(!$item){ throw '検証用の大容量ファイルが一覧にありません。' }
 $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); $item.SetFocus()
 $destination = if($Step -eq 'TabDrop'){ $session.right } else { $session.left }
 $before = @(Get-ChildItem -LiteralPath $destination -File | ForEach-Object FullName)
 if($Step -ne 'TabDrop'){ [TransferInput]::Chord($session.pid, @(0x11,0x43)); Start-Sleep -Milliseconds 150 }
 $watch = [Diagnostics.Stopwatch]::StartNew()
 if($Step -eq 'CtrlPaste'){ [TransferInput]::Chord($session.pid, @(0x11,0x56)) }
 if($Step -eq 'MenuPaste') {
  $list = Elements | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::List -and $_.Current.Name -eq '項目ビュー' } | Select-Object -First 1
  $rect = $list.Current.BoundingRectangle
  [TransferInput]::Click($session.pid,[int]($rect.X+$rect.Width/2),[int]($rect.Bottom-50),$true)
  Start-Sleep -Milliseconds 250
  $menu = $desktop.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $session.pid -and $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Menu } | Select-Object -First 1
  $paste = $menu.FindAll($scope,$all) | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::MenuItem -and $_.Current.Name -match '^貼り付け' } | Select-Object -First 1
  if(!$paste){ [TransferInput]::Chord($session.pid,@(0x1B)); throw '貼り付けメニューが見つかりません。' }
  $point=$paste.GetClickablePoint(); $watch.Restart()
  [TransferInput]::Click($session.pid,[int]$point.X,[int]$point.Y,$false)
 }
 if($Step -eq 'TabDrop') {
  $header=Elements | Where-Object { $_.Current.AutomationId -like '右.tab.*' } | Select-Object -First 1
  $from=$item.GetClickablePoint(); $to=$header.GetClickablePoint()
  [TransferInput]::Drag($session.pid,[int]$from.X,[int]$from.Y,[int]$to.X,[int]$to.Y)
 }
 $newFile = $null; $responds = $true
 $copyDeadline = [DateTime]::UtcNow.AddSeconds(10)
 do {
  Start-Sleep -Milliseconds 30
  $responds = $responds -and [TransferInput]::Responds($hwnd)
  $newFile = Get-ChildItem -LiteralPath $destination -File | Where-Object { $_.FullName -notin $before -and $_.Extension -eq '.bin' } | Select-Object -First 1
 } while(!$newFile -and $responds -and [DateTime]::UtcNow -lt $copyDeadline)
 $inProgress = $newFile -and ![TransferInput]::Ready($newFile.FullName,$session.size)
 $probeMs = $watch.ElapsedMilliseconds
 Write-Output "$Step response=$responds; inProgress=$inProgress; probeMs=$probeMs"
 Assert $responds 'コピー開始後にメインウィンドウが応答しない、または無効になりました。'
 Assert ([bool]$inProgress) '検証時点でコピー中を確認できません。ファイルサイズを増やして再検証してください。'
 if($CloseDuringCopy) {
  $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
 } else {
 (Find '右.newTab').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 $tabCount=@(Elements | Where-Object { $_.Current.AutomationId -like '右.tab.*' }).Count
 Assert ($tabCount -ge 2) 'コピー中に新しいタブを開けません。'
 }
 Until { $script:copied = Get-ChildItem -LiteralPath $destination -File | Where-Object { $_.FullName -notin $before -and $_.Extension -eq '.bin' } | Select-Object -First 1; $script:copied -and [TransferInput]::Ready($script:copied.FullName,$session.size) }
 Assert ((Get-FileHash -LiteralPath $session.file).Hash -eq (Get-FileHash -LiteralPath $script:copied.FullName).Hash) 'コピーした内容が一致しません。'
 if($CloseDuringCopy){ Until { !(Get-Process -Id $session.pid -ErrorAction SilentlyContinue) }; 'PASS: コピー中に閉じても内容を保持し、プロセスが終了' }
 "$Step PASS: コピー中の応答・タブ追加・内容一致" | Tee-Object -FilePath (Join-Path $session.trial 'results.txt') -Append
}
if ($Step -eq 'Inspect') { "response=$([TransferInput]::Responds($hwnd))"; Get-Content -LiteralPath (Join-Path $session.trial 'app.log') -Tail 8 }
if ($Step -eq 'Close') { $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
Write-Output ('検証フォルダー: ' + $session.trial)
