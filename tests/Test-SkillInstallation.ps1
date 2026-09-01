[CmdletBinding()]
param(
    [ValidateSet('Codex', 'ClaudeCode', 'Both')]
    [string]$Client = 'Both',

    [ValidateSet('Isolated', 'Installed')]
    [string]$Mode = 'Isolated',

    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),

    [string]$RecorderHome
)

$ErrorActionPreference = 'Stop'

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw "FAIL: $Message"
    }
}

$RepositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$installer = Join-Path $RepositoryRoot 'scripts\Install-WorkflowRecorderSkill.ps1'
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Installer was not found: $installer"
}

$clients = if ($Client -eq 'Both') { @('Codex', 'ClaudeCode') } else { @($Client) }
$temporaryRoot = $null
try {
    if ($Mode -eq 'Isolated') {
        $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('workflow-recorder-skill-eval-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
        foreach ($currentClient in $clients) {
            & $installer -Client $currentClient -DestinationRoot $temporaryRoot
        }
        $userRoot = $temporaryRoot
    }
    else {
        $userRoot = $env:USERPROFILE
    }

    foreach ($currentClient in $clients) {
        $skillRoot = switch ($currentClient) {
            'Codex' { Join-Path $userRoot '.codex\skills\windows-workflow-recorder' }
            'ClaudeCode' { Join-Path $userRoot '.claude\skills\windows-workflow-recorder' }
        }
        $skillPath = Join-Path $skillRoot 'SKILL.md'
        $locator = Join-Path $skillRoot 'scripts\Find-WorkflowRecorder.ps1'
        Assert (Test-Path -LiteralPath $skillPath) "${currentClient}: SKILL.md must exist after installation."
        Assert (Test-Path -LiteralPath $locator) "${currentClient}: the CLI locator must be installed with the skill."

        $skill = Get-Content -Raw -LiteralPath $skillPath
        Assert ($skill -match '(?m)^name:\s*windows-workflow-recorder\s*$') "${currentClient}: the installed skill must have the expected name."
        if ($currentClient -eq 'Codex') {
            Assert ($skill.Contains('$windows-workflow-recorder <request>')) 'Codex: the installed skill must document the dollar-sign invocation.'
            Assert ($skill.Contains('**CLI is the default recorder.**')) 'Codex: normal recordings must be explicitly CLI-first.'
        }
        else {
            Assert ($skill -match '(?m)^user-invocable:\s*true\s*$') 'Claude Code: the installed skill must be user-invocable.'
            Assert ($skill.Contains('/windows-workflow-recorder <request>')) 'Claude Code: the installed skill must document the slash invocation.'
            Assert ($skill.Contains('Use the local **CLI** by default.')) 'Claude Code: normal recordings must be explicitly CLI-first.'
        }
        Assert ($skill -match 'Do not open, automate, or direct the GUI') "${currentClient}: GUI use must require an explicit request."
        Write-Host "PASS: $currentClient installation has the correct invocation and CLI-first routing."
    }

    if ($RecorderHome) {
        $recorderHome = [System.IO.Path]::GetFullPath($RecorderHome)
        $cliCandidates = @(
            (Join-Path $recorderHome 'cli\WorkflowRecorder.Cli.exe'),
            (Join-Path $recorderHome 'WorkflowRecorder.Cli.exe')
        )
        $cli = $cliCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        Assert ($null -ne $cli) "A runnable WorkflowRecorder.Cli.exe was not found under $recorderHome."
        $help = & $cli help 2>&1
        Assert ($LASTEXITCODE -eq 0) 'WorkflowRecorder.Cli.exe help must succeed.'
        Assert (($help -join "`n") -match 'record-controlled') 'CLI help must expose record-controlled.'
        Write-Host "PASS: CLI is runnable and exposes record-controlled: $cli"
    }
    else {
        Write-Warning 'CLI execution was skipped because -RecorderHome was not supplied. Supply the extracted dist/win-x64 folder for a complete post-install evaluation.'
    }

    Write-Host "PASS: Workflow Recorder skill installation evaluation completed ($Mode mode)."
}
finally {
    if ($temporaryRoot -and (Test-Path -LiteralPath $temporaryRoot)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
