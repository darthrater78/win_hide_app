# Launches the published exe next to Notepad, checks it survives startup, then clicks
# Notepad's button in the mockup to hide it from the real taskbar and again to bring it
# back, saving a screenshot of each state. Run by CI on windows-latest; works on any
# Windows machine.
#
#   pwsh scripts/smoke-test.ps1 -Exe dist/ShareHider-0.1.0-win-x64.exe
param(
    [Parameter(Mandatory)][string]$Exe,
    [string]$ScreenshotDir = 'dist',
    [int]$StartupSeconds = 10,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
$uia = [System.Windows.Automation.AutomationElement]
$tree = [System.Windows.Automation.TreeScope]

# .NET resolves relative paths against its own current directory, not PowerShell's location.
$ScreenshotDir = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $ScreenshotDir))

function Save-Screenshot([string]$Name) {
    $path = Join-Path $ScreenshotDir $Name
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    Write-Host "screenshot saved to $path"
}

# Polls until the script block returns something truthy, or fails with the message.
function Wait-Until([scriptblock]$Condition, [string]$Failure) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $result = & $Condition
        if ($result) { return $result }
        Start-Sleep -Milliseconds 250
    }
    throw $Failure
}

function Find-Buttons($Root, [string]$NameLike) {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        $uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    @($Root.FindAll($tree::Descendants, $condition) | Where-Object { $_.Current.Name -like $NameLike })
}

# Notepad's buttons on the real taskbar, found through UI Automation.
function Get-TaskbarNotepadCount {
    $condition = [System.Windows.Automation.PropertyCondition]::new($uia::ClassNameProperty, 'Shell_TrayWnd')
    $taskbar = $uia::RootElement.FindFirst($tree::Children, $condition)
    if (-not $taskbar) { return 0 }
    (Find-Buttons $taskbar '*Notepad*').Count
}

# Something for the taskbar mockup to show.
$notepad = Start-Process -FilePath notepad.exe -PassThru
$app = Start-Process -FilePath (Resolve-Path $Exe) -PassThru
try {
    Start-Sleep -Seconds $StartupSeconds
    if ($app.HasExited) {
        $log = Join-Path $env:LOCALAPPDATA 'ShareHider\log.txt'
        if (Test-Path $log) { Get-Content $log -Tail 40 }
        throw "ShareHider exited during startup (exit code $($app.ExitCode))."
    }
    Write-Host "ShareHider is running after $StartupSeconds s"
    Save-Screenshot 'smoke-1-started.png'

    $windowCondition = [System.Windows.Automation.PropertyCondition]::new($uia::ProcessIdProperty, $app.Id)
    $window = Wait-Until { $uia::RootElement.FindFirst($tree::Children, $windowCondition) } `
        'The ShareHider window did not open on startup.'
    # Looked up afresh each time, in case a mockup refresh replaces the button.
    function Get-MockButton {
        Wait-Until { Find-Buttons $window '*Notepad*' | Select-Object -First 1 } 'Notepad has no button in the taskbar mockup.'
    }
    function Invoke-MockButton {
        (Get-MockButton).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    Get-MockButton | Out-Null

    # The real taskbar's layout differs between Windows versions; when UI Automation can't
    # see Notepad's button there, the mockup state and the screenshots are the evidence.
    $taskbarBefore = Get-TaskbarNotepadCount
    if ($taskbarBefore -eq 0) {
        Write-Warning "Notepad's real taskbar button isn't visible to UI Automation; checking the mockup only."
    }

    Invoke-MockButton
    Wait-Until { (Get-MockButton).Current.HelpText -like '*Hidden from the taskbar*' } `
        'The mockup did not mark Notepad hidden after the click.' | Out-Null
    if ($taskbarBefore -gt 0) {
        Wait-Until { (Get-TaskbarNotepadCount) -eq 0 } "Notepad's button is still on the taskbar." | Out-Null
    }
    Write-Host 'Notepad hidden'
    Save-Screenshot 'smoke-2-hidden.png'

    Invoke-MockButton
    Wait-Until { (Get-MockButton).Current.HelpText -like '*Shown on the taskbar*' } `
        'The mockup did not mark Notepad shown after the second click.' | Out-Null
    if ($taskbarBefore -gt 0) {
        Wait-Until { (Get-TaskbarNotepadCount) -gt 0 } "Notepad's button did not come back to the taskbar." | Out-Null
    }
    Write-Host 'Notepad restored'
    Save-Screenshot 'smoke-3-restored.png'
}
finally {
    foreach ($process in $app, $notepad) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
