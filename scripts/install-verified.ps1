[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$entry = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Inklane.Desktop'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs/Inklane'
$data = Join-Path $env:LOCALAPPDATA 'Inklane'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Inklane.lnk'

function DataFingerprint {
    if (!(Test-Path -LiteralPath $data)) { return }
    Get-ChildItem -LiteralPath $data -File -Recurse -Force | Sort-Object FullName | ForEach-Object {
        "$($_.FullName)|$((Get-FileHash -LiteralPath $_.FullName).Hash)"
    }
}

Push-Location $root
try {
    $commit = & git rev-parse HEAD
    if ($LASTEXITCODE -or (& git branch --show-current) -ne 'main' -or (& git status --porcelain) -or
        $commit -ne (& git rev-parse origin/main)) {
        throw 'Use a clean main checkout at the integrated origin/main commit.'
    }
    $current = Get-ItemProperty -LiteralPath $entry -ErrorAction SilentlyContinue
    if ($current -and ($current.Family -ne 'Inklane.Desktop.Install.1' -or
        (Split-Path -Parent $current.InstallLocation) -ine $installRoot)) {
        throw 'The registered installation is outside the expected per-user Inklane location.'
    }
    $oldExe = if ($current) { Join-Path $current.InstallLocation 'Inklane.exe' }
    if (Get-Process Inklane -ErrorAction SilentlyContinue | Where-Object Path -IEQ $oldExe) {
        throw 'Close the installed Inklane window and save any pending work before upgrading.'
    }
    & (Join-Path $PSScriptRoot 'package.ps1')
    $version = (& dotnet msbuild src/Inklane/Inklane.csproj -getProperty:Version).Trim()
    if ($LASTEXITCODE -or $version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.]+)?$') { throw 'Unknown package version.' }
    $payload = Join-Path $root "artifacts/Inklane-$version-win-x64/Inklane.exe"
    if ((Get-Item -LiteralPath $payload).VersionInfo.ProductVersion -ne "$version+$commit") {
        throw 'The checked payload is not from the integrated commit.'
    }
    $setup = Join-Path $root "artifacts/Inklane-Setup-$version-x64.exe"
    $expected = ((Get-Content -LiteralPath "$setup.sha256" -Raw).Trim() -split '\s+')[0]
    if ((Get-FileHash -LiteralPath $setup).Hash -ne $expected) { throw 'Setup checksum does not match.' }
    $before = (DataFingerprint) -join "`n"
    $process = Start-Process -FilePath $setup -ArgumentList '/S' -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Inklane Setup failed with exit code $($process.ExitCode)." }
    if ($before -cne ((DataFingerprint) -join "`n")) {
        throw 'Application data changed during setup. Check the installed application before use.'
    }
    $installed = Get-ItemProperty -LiteralPath $entry
    $exe = Join-Path $installed.InstallLocation 'Inklane.exe'
    $target = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    if ($installed.DisplayVersion -ne $version -or $target.TargetPath -ine $exe -or $target.Arguments -or
        (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -ne "$version+$commit" -or
        (Get-FileHash -LiteralPath $exe).Hash -ne (Get-FileHash -LiteralPath $payload).Hash) {
        throw 'The installed executable, version, or Start shortcut does not match the verified payload.'
    }
    $launch = Start-Process -FilePath $shortcut -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        if ($launch.HasExited) { throw 'Inklane closed during startup from Start.' }
        $launch.Refresh()
        if ($launch.Path -ieq $exe -and $launch.MainWindowHandle) {
            Write-Host "Installed and launched Inklane $version from $exe"
            return
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'Start did not open the verified Inklane window within 30 seconds.'
}
finally { Pop-Location }
