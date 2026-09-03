param(
    [ValidateSet(
        "All", "Search", "Unigram", "Regression",
        "Store", "Settings", "Notepad", "Word", "Chrome")]
    [string]$Scenario = "All",
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

[UnmanagedFunctionPointer(CallingConvention.StdCall)]
public delegate int T9ActivateDelegate();

public static class T9IntegrationNative {
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(
        IntPtr parent, IntPtr after, string className, string windowName);
    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")]
    public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr window, System.Text.StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    public static IntPtr FindVisibleT9Window() {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        EnumWindows((window, parameter) => {
            if (!IsWindowVisible(window)) {
                return true;
            }

            var title = new System.Text.StringBuilder(64);
            GetWindowText(window, title, title.Capacity);
            if (!string.Equals(title.ToString(), "T9 九键", StringComparison.Ordinal)) {
                return true;
            }

            Rect rect;
            if (!GetWindowRect(window, out rect)) {
                return true;
            }
            long width = Math.Max(0, rect.Right - rect.Left);
            long height = Math.Max(0, rect.Bottom - rect.Top);
            long area = width * height;
            if (area > bestArea) {
                best = window;
                bestArea = area;
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    public static void Tap(byte key) {
        keybd_event(key, 0, 0, UIntPtr.Zero);
        keybd_event(key, 0, 2, UIntPtr.Zero);
    }

    public static void Chord(byte modifier, byte key) {
        keybd_event(modifier, 0, 0, UIntPtr.Zero);
        Tap(key);
        keybd_event(modifier, 0, 2, UIntPtr.Zero);
    }

    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
}
'@

function Wait-Value {
    param(
        [scriptblock]$Probe,
        [string]$Failure,
        [int]$Seconds = $TimeoutSeconds
    )

    $limit = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $value = & $Probe
        if ($null -ne $value -and $false -ne $value) {
            return $value
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw $Failure
}

function Get-RegisteredIme {
    $clsid = "{A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}"
    $paths = @(
        "Registry::HKEY_CURRENT_USER\Software\Classes\CLSID\$clsid\InprocServer32",
        "Registry::HKEY_LOCAL_MACHINE\Software\Classes\CLSID\$clsid\InprocServer32"
    )
    foreach ($path in $paths) {
        $value = (Get-Item -LiteralPath $path -ErrorAction SilentlyContinue).GetValue("")
        if ($value -and (Test-Path -LiteralPath $value)) {
            return $value
        }
    }
    throw "未找到已注册的 x64 T9Ime.dll"
}

function Enable-T9 {
    $dll = Get-RegisteredIme
    $handle = [Runtime.InteropServices.NativeLibrary]::Load($dll)
    try {
        $address = [Runtime.InteropServices.NativeLibrary]::GetExport($handle, "T9ImeActivate")
        $activate = [Runtime.InteropServices.Marshal]::GetDelegateForFunctionPointer(
            $address,
            [T9ActivateDelegate])
        $hr = $activate.Invoke()
        if ($hr -lt 0) {
            throw ("T9ImeActivate 失败: 0x{0:X8}" -f ([uint32]$hr))
        }
    }
    finally {
        [Runtime.InteropServices.NativeLibrary]::Free($handle)
    }
}

function Get-FocusedEdit {
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($focused -and
        $focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit) {
        return $focused
    }
    return $null
}

function Get-TaskbarSearchTarget {
    $handle = [T9IntegrationNative]::FindWindow("Shell_TrayWnd", $null)
    if ($handle -eq [IntPtr]::Zero) {
        return $null
    }

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    $condition = [System.Windows.Automation.OrCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            "SearchButton"),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            "SearchBox"))
    $items = $root.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
    foreach ($item in $items) {
        if (-not $item.Current.IsEnabled -or
            $item.Current.IsOffscreen) {
            continue
        }
        $bounds = $item.Current.BoundingRectangle
        if (-not $bounds.IsEmpty -and
            $bounds.Width -ge 120 -and
            $bounds.Height -ge 24) {
            return $item
        }
    }
    return $null
}

function Find-Editable {
    param([int[]]$ProcessIds)

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    foreach ($processId in $ProcessIds) {
        $processCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $processId)
        $items = $root.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $processCondition)
        $document = $null
        foreach ($item in $items) {
            if (-not $item.Current.IsEnabled -or $item.Current.IsOffscreen) {
                continue
            }
            $bounds = $item.Current.BoundingRectangle
            if ($bounds.IsEmpty -or
                $bounds.Left -lt -10000 -or
                $bounds.Top -lt -10000) {
                continue
            }
            if ($item.Current.ControlType -eq
                [System.Windows.Automation.ControlType]::Edit) {
                return $item
            }
            if ($item.Current.ControlType -eq
                    [System.Windows.Automation.ControlType]::Document -and
                $item.Current.IsKeyboardFocusable) {
                $document = $item
            }
        }
        if ($document) {
            return $document
        }
    }
    return $null
}

function Get-T9Overlay {
    $handle = [T9IntegrationNative]::FindVisibleT9Window()
    if ($handle -eq [IntPtr]::Zero) {
        $bandHost = [T9IntegrationNative]::FindWindow($null, "T9PaneBand")
        if ($bandHost -ne [IntPtr]::Zero) {
            $handle = [T9IntegrationNative]::FindWindowEx(
                $bandHost,
                [IntPtr]::Zero,
                $null,
                "T9 九键")
        }
    }
    if ($handle -ne [IntPtr]::Zero -and
        [T9IntegrationNative]::IsWindowVisible($handle)) {
        return [System.Windows.Automation.AutomationElement]::FromHandle($handle)
    }
    return $null
}

function Assert-OverlayAtEdit {
    param(
        [System.Windows.Automation.AutomationElement]$Edit,
        [System.Windows.Automation.AutomationElement]$Overlay
    )

    Assert-OverlayAtBounds $Edit.Current.BoundingRectangle $Overlay
}

function Assert-OverlayAtBounds {
    param(
        [System.Windows.Rect]$EditRect,
        [System.Windows.Automation.AutomationElement]$Overlay
    )

    $overlayRect = $Overlay.Current.BoundingRectangle
    if ($overlayRect.IsEmpty -or $overlayRect.Width -lt 300 -or $overlayRect.Height -lt 180) {
        throw "九键窗口尺寸无效: $overlayRect"
    }
    if ($overlayRect.Left -lt 20 -and $overlayRect.Top -lt 20) {
        throw "九键错误落在屏幕左上角: $overlayRect"
    }
    $verticalDistance = [Math]::Min(
        [Math]::Abs($overlayRect.Bottom - $editRect.Top),
        [Math]::Abs($overlayRect.Top - $editRect.Bottom))
    if ($verticalDistance -gt 96) {
        throw "九键未靠近当前输入框: edit=$EditRect overlay=$overlayRect"
    }
}

function Test-OverlayAtEdit {
    param(
        [System.Windows.Automation.AutomationElement]$Edit,
        [System.Windows.Automation.AutomationElement]$Overlay
    )

    try {
        $editRect = $Edit.Current.BoundingRectangle
        $overlayRect = $Overlay.Current.BoundingRectangle
        if ($overlayRect.IsEmpty -or $overlayRect.Width -lt 300 -or $overlayRect.Height -lt 180) {
            return $false
        }
        $distance = [Math]::Min(
            [Math]::Abs($overlayRect.Bottom - $editRect.Top),
            [Math]::Abs($overlayRect.Top - $editRect.Bottom))
        return $distance -le 96
    }
    catch {
        return $false
    }
}

function Test-SearchTransition {
    Enable-T9
    [T9IntegrationNative]::Tap(0x5B)
    [System.Windows.Forms.SendKeys]::SendWait("a")
    $startEdit = Wait-Value { Get-FocusedEdit } "开始菜单搜索框未获得焦点"
    $startEditRect = $startEdit.Current.BoundingRectangle
    $startOverlay = Wait-Value { Get-T9Overlay } "开始菜单搜索未显示九键"
    Assert-OverlayAtBounds $startEditRect $startOverlay
    $startRect = $startOverlay.Current.BoundingRectangle

    [T9IntegrationNative]::Tap(0x1B)
    Start-Sleep -Milliseconds 250
    $taskbarTarget = Wait-Value { Get-TaskbarSearchTarget } "未找到任务栏搜索按钮"
    $targetRect = $taskbarTarget.Current.BoundingRectangle
    [T9IntegrationNative]::Click(
        [int](($targetRect.Left + $targetRect.Right) / 2),
        [int](($targetRect.Top + $targetRect.Bottom) / 2))
    $taskbarEdit = Wait-Value { Get-FocusedEdit } "独立搜索框未在一次点击后获得焦点"
    $taskbarOverlay = Wait-Value {
        $currentEdit = Get-FocusedEdit
        if (-not $currentEdit) {
            return $null
        }
        $candidate = Get-T9Overlay
        if ($candidate -and (Test-OverlayAtEdit $currentEdit $candidate)) {
            return $candidate
        }
        return $null
    } "独立搜索未在一次操作后显示在正确输入框旁"
    $taskbarEdit = Wait-Value { Get-FocusedEdit } "独立搜索框在定位验证前失去焦点"
    Assert-OverlayAtEdit $taskbarEdit $taskbarOverlay
    Start-Sleep -Milliseconds 700
    $settled = Get-T9Overlay
    if (-not $settled) {
        throw "独立搜索九键闪现后消失"
    }
    $settledRect = $settled.Current.BoundingRectangle
    if ([Math]::Abs($settledRect.Left - $taskbarOverlay.Current.BoundingRectangle.Left) -gt 8 -or
        [Math]::Abs($settledRect.Top - $taskbarOverlay.Current.BoundingRectangle.Top) -gt 8) {
        throw "独立搜索首次定位不稳定，需要二次事件"
    }
    [T9IntegrationNative]::Tap(0x1B)
    Write-Output "PASS Search start=$startRect independent=$settledRect"
}

function Test-Unigram {
    Enable-T9
    $package = Get-AppxPackage -Name "*Unigram*" | Select-Object -First 1
    if (-not $package) {
        throw "未安装 Unigram"
    }
    Start-Process -FilePath "explorer.exe" -ArgumentList "shell:AppsFolder\$($package.PackageFamilyName)!App"
    $process = Wait-Value {
        Get-Process -Name "Telegram" -ErrorAction SilentlyContinue | Select-Object -First 1
    } "Unigram 未启动"

    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $process.Id)
    $edit = Wait-Value {
        $items = $root.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants,
            $processCondition)
        foreach ($item in $items) {
            if ($item.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -and
                $item.Current.IsEnabled -and
                -not $item.Current.IsOffscreen) {
                return $item
            }
        }
        return $null
    } "Unigram 中未找到可编辑搜索框"

    $edit.SetFocus()
    $overlay = Wait-Value { Get-T9Overlay } "Unigram 聚焦后未显示九键"
    Assert-OverlayAtEdit $edit $overlay

    $rawValue = $null
    if ($edit.TryGetCurrentPattern(
            [System.Windows.Automation.ValuePattern]::Pattern,
            [ref]$rawValue)) {
        $value = [System.Windows.Automation.ValuePattern]$rawValue
        if (-not $value.Current.IsReadOnly) {
            $original = $value.Current.Value
            try {
                $value.SetValue("T9E2E")
                $edit.SetFocus()
                $buttonCondition = [System.Windows.Automation.AndCondition]::new(
                    [System.Windows.Automation.PropertyCondition]::new(
                        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                        [System.Windows.Automation.ControlType]::Button),
                    [System.Windows.Automation.PropertyCondition]::new(
                        [System.Windows.Automation.AutomationElement]::NameProperty,
                        "⌫"))
                $backspace = $overlay.FindFirst(
                    [System.Windows.Automation.TreeScope]::Descendants,
                    $buttonCondition)
                if (-not $backspace) {
                    throw "九键中未找到退格按钮"
                }
                $invoke = [System.Windows.Automation.InvokePattern]$backspace.GetCurrentPattern(
                    [System.Windows.Automation.InvokePattern]::Pattern)
                $invoke.Invoke()
                $invoke.Invoke()
                Wait-Value {
                    if ($value.Current.Value.Length -eq 3) { return $true }
                    return $false
                } "Unigram 连续两次退格未生效"
            }
            finally {
                $value.SetValue($original)
            }
        }
    }
    Write-Output "PASS Unigram pid=$($process.Id)"
}

