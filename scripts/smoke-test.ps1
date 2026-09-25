# Launches the published exe next to Notepad, checks it survives startup, and saves a
# screenshot of its window. Run by CI on windows-latest; works on any Windows machine.
#
#   pwsh scripts/smoke-test.ps1 -Exe dist/ShareHider-0.1.0-win-x64.exe
param(
    [Parameter(Mandatory)][string]$Exe,
    [string]$Screenshot = 'dist/smoke-test.png',
    [int]$StartupSeconds = 10
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# .NET resolves relative paths against its own current directory, not PowerShell's location.
$Screenshot = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Screenshot))

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

    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save($Screenshot, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }

    Write-Host "ShareHider is running after $StartupSeconds s; screenshot saved to $Screenshot"
}
finally {
    foreach ($process in $app, $notepad) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    }
}
