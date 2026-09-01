[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [Alias('Host')]
    [ValidateSet('Codex', 'ClaudeCode')]
    [string]$Client,

    [string]$DestinationRoot,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$source = switch ($Client) {
    'Codex' { Join-Path $repositoryRoot 'codex-skill\windows-workflow-recorder' }
    'ClaudeCode' { Join-Path $repositoryRoot 'claude-code\skills\windows-workflow-recorder' }
}
$userRoot = if ($DestinationRoot) { [System.IO.Path]::GetFullPath($DestinationRoot) } else { $env:USERPROFILE }
$targetRoot = switch ($Client) {
    'Codex' { Join-Path $userRoot '.codex\skills' }
    'ClaudeCode' { Join-Path $userRoot '.claude\skills' }
}
$target = Join-Path $targetRoot 'windows-workflow-recorder'

if (-not (Test-Path -LiteralPath $source)) {
    throw "The $Client skill package was not found at $source."
}
if ((Test-Path -LiteralPath $target) -and -not $Force) {
    throw "$target already exists. Review it first, or rerun with -Force to update its files."
}

if ($PSCmdlet.ShouldProcess($target, "Install $Client Windows Workflow Recorder skill")) {
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $targetRoot -Recurse -Force
    [pscustomobject]@{
        Client = $Client
        InstalledSkill = $target
        Invocation = if ($Client -eq 'Codex') { '$windows-workflow-recorder <request>' } else { '/windows-workflow-recorder <request>' }
    } | Format-List
}
