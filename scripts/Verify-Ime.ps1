param([int]$ProcessId = [int](Get-Content (Join-Path $PSScriptRoot '..\artifacts\app.pid')))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class ImeTestInput {
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
}
'@
$all = [System.Windows.Automation.Condition]::TrueCondition
$scope = [System.Windows.Automation.TreeScope]::Descendants
$window = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,$all) | Where-Object { $_.Current.ProcessId -eq $ProcessId -and $_.Current.Name -like 'Explorer Alt*' } | Select-Object -First 1
[ImeTestInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
[System.Windows.Forms.SendKeys]::SendWait('^l')
Start-Sleep -Milliseconds 300
$address = [System.Windows.Automation.AutomationElement]::FocusedElement
if ($address.Current.ClassName -ne 'TextBox') { throw 'パス入力欄にフォーカスがありません。' }
$value = $address.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$original = $value.Current.Value
try {
 $value.SetValue('')
 [ImeTestInput]::keybd_event(0x19,0,0,[UIntPtr]::Zero)
 [ImeTestInput]::keybd_event(0x19,0,2,[UIntPtr]::Zero)
 [System.Windows.Forms.SendKeys]::SendWait('nihongo')
 [System.Windows.Forms.SendKeys]::SendWait(' ')
 [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
 Start-Sleep -Milliseconds 500
 $actual = $value.Current.Value
 "IME入力結果: $actual"
 if ($actual -notmatch '[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]') { throw '日本語IMEによる確定文字列を確認できません。' }
 'PASS: WPFパス欄で日本語IMEの入力・変換・確定'
} finally {
 [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
 $value.SetValue($original)
 [ImeTestInput]::keybd_event(0x19,0,0,[UIntPtr]::Zero)
 [ImeTestInput]::keybd_event(0x19,0,2,[UIntPtr]::Zero)
 [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
}
