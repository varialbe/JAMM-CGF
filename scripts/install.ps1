# PowerShell 5.1+, per-user installation. Git is required for pinned recursive submodules.
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:LOCALAPPDATA 'jamm-cgf'
$source = Join-Path $root 'src'
$bin = Join-Path $root 'bin'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'JAMM-CGF.lnk'
function Run-Native($Exe, [string[]]$Arguments) {
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed (exit $LASTEXITCODE). Check the output above and retry." }
}
try {
    if ($Uninstall) {
        foreach ($name in @('cgfmm.cmd', 'cgfmm-gui.cmd')) {
            Remove-Item (Join-Path $bin $name) -Force -ErrorAction SilentlyContinue
        }
        Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
        Write-Host "Removed launchers and Start-menu shortcut. Source at $source, shared .NET, manager data and game backups are retained."
        exit 0
    }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git is required to fetch the pinned recursive submodules reliably. Install Git for Windows, reopen PowerShell and retry.'
    }
    $hostPath = $null
    $localHost = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    $systemHost = Get-Command dotnet -ErrorAction SilentlyContinue
    $candidates = @($localHost)
    if ($systemHost) { $candidates = @($systemHost.Source) + $candidates }
    foreach ($candidate in $candidates) {
        if ((Test-Path $candidate) -and ((& $candidate --list-sdks) -match '^10\.')) { $hostPath = $candidate; break }
    }
    if (-not $hostPath) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $tempScript = Join-Path ([IO.Path]::GetTempPath()) ([IO.Path]::GetRandomFileName() + '.ps1')
        try {
            Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -UseBasicParsing -OutFile $tempScript
            & $tempScript -Channel '10.0' -InstallDir (Split-Path $localHost) -NoPath
            if (-not (Test-Path $localHost)) { throw 'Microsoft SDK installation failed. Check network access and retry.' }
        } finally { Remove-Item $tempScript -Force -ErrorAction SilentlyContinue }
        $hostPath = $localHost
    }
    $env:DOTNET_ROOT = Split-Path $hostPath
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    New-Item $root -ItemType Directory -Force | Out-Null
    if (Test-Path $source) {
        if (-not (Test-Path (Join-Path $source '.git'))) { throw "Not a Git checkout: $source. Move it aside and retry." }
        $remote = & git -C $source remote get-url origin
        if ($LASTEXITCODE -ne 0 -or $remote -ne 'https://github.com/varialbe/JAMM-CGF.git') { throw "Unexpected source remote at $source." }
        $changes = & git -C $source status --porcelain
        if ($LASTEXITCODE -ne 0 -or $changes) { throw "Save local changes in $source elsewhere before updating." }
        $branch = & git -C $source branch --show-current
        if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') { throw "Source checkout must be on main: $source" }
        Run-Native git @('-C', $source, 'pull', '--ff-only', 'origin', 'main')
        Run-Native git @('-C', $source, 'submodule', 'update', '--init', '--recursive')
    } else {
        Run-Native git @('clone', '--branch', 'main', '--recurse-submodules', 'https://github.com/varialbe/JAMM-CGF.git', $source)
    }
    Run-Native $hostPath @('build', $source, '-c', 'Release')
    New-Item $bin -ItemType Directory -Force | Out-Null
    foreach ($app in @('cgfmm', 'cgfmm-gui')) {
        $project = 'Jamm.Cli'
        if ($app -eq 'cgfmm-gui') { $project = 'Jamm.Gui' }
        $dll = Join-Path $source "src\$project\bin\Release\net10.0\$app.dll"
        # Escape literal percent signs for cmd.exe.
        $dotnetCmd = $hostPath.Replace('%', '%%')
        $dllCmd = $dll.Replace('%', '%%')
        $rootCmd = $env:DOTNET_ROOT.Replace('%', '%%')
        @('@echo off', "set `"DOTNET_ROOT=$rootCmd`"", "`"$dotnetCmd`" `"$dllCmd`" %*") | Set-Content (Join-Path $bin "$app.cmd") -Encoding Default
    }
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $hostPath
    $guiDll = Join-Path $source 'src\Jamm.Gui\bin\Release\net10.0\cgfmm-gui.dll'
    $link.Arguments = "`"$guiDll`""
    $link.WorkingDirectory = $root
    $link.Save()
    Write-Host "Installed. Open JAMM-CGF from Start. Terminal launchers are in $bin (add to PATH if desired). Re-run to update."
} catch {
    Write-Error "JAMM installation failed: $($_.Exception.Message)" -ErrorAction Continue
    exit 1
}
