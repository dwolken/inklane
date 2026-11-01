[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $projectRoot 'Inklane.slnx'
if (-not (Test-Path -LiteralPath $solutionPath)) {
    $solutionPath = Join-Path $projectRoot 'Inklane.sln'
}
if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw 'The Inklane solution was not found beside scripts/.'
}
$artifactArguments = @()
if ($ArtifactsPath) { $artifactArguments = @('--artifacts-path', [System.IO.Path]::GetFullPath($ArtifactsPath)) }

Push-Location $projectRoot
try {
    & dotnet restore $solutionPath @artifactArguments --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & dotnet build $solutionPath @artifactArguments --configuration $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    # Retain each run independently so a PDF preview of an earlier fixture cannot lock this run.
    $testOutput = Join-Path $projectRoot ('.scratch/test-runs/' + [guid]::NewGuid().ToString('N'))
    & dotnet run --project (Join-Path $projectRoot 'tests/Inklane.Tests/Inklane.Tests.csproj') @artifactArguments --configuration $Configuration --no-build -- $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Regression checks failed.' }
    & dotnet run --project (Join-Path $projectRoot 'tests/Inklane.SurfaceTests/Inklane.SurfaceTests.csproj') @artifactArguments --configuration $Configuration --no-build -- (Join-Path $testOutput 'surface')
    if ($LASTEXITCODE -ne 0) { throw 'Editor surface regression checks failed.' }
    & dotnet run --project (Join-Path $projectRoot 'tests/Inklane.LifecycleTests/Inklane.LifecycleTests.csproj') @artifactArguments --configuration $Configuration --no-build -- (Join-Path $testOutput 'lifecycle')
    if ($LASTEXITCODE -ne 0) { throw 'Document lifecycle regression checks failed.' }
}
finally {
    Pop-Location
}
