[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SetupPath,
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$Version,
    [switch]$Worker,
    [string]$RunDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$setup = (Resolve-Path -LiteralPath $SetupPath).Path
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
if (-not $Worker) {
    $RunDirectory = Join-Path $projectRoot ('.scratch/installer-validation/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $RunDirectory | Out-Null
    if (-not ('InstallerTestDesktop' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'InstallerTestDesktop.cs') }
    $pwsh = (Get-Process -Id $PID).Path
    $workerArgs = @($pwsh,'-NoLogo','-NoProfile','-NonInteractive','-File',$PSCommandPath,'-Worker','-SetupPath',$setup,'-PayloadDirectory',$payload,'-Version',$Version,'-RunDirectory',$RunDirectory)
    $commandLine = ($workerArgs | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $code = [InstallerTestDesktop]::Run($pwsh, $commandLine, $projectRoot)
    $logPath = Join-Path $RunDirectory 'checks.log'
    if (Test-Path -LiteralPath $logPath) { Get-Content -LiteralPath $logPath | Write-Host }
    if ($code -ne 0) { throw "Installer validation failed. Evidence: $RunDirectory" }
    Write-Host "Installer validation passed: $RunDirectory"
    return
}

$entry = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/Inklane.Desktop'
$installRoot = Join-Path $env:LOCALAPPDATA 'Programs/Inklane'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Inklane.lnk'
$checkLog = Join-Path $RunDirectory 'checks.log'
# The worker is a dedicated process. Normal app launches use disposable preferences and recovery data.
$env:INKLANE_DATA_HOME = Join-Path $RunDirectory 'app-data'
$settingsRoot = Join-Path $env:LOCALAPPDATA 'Inklane'
$createdSettingsRoot = $false
$sentinel = Join-Path $settingsRoot ('installer-check-' + [guid]::NewGuid().ToString('N') + '.json')
$ownedFiles = [Collections.Generic.List[string]]::new()
$ownedDirectories = [Collections.Generic.List[string]]::new()
$runMarker = [guid]::NewGuid().ToString('N')
$caseResults = [Collections.Generic.List[string]]::new()
function Assert-Check([bool]$condition, [string]$description) {
    if (-not $condition) { throw $description }
    $caseResults.Add($description)
    Add-Content -LiteralPath $checkLog -Value "PASS $description"
}
function Wait-Until([scriptblock]$predicate) {
    $deadline = [datetime]::UtcNow.AddSeconds(45)
    do { if (& $predicate) { return }; Start-Sleep -Milliseconds 150 } while ([datetime]::UtcNow -lt $deadline)
    throw 'An expected Windows installer or application state did not appear in time.'
}
function Invoke-Setup([string]$exe, [string[]]$arguments, [int]$expectedExit = 0) {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(120000)) { throw "Timed out waiting for $exe" }
    $process.Refresh()
    if ($process.ExitCode -ne $expectedExit) { throw "$exe returned $($process.ExitCode), expected $expectedExit." }
}
function Get-RegisteredFolder {
    $registration = Get-ItemProperty -LiteralPath $entry
    $folder = [IO.Path]::GetFullPath($registration.InstallLocation)
    if ($registration.Family -ne 'Inklane.Desktop.Install.1' -or (Split-Path -Parent $folder) -ne $installRoot) {
        throw 'Unexpected installer registration. No cleanup will run against this path.'
    }
    return $folder
}
function Invoke-RegisteredUninstall {
    $registration = Get-ItemProperty -LiteralPath $entry
    $folder = Get-RegisteredFolder
    $uninstaller = Join-Path $folder 'Uninstall.exe'
    if ($registration.UninstallString -ne ('"' + $uninstaller + '"') -or -not (Test-Path -LiteralPath $uninstaller)) { throw 'Invalid Windows uninstall entry.' }
    Invoke-Setup $uninstaller @('/S')
    Wait-Until { -not (Test-Path -LiteralPath $entry) -and -not (Test-Path -LiteralPath $uninstaller) }
}
function Add-Sentinel([string]$path) {
    if (Test-Path -LiteralPath $path) { throw "Test fixture collision: $path" }
    [IO.File]::WriteAllText($path,$runMarker)
    $ownedFiles.Add($path)
}
function Get-DefaultAssociations {
    $defaults = [ordered]@{}
    foreach ($extension in @('.png','.jpg','.jpeg','.pdf')) {
        $choice = "HKCU:/Software/Microsoft/Windows/CurrentVersion/Explorer/FileExts/$extension/UserChoice"
        $class = "HKCU:/Software/Classes/$extension"
        $defaults["$extension-choice"] = Get-ItemPropertyValue -LiteralPath $choice -Name 'ProgId' -ErrorAction SilentlyContinue
        $defaults["$extension-class"] = if (Test-Path -LiteralPath $class) { (Get-Item -LiteralPath $class).GetValue('') }
    }
    return $defaults | ConvertTo-Json -Compress
}

try {
    # Tests use the exact deliverable and its real HKCU/Start entries. Never claim an existing installation.
    foreach ($path in @($entry,$shortcut,$installRoot)) {
        if (Test-Path -LiteralPath $path) { throw "Installer checks require a clean per-user installation slot. Already present: $path. Run these checks in a clean Windows profile." }
    }
    $defaultAssociations = Get-DefaultAssociations
    if (-not ('InstallerTestDesktop' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'InstallerTestDesktop.cs') }
    $fixture = Join-Path $RunDirectory 'old-payload'
    New-Item -ItemType Directory -Path $fixture | Out-Null
    Copy-Item -LiteralPath (Join-Path $payload 'Inklane.exe') -Destination $fixture
    [IO.File]::WriteAllText((Join-Path $fixture 'obsolete.txt'),'An owned file from the previous-version fixture.')
    $baseline = Join-Path $RunDirectory 'previous-version-fixture.exe'
    & (Join-Path $PSScriptRoot 'new-installer.ps1') -PayloadDirectory $fixture -Version '0.0.0-alpha.1' -OutputPath $baseline
    if (-not (Test-Path -LiteralPath $settingsRoot)) {
        New-Item -ItemType Directory -Path $settingsRoot | Out-Null
        $createdSettingsRoot = $true
    }
    Add-Sentinel $sentinel

    Invoke-Setup $setup @('/S')
    $freshDir = Get-RegisteredFolder
    $ownedDirectories.Add($freshDir)
    Assert-Check ((Get-ItemProperty -LiteralPath $entry).DisplayVersion -eq $Version) 'Fresh setup appears in Installed apps with the correct version.'
    Assert-Check ((Get-DefaultAssociations) -ceq $defaultAssociations) 'Existing image and PDF default associations survive installation.'
    Assert-Check ((Get-FileHash -LiteralPath (Join-Path $freshDir 'Inklane.dll')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $payload 'Inklane.dll')).Hash) 'Installed application assembly matches the published payload.'
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    Assert-Check ($link.TargetPath -eq (Join-Path $freshDir 'Inklane.exe') -and [string]::IsNullOrEmpty($link.Arguments)) 'The Start menu shortcut targets the installed application with normal startup arguments.'
    $smoke = Join-Path $RunDirectory 'installed-startup.json'
    Invoke-Setup $shortcut @('--smoke-test',('"' + $smoke + '"'))
    Wait-Until { Test-Path -LiteralPath $smoke }
    $startup = Get-Content -LiteralPath $smoke -Raw | ConvertFrom-Json
    Assert-Check ($startup.success -and $startup.windowRendered -and [IO.Path]::GetFullPath($startup.runtimeDirectory).TrimEnd('\') -eq $freshDir) 'The installed Start shortcut launches a rendered window using the installed runtime.'
    # An ordinary launch exercises the normal public entry point on this isolated Windows desktop.
    $normal = Start-Process -FilePath $shortcut -PassThru -WindowStyle Normal
    Wait-Until { $normal.Refresh(); -not $normal.HasExited -and [InstallerTestDesktop]::FindWorkspace($normal.Id) -ne [IntPtr]::Zero }
    Assert-Check ($normal.Path -eq (Join-Path $freshDir 'Inklane.exe')) 'The installed Start shortcut opens an application window without test arguments.'
    [InstallerTestDesktop]::CloseWorkspace([InstallerTestDesktop]::FindWorkspace($normal.Id),$normal.Id)
    Assert-Check ($normal.WaitForExit(10000)) 'The installed workspace closes normally from a clean profile.'
    Invoke-RegisteredUninstall
    Assert-Check (-not (Test-Path -LiteralPath $shortcut) -and -not (Test-Path -LiteralPath (Join-Path $freshDir 'Inklane.exe'))) 'The registered Windows uninstaller removes the shortcut and application.'

    Invoke-Setup $baseline @('/S')
    $oldDir = Get-RegisteredFolder
    $ownedDirectories.Add($oldDir)
    $notebook = Join-Path $oldDir 'user-notebook.pdf'
    $annotation = Join-Path $installRoot 'image.png.inklane.json'
    Add-Sentinel $notebook
    Add-Sentinel $annotation
    $lock = [IO.File]::Open((Join-Path $oldDir 'Inklane.exe'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try { Invoke-Setup $setup @('/S') 5 } finally { $lock.Dispose() }
    Assert-Check ((Get-RegisteredFolder) -eq $oldDir -and (Test-Path -LiteralPath (Join-Path $oldDir 'Inklane.exe'))) 'A locked previous installation blocks upgrading and remains registered.'
    Invoke-Setup $setup @('/S')
    $upgraded = Get-RegisteredFolder
    $ownedDirectories.Add($upgraded)
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
    Assert-Check ($upgraded -ne $oldDir -and $link.TargetPath -eq (Join-Path $upgraded 'Inklane.exe') -and (Get-ItemProperty -LiteralPath $entry).DisplayVersion -eq $Version) 'Upgrade switches the normal shortcut and single Installed apps entry to the new version.'
    Assert-Check (-not (Test-Path -LiteralPath (Join-Path $oldDir 'obsolete.txt')) -and -not (Test-Path -LiteralPath (Join-Path $oldDir 'Inklane.exe'))) 'Upgrade removes files owned by the previous version.'
    Invoke-Setup $baseline @('/S') 4
    Assert-Check ((Get-RegisteredFolder) -eq $upgraded) 'An older setup cannot downgrade the active installation.'
    Invoke-RegisteredUninstall
    foreach ($path in @($notebook,$annotation,$sentinel)) {
        Assert-Check (([IO.File]::ReadAllText($path)) -ceq $runMarker) "Upgrade and uninstall preserved the test file $([IO.Path]::GetFileName($path))."
    }
    Assert-Check (-not (Test-Path -LiteralPath $shortcut) -and -not (Test-Path -LiteralPath $entry)) 'Final uninstall removed the Start menu and Installed apps entries.'
    Assert-Check ((Get-DefaultAssociations) -ceq $defaultAssociations) 'Existing image and PDF default associations survive upgrade and uninstall.'
    [ordered]@{ version=$Version; setupSha256=(Get-FileHash -LiteralPath $setup).Hash; signature=(Get-AuthenticodeSignature -LiteralPath $setup).Status.ToString(); completedUtc=[datetime]::UtcNow.ToString('O'); checks=$caseResults; installedStartup=$startup } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $RunDirectory 'result.json') -Encoding utf8
} catch {
    Add-Content -LiteralPath $checkLog -Value ('FAIL ' + $_.Exception.Message + [Environment]::NewLine + $_.ScriptStackTrace)
    exit 1
} finally {
    if ($normal -and -not $normal.HasExited -and $ownedDirectories.Contains((Split-Path -Parent $normal.Path))) {
        $normal.CloseMainWindow() | Out-Null
        if (-not $normal.WaitForExit(3000)) { $normal.Kill(); $normal.WaitForExit() }
    }
    if ($ownedDirectories.Count -gt 0 -and (Test-Path -LiteralPath $entry)) {
        try {
            if ($ownedDirectories.Contains((Get-RegisteredFolder))) { Invoke-RegisteredUninstall }
        } catch { Add-Content -LiteralPath $checkLog -Value ('Cleanup: ' + $_.Exception.Message) }
    }
    # Remove only the distinctive test files this run created. Directory removal is nonrecursive.
    foreach ($file in $ownedFiles) { if ((Test-Path -LiteralPath $file) -and [IO.File]::ReadAllText($file) -ceq $runMarker) { [IO.File]::Delete($file) } }
    foreach ($directory in @($ownedDirectories.ToArray()) + @($installRoot)) {
        if ($ownedDirectories.Count -gt 0 -and [IO.Directory]::Exists($directory) -and -not [IO.Directory]::EnumerateFileSystemEntries($directory).GetEnumerator().MoveNext()) { [IO.Directory]::Delete($directory) }
    }
    if ($createdSettingsRoot -and [IO.Directory]::Exists($settingsRoot) -and -not [IO.Directory]::EnumerateFileSystemEntries($settingsRoot).GetEnumerator().MoveNext()) { [IO.Directory]::Delete($settingsRoot) }
}