function Test-ApplicationSurface {
    param(
        [string]$Name,
        [scriptblock]$Launch,
        [string[]]$ProcessNames,
        [int]$Seconds = $TimeoutSeconds
    )

    Enable-T9
    & $Launch
    $processes = Wait-Value {
        $found = @(
            foreach ($processName in $ProcessNames) {
                Get-Process -Name $processName -ErrorAction SilentlyContinue
            }
        )
        if ($found.Count) {
            return @($found | Sort-Object StartTime -Descending)
        }
        return $null
    } "$Name 未启动" $Seconds
    $target = Wait-Value {
        Find-Editable @($processes.Id)
    } "$Name 中未找到 TSF 可编辑表面" $Seconds

    $target.SetFocus()
    $overlay = Wait-Value { Get-T9Overlay } "$Name 聚焦后未显示九键" $Seconds
    Assert-OverlayAtEdit $target $overlay

    [void]([T9IntegrationNative]::SetForegroundWindow(
        [T9IntegrationNative]::GetShellWindow()))
    Wait-Value {
        if (-not (Get-T9Overlay)) { return $true }
        return $false
    } "$Name 失焦后九键未隐藏" $Seconds
    Write-Output "PASS $Name pid=$($processes[0].Id)"
}

function Test-RegressionApplications {
    Test-ApplicationSurface "Store" {
        Start-Process "ms-windows-store://home"
    } @("WinStore.App")
    Test-ApplicationSurface "Settings" {
        Start-Process "ms-settings:"
    } @("SystemSettings")
    Test-ApplicationSurface "Notepad" {
        Start-Process "notepad.exe" -PassThru
    } @("Notepad")
    Test-ApplicationSurface "Word" {
        Start-Process "winword.exe" -ArgumentList "/q" -PassThru
    } @("WINWORD") 30
    Test-ApplicationSurface "Chrome" {
        Start-Process "chrome.exe" -PassThru -ArgumentList @(
            "--new-window",
            "data:text/html,<input autofocus aria-label='T9 regression'>")
    } @("chrome") 30
}

if ($Scenario -in @("All", "Search")) {
    Test-SearchTransition
}
if ($Scenario -in @("All", "Unigram")) {
    Test-Unigram
}
if ($Scenario -in @("All", "Regression")) {
    Test-RegressionApplications
}
if ($Scenario -eq "Store") {
    Test-ApplicationSurface "Store" {
        Start-Process "ms-windows-store://home"
    } @("WinStore.App")
}
if ($Scenario -eq "Settings") {
    Test-ApplicationSurface "Settings" {
        Start-Process "ms-settings:"
    } @("SystemSettings")
}
if ($Scenario -eq "Notepad") {
    Test-ApplicationSurface "Notepad" {
        Start-Process "notepad.exe" -PassThru
    } @("Notepad")
}
if ($Scenario -eq "Word") {
    Test-ApplicationSurface "Word" {
        Start-Process "winword.exe" -ArgumentList "/q" -PassThru
    } @("WINWORD") 30
}
if ($Scenario -eq "Chrome") {
    Test-ApplicationSurface "Chrome" {
        Start-Process "chrome.exe" -PassThru -ArgumentList @(
            "--new-window",
            "data:text/html,<input autofocus aria-label='T9 regression'>")
    } @("chrome") 30
}
