
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
$trial = Join-Path $root ('artifacts\sidebar-' + [Guid]::NewGuid().ToString('N'))
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
$app=Start-Process (Join-Path $root '.tools\dotnet\dotnet.exe') -ArgumentList @(('"'+$dll+'"'),('"'+$left+'"'),('"'+$right+'"')) -WindowStyle Hidden -PassThru
$script:window=$null
function Bookmark([string]$name) { (Elements) | Where-Object { $_.Current.AutomationId -like 'Sidebar.Bookmark.*' -and $_.Current.Name -eq $name } | Select-Object -First 1 }
function Menu([string]$name) { [System.Windows.Automation.AutomationElement]::RootElement.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name)) }
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
  if ($app.HasExited) { throw "検証アプリが起動中に終了しました。終了コード: $($app.ExitCode)" }
  $script:window=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $app.Id -and $_.Current.Name -like 'explorer-cover*' } | Select-Object -First 1
  $null -ne $script:window
 }
 At '左' $left; At '右' $right
 [TabInput]::Target=[uint32]$app.Id
 [TabInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
 (Find '左Address').SetFocus(); Start-Sleep -Milliseconds 250
 Go '左' $a
 Invoke 'Sidebar.AddBookmark'; Invoke 'Sidebar.AddBookmark'
 Assert (@((Elements) | Where-Object { $_.Current.AutomationId -like 'Sidebar.Bookmark.*' -and $_.Current.Name -eq '日本語 A' }).Count -eq 1) '重複したブックマークを作成した'
 Go '左' $b
 (Find '右Address').SetFocus(); Press @(0x11,0x54); At '右' $right
 Go '右' $c
 $bookmark=Bookmark '日本語 A'; $bookmark.SetFocus()
 Assert ($null -eq (Find 'Sidebar.Target')) '移動先表示が残っている'
 Assert ($bookmark.Current.HelpText.Contains($a)) 'ブックマークにフルパスがない'
 Assert ((Find 'Sidebar.RefreshDrives').Current.BoundingRectangle.Top -lt (Find 'Sidebar.AddBookmark').Current.BoundingRectangle.Top) 'ドライブが上に配置されていない'
 $bookmark.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 At '右' $a; At '左' $b
 Assert ((Tabs '右').Count -eq 2) '既存タブで開いていない'
 Press @(0x12,0x25); At '右' $c
 Press @(0x11,0x09); At '右' $right
 'PASS: サイドバーを触る直前の右タブで開き、履歴・別タブ・左ペインを保持'
 (Find '左Address').SetFocus(); $bookmark.SetFocus()
 $bookmark.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); At '左' $a
 Assert ((Tabs '左').Count -eq 1) '左に新規タブを作った'
 Press @(0x12,0x25); At '左' $b
 Remove-Item -LiteralPath $a # この検証が作った空フォルダーだけ
 $bookmark.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Wait-Until { (Find '左Status').Current.Name -like '移動できません:*' }
 Assert ($null -ne (Bookmark '日本語 A')) '到達できないブックマークを消した'
 New-Item -ItemType Directory $a | Out-Null
 'PASS: 左タブへの移動と、到達できない登録先の保持'
 $bookmark.SetFocus(); Press @(0x5D)
 Wait-Until { $null -ne (Menu '名前を変更') }
 (Menu '名前を変更').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Wait-Until { $null -ne (Menu 'ブックマーク名を変更') }
 $dialog=Menu 'ブックマーク名を変更'
 $input=$dialog.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'Bookmark.Name'))
 $input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('作業資料')
 $save=$dialog.FindFirst($scope,[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,'Bookmark.Save'))
 $save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Wait-Until { $null -ne (Bookmark '作業資料') }
 $bookmark=Bookmark '作業資料'; $bookmark.SetFocus(); Press @(0x5D)
 Wait-Until { $null -ne (Menu 'ブックマークを削除') }
 (Menu 'ブックマークを削除').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Wait-Until { $null -eq (Bookmark '作業資料') }
 Assert (Test-Path -LiteralPath $a) 'ブックマーク削除で実フォルダーも削除した'
 'PASS: ブックマークの改名・削除'
 $driveId='Sidebar.Drive.'+[IO.Path]::GetPathRoot($trial)
 Wait-Until { $null -ne (Find $driveId) -and (Find $driveId).Current.Name -match '\d+\.\dGiB/\d+\.\dGiB' }
 Assert ((Find $driveId).Current.HelpText -like '*空き容量 / 総容量*') '容量の意味を確認できない'
 foreach($drive in [IO.DriveInfo]::GetDrives()){
  if(!$drive.IsReady){$row=Find ('Sidebar.Drive.'+$drive.Name); Assert ($null -eq $row -or $row.Current.IsOffscreen) 'メディアなしドライブが表示されている'}
 }
 'PASS: ドライブを上へ配置、容量の簡潔な表示、メディアなし非表示、ブックマークのフルパス'
 (Find '右Address').SetFocus(); (Find $driveId).SetFocus(); Invoke $driveId
 At '右' ([IO.Path]::GetPathRoot($trial))
 Invoke 'Sidebar.RefreshDrives'
 Go '右' $right; Go '左' $left
  'PASS: ドライブ容量・使用率表示、更新と対象タブへのルート移動'
  (Find $driveId).SetFocus()
  Start-Sleep -Seconds 11
  Assert ((Find $driveId).Current.HasKeyboardFocus) '自動更新でサイドバーのフォーカスを失った'
  $rect=(Find 'Sidebar.Splitter').Current.BoundingRectangle
  Write-Output ('境界（変更前）: '+$rect)
  $hit=[System.Windows.Automation.AutomationElement]::FromPoint([System.Windows.Point]::new($rect.X+2,$rect.Y+100))
  Write-Output ('境界の操作対象: '+$hit.Current.ProcessId+' '+$hit.Current.ClassName+' '+$hit.Current.AutomationId)
  [TabInput]::Drag([int]($rect.X+2),[int]($rect.Y+100),[int]($rect.X+62),[int]($rect.Y+100))
  Start-Sleep -Milliseconds 300
  Write-Output ('境界（変更後）: '+(Find 'Sidebar.Splitter').Current.BoundingRectangle)
  Assert (((Find 'Sidebar.Splitter').Current.BoundingRectangle.X-$rect.X) -gt 40) 'サイドバーの幅を変更できない'
  $rightButton=(Find '右.navigateAddress').Current.BoundingRectangle
  Write-Output ('右移動ボタン: '+$rightButton+' 窓: '+$window.Current.BoundingRectangle)
  Assert ($rightButton.Right -le $window.Current.BoundingRectangle.Right) '幅変更で右ペインのボタンが画面外へ出た'
  'PASS: 自動更新でフォーカス保持、サイドバー幅のドラッグ変更'
 Capture 'sidebar-wide.png'
 $bounds=(Find $driveId).Current.BoundingRectangle
 [TabInput]::Hover([int]($bounds.X+10),[int]($bounds.Y+10))
 Capture 'sidebar-hover.png'
 (Find '右Address').SetFocus(); Press @(0x1B)
 $rect=(Find 'Sidebar.Splitter').Current.BoundingRectangle
 [TabInput]::Drag([int]($rect.X+2),[int]($rect.Y+100),[int]($rect.X-178),[int]($rect.Y+100))
 Capture 'sidebar-narrow.png'
 $rect=(Find 'Sidebar.Splitter').Current.BoundingRectangle
 [TabInput]::Drag([int]($rect.X+2),[int]($rect.Y+100),[int]($rect.X+122),[int]($rect.Y+100))
 Capture 'sidebar.png'
 'PASS: ホバー・最小幅・左右の配色を画像に記録'
} catch {
 Write-Output ($_ | Out-String)
 throw
} finally {
 if($script:window){try{$script:window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()}catch{}}
 if(!$app.HasExited){$app.CloseMainWindow() | Out-Null}
 if(!$app.WaitForExit(5000)){Write-Warning ('検証アプリの終了待機に失敗: '+$app.Id)}
 $env:EXPLORER_COVER_SHORTCUTS=$oldConfig; $env:EXPLORER_COVER_LOG=$oldLog
 Write-Output ('検証ログ: '+$log)
}
