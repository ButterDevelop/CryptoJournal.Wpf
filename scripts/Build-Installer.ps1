param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'

$repositoryRoot     = Split-Path -Parent $PSScriptRoot
$projectPath        = Join-Path $repositoryRoot 'CryptoJournal.Wpf.csproj'
$installerScript    = Join-Path $repositoryRoot 'setupGenerateScript.iss'
$publishDirectory   = Join-Path $repositoryRoot 'artifacts\publish\win-x64'
$installerDirectory = Join-Path $repositoryRoot 'artifacts\installer'
$installerPath      = Join-Path $installerDirectory "Setup_CryptoJournal_$Version.exe"
$checksumPath       = "$installerPath.sha256"
$isccPath           = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'

if (-not (Test-Path -LiteralPath $isccPath))
{
    throw "Inno Setup compiler was not found at '$isccPath'."
}

if (Test-Path -LiteralPath $publishDirectory)
{
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $installerDirectory)
{
    Remove-Item -LiteralPath $installerDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    "-p:Version=$Version" `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0)
{
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

& $isccPath `
    "/DMyAppVersion=$Version" `
    "/DMyPublishDir=$publishDirectory" `
    "/DMyOutputDir=$installerDirectory" `
    $installerScript

if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $installerPath))
{
    throw "Installer was not created at '$installerPath'."
}

$checksum = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$checksum  $(Split-Path -Leaf $installerPath)" -Encoding ascii

Write-Output $installerPath
Write-Output $checksumPath
