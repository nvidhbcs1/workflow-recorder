[CmdletBinding()]
param(
    [string]$RecorderHome,
    [string]$SessionsRoot
)

$candidateRoots = [System.Collections.Generic.List[string]]::new()
if ($RecorderHome) { $candidateRoots.Add($RecorderHome) }
if ($env:WORKFLOW_RECORDER_HOME) { $candidateRoots.Add($env:WORKFLOW_RECORDER_HOME) }

$cursor = (Get-Location).Path
while ($cursor) {
    $candidateRoots.Add((Join-Path $cursor 'dist\win-x64'))
    $candidateRoots.Add((Join-Path $cursor 'workflow-recorder\dist\win-x64'))
    $parent = Split-Path -Parent $cursor
    if (-not $parent -or $parent -eq $cursor) { break }
    $cursor = $parent
}

$candidateRoots.Add((Join-Path $env:LOCALAPPDATA 'WorkflowRecorder\bin'))
$candidateRoots.Add((Join-Path $env:ProgramFiles 'Workflow Recorder'))

$cliPath = $null
$appPath = $null
foreach ($candidateRoot in $candidateRoots | Select-Object -Unique) {
    if (-not $candidateRoot) { continue }
    $cliCandidates = @(
        (Join-Path $candidateRoot 'cli\WorkflowRecorder.Cli.exe'),
        (Join-Path $candidateRoot 'WorkflowRecorder.Cli.exe')
    )
    $appCandidates = @(
        (Join-Path $candidateRoot 'app\WorkflowRecorder.App.exe'),
        (Join-Path $candidateRoot 'WorkflowRecorder.App.exe')
    )
    if (-not $cliPath) { $cliPath = $cliCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
    if (-not $appPath) { $appPath = $appCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1 }
    if ($cliPath -and $appPath) { break }
}

if (-not $SessionsRoot) {
    $workspaceSessions = Join-Path (Get-Location).Path 'workflow-recorder\evaluation\sessions'
    $SessionsRoot = if (Test-Path -LiteralPath $workspaceSessions) {
        $workspaceSessions
    } else {
        Join-Path $env:LOCALAPPDATA 'WorkflowRecorder\Sessions'
    }
}

$latestSession = $null
if (Test-Path -LiteralPath $SessionsRoot) {
    $latestSession = Get-ChildItem -LiteralPath $SessionsRoot -Directory |
        Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'session.json') } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

[pscustomobject]@{
    CliPath = $cliPath
    AppPath = $appPath
    SessionsRoot = $SessionsRoot
    LatestSession = $latestSession
} | ConvertTo-Json
