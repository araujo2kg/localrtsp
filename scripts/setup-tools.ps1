[CmdletBinding()]
param(
    [ValidateSet('all', 'mediamtx', 'ffmpeg')]
    [string]$Only = 'all'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-VerifiedToolArchive
{
    <#
    .SYNOPSIS
    Downloads a tool archive when missing and verifies its SHA-256 hash.
    #>
    param(
        [string]$ToolName,
        [pscustomobject]$Definition,
        [string]$ToolsDirectory
    )

    $downloadTimeoutSeconds = 300
    $archivePath = Join-Path $ToolsDirectory "$ToolName-$($Definition.version).zip"

    if (-not (Test-Path -LiteralPath $archivePath))
    {
        Write-Host "Downloading $ToolName $($Definition.version)..."
        Invoke-WebRequest -UseBasicParsing -Uri $Definition.url -OutFile $archivePath -TimeoutSec $downloadTimeoutSeconds
    }

    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash

    if ($archiveHash -ne $Definition.sha256)
    {
        throw "Checksum mismatch for $archivePath. Remove that archive and investigate before retrying."
    }

    return $archivePath
}

function Install-StreamingTool
{
    <#
    .SYNOPSIS
    Extracts a verified tool with its upstream licenses and documentation.
    #>
    param(
        [string]$ToolName,
        [pscustomobject]$Definition,
        [string]$ToolsDirectory,
        [string]$ManifestPath
    )

    $archivePath = Get-VerifiedToolArchive -ToolName $ToolName -Definition $Definition -ToolsDirectory $ToolsDirectory
    $extractionDirectory = Join-Path $ToolsDirectory "$ToolName-$($Definition.version)-extracted"
    $installationDirectory = Join-Path $ToolsDirectory $ToolName

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractionDirectory -Force
    New-Item -ItemType Directory -Path $installationDirectory -Force | Out-Null

    $contentsDirectory = $extractionDirectory

    if ($ToolName -eq 'ffmpeg')
    {
        # FFmpeg archives contain a versioned root folder; MediaMTX files are at the archive root.
        $archiveRoot = Get-ChildItem -LiteralPath $extractionDirectory -Directory | Select-Object -First 1
        $contentsDirectory = $archiveRoot.FullName
    }

    foreach ($item in Get-ChildItem -LiteralPath $contentsDirectory -Force)
    {
        Copy-Item -LiteralPath $item.FullName -Destination $installationDirectory -Recurse -Force
    }

    Copy-Item -LiteralPath $ManifestPath -Destination (Join-Path $ToolsDirectory 'provenance.json') -Force
    Write-Host "Verified and installed $ToolName $($Definition.version) to $installationDirectory"
}

$repositoryDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath = Join-Path $PSScriptRoot 'tools-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$toolsDirectory = Join-Path $repositoryDirectory 'tools'

New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null

foreach ($toolName in @('mediamtx', 'ffmpeg'))
{
    if ($Only -ne 'all' -and $Only -ne $toolName)
    {
        continue
    }

    Install-StreamingTool -ToolName $toolName -Definition $manifest.$toolName -ToolsDirectory $toolsDirectory -ManifestPath $manifestPath
}

Write-Host 'No PATH changes or global installation were made.'