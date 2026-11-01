[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
$output = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath (Join-Path $payload 'Inklane.exe'))) { throw 'The installer needs a published Inklane.exe.' }
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta|rc)\.(\d+))?$') { throw "Unsupported setup version: $Version" }
$components = @([int]$Matches[1], [int]$Matches[2], [int]$Matches[3])
if (($components | Measure-Object -Maximum).Maximum -gt 65535) { throw 'Version components must fit a Windows file version.' }
$revision = 65535
if ($Matches[4]) {
    $sequence = [int]$Matches[5]
    if ($sequence -gt 9999) { throw 'Prerelease sequence exceeds the supported range.' }
    $revision = @{alpha=10000; beta=20000; rc=30000}[$Matches[4]] + $sequence
}
$numericVersion = ($components + $revision) -join '.'

# Download the compiler as a portable archive. No machine registration or tool installation is needed.
$toolVersion = '3.13'
$expectedDigest = 'BA63DFFC4410EE89193E1CB5A41989991BD77C61068DA17E3156D136B7B0B3D8'
$toolRoot = Join-Path $projectRoot ".scratch/installer-tools/verified-$toolVersion"
$toolArchive = Join-Path $toolRoot "nsis-$toolVersion.zip"
New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $toolArchive) -or (Get-FileHash -LiteralPath $toolArchive).Hash -ne $expectedDigest) {
    $download = Join-Path $toolRoot ([guid]::NewGuid().ToString('N') + '.download')
    try {
        $basePath = "project/nsis/NSIS%203/$toolVersion/nsis-$toolVersion.zip"
        foreach ($url in @("https://downloads.sourceforge.net/$basePath", "https://sf-eu-introserv-1.dl.sourceforge.net/${basePath}?download")) {
            try {
                Invoke-WebRequest $url -OutFile $download -TimeoutSec 45 -UserAgent 'Wget/1.21.4'
                if ((Get-FileHash -LiteralPath $download).Hash -eq $expectedDigest) {
                    Move-Item -LiteralPath $download -Destination $toolArchive -Force
                    break
                }
            } catch { Write-Verbose "Compiler mirror unavailable: $url" }
        }
        if (-not (Test-Path -LiteralPath $toolArchive) -or (Get-FileHash -LiteralPath $toolArchive).Hash -ne $expectedDigest) {
            throw "Could not obtain the verified NSIS $toolVersion compiler archive."
        }
    } finally { if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download -Force } }
}
$buildDirectory = Join-Path $projectRoot ('.scratch/installer-build/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
Expand-Archive -LiteralPath $toolArchive -DestinationPath $buildDirectory
$compiler = Join-Path $buildDirectory "nsis-$toolVersion/bin/makensis.exe"

# Both extraction and removal come from precisely the same published file set.
# RMDir is deliberately nonrecursive. Files created by a user survive upgrades and removal.
$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File -Force | Where-Object Name -NE '.inklane-package' | Sort-Object FullName)
$directories = @(Get-ChildItem -LiteralPath $payload -Recurse -Directory -Force | Sort-Object { $_.FullName.Length } -Descending)
function NsisString([string]$value) { $value.Replace('$','$$').Replace('"','$\"') }
$extract = [Collections.Generic.List[string]]::new()
$remove = [Collections.Generic.List[string]]::new()
$directoryChecks = [Collections.Generic.List[string]]::new()
$access = [Collections.Generic.List[string]]::new()
$removed = [Collections.Generic.List[string]]::new()
$lastDirectory = $null
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($payload, $file.FullName)
    $directory = [IO.Path]::GetDirectoryName($relative)
    if ($directory -ne $lastDirectory) {
        $destination = if ($directory) { '$INSTDIR\' + (NsisString $directory) } else { '$INSTDIR' }
        $extract.Add('SetOutPath "' + $destination + '"')
        $lastDirectory = $directory
    }
    $extract.Add('File "' + (NsisString $file.FullName) + '"')
    $remove.Add('Delete "$INSTDIR\' + (NsisString $relative) + '"')
    $access.Add('!insertmacro CheckFileUnlocked "$INSTDIR\' + (NsisString $relative) + '"')
    $removed.Add('IfFileExists "$INSTDIR\' + (NsisString $relative) + '" removal_failed 0')
}
# The generated installer and uninstaller include NSIS code, so ship its upstream notice.
$nsisNotice = NsisString (Join-Path $buildDirectory "nsis-$toolVersion/COPYING")
$extract.Add('SetOutPath "$INSTDIR\licenses"')
$extract.Add('File "/oname=nsis-COPYING.txt" "' + $nsisNotice + '"')
$remove.Add('Delete "$INSTDIR\licenses\nsis-COPYING.txt"')
$directoryChecks.Add('!insertmacro RejectReparse "$INSTDIR\licenses"')
$access.Add('!insertmacro CheckFileUnlocked "$INSTDIR\licenses\nsis-COPYING.txt"')
$removed.Add('IfFileExists "$INSTDIR\licenses\nsis-COPYING.txt" removal_failed 0')
foreach ($directory in $directories) {
    $relative = NsisString ([IO.Path]::GetRelativePath($payload, $directory.FullName))
    $remove.Add('RMDir "$INSTDIR\' + $relative + '"')
    $directoryChecks.Add('!insertmacro RejectReparse "$INSTDIR\' + $relative + '"')
}
$remove.Add('RMDir "$INSTDIR\licenses"')
$manifest = Join-Path $buildDirectory 'payload.nsh'
@('!macro ExtractPayload') + $extract.ToArray() + @('!macroend','!macro RemovePayload') + $remove.ToArray() + @('!macroend','!macro CheckPayloadDirectories') + $directoryChecks.ToArray() + @('!macroend','!macro CheckPayloadAccess') + $access.ToArray() + @('!macroend','!macro VerifyRemoved') + $removed.ToArray() + @('!macroend') | Set-Content -LiteralPath $manifest -Encoding utf8BOM
$sizeKiB = [math]::Max(1,[math]::Ceiling(($files | Measure-Object Length -Sum).Sum / 1KB))
$icon = Join-Path $projectRoot 'src/Inklane/Assets/inklane.ico'
# Use the application's own icon for the wizard artwork as well as the executable.
Add-Type -AssemblyName System.Drawing.Common
$wizardBitmap = Join-Path $buildDirectory 'wizard.bmp'
$art = [Drawing.Bitmap]::new(328,628,[Drawing.Imaging.PixelFormat]::Format24bppRgb)
$graphics = [Drawing.Graphics]::FromImage($art)
$brand = [Drawing.Icon]::new($icon,128,128)
try {
    $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#EEEAF7'))
    $graphics.DrawIcon($brand,[Drawing.Rectangle]::new(84,204,160,160))
    $art.Save($wizardBitmap,[Drawing.Imaging.ImageFormat]::Bmp)
} finally { $brand.Dispose(); $graphics.Dispose(); $art.Dispose() }
New-Item -ItemType Directory -Path (Split-Path -Parent $output) -Force | Out-Null
& $compiler '/V2' '/INPUTCHARSET' 'UTF8' "/DVERSION=$Version" "/DNUMERIC_VERSION=$numericVersion" "/DSIZE_KIB=$sizeKiB" "/DPAYLOAD_MANIFEST=$manifest" "/DPRODUCT_ICON=$icon" "/DWIZARD_BITMAP=$wizardBitmap" "/DSETUP_OUTPUT=$output" (Join-Path $projectRoot 'installer/Inklane.nsi')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $output)) { throw 'NSIS setup compilation failed.' }
$digest = (Get-FileHash -LiteralPath $output).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($output + '.sha256') -Value ($digest + '  ' + [IO.Path]::GetFileName($output)) -Encoding ascii
Write-Host "Created $output"
