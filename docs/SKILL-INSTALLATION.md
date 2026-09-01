# Install the Workflow Recorder skill

The Windows recorder application and its skill are separate. Install or build the recorder first so `WorkflowRecorder.Cli.exe` is available, then install the matching skill package.

The recorder's CLI is the default for normal recording requests. The GUI opens only when the user explicitly requests the GUI or its target preview.

## Codex

Codex skills use a dollar-sign mention, not a slash command.

```powershell
.\scripts\Install-WorkflowRecorderSkill.ps1 -Client Codex
```

Start a new Codex task, then use:

```text
$windows-workflow-recorder start recording Telegram
```

`/windows-workflow-recorder` is **not** a Codex skill command. Codex can also select the skill automatically when a request clearly matches its description, but the `$` mention is the deterministic way to select it.

## Claude Code

Claude Code supports skills from `~/.claude/skills/<name>/SKILL.md`. This project ships a Claude-ready copy so it can be called consistently as a slash command.

```powershell
.\scripts\Install-WorkflowRecorderSkill.ps1 -Client ClaudeCode
```

Restart Claude Code or start a new session, then use:

```text
/windows-workflow-recorder start recording Telegram
```

For native Windows or Git Bash, `~/.claude` maps to `%USERPROFILE%\.claude`. For WSL, run the installer from the Linux checkout so it installs into that environment's `~/.claude/skills/` folder.

## Verify the installation

Run the post-install evaluator after installing the skill. It checks the actual installed folder, required resources, the client’s explicit invocation syntax, the CLI-first rule, and a runnable CLI.

```powershell
.\tests\Test-SkillInstallation.ps1 -Client Codex -Mode Installed -RecorderHome C:\path\to\dist\win-x64
.\tests\Test-SkillInstallation.ps1 -Client ClaudeCode -Mode Installed -RecorderHome C:\path\to\dist\win-x64
```

Use `-Client Both` when both skills are installed. The CI workflow also runs an isolated-install version of this evaluator so a packaging regression fails before release.

For a final live check, start a new session and invoke the command shown above with a harmless target such as `notepad`. The agent should use `WorkflowRecorder.Cli.exe record-controlled ...` (and request an explicit target if none was given); it should not open `WorkflowRecorder.App.exe` unless you request the GUI.

The skill does not install the recorder executable. If it cannot find the CLI, set `WORKFLOW_RECORDER_HOME` to the extracted `dist/win-x64` folder or provide that folder to the agent.
