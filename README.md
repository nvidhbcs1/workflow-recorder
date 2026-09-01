# Workflow Recorder for Windows

<p align="center">
  <img src="assets/workflow-recorder-logo.png" width="220" alt="Workflow Recorder logo">
</p>

Workflow Recorder captures enough context to turn a Windows workflow into useful documentation and a reusable Codex skill without recording a continuous video or saving ordinary typed text.

It records:

- foreground application and window titles;
- mouse clicks plus Windows UI Automation control metadata when available;
- compact cursor paths before meaningful clicks;
- semantic command keys and shortcuts (including Enter and Alt+Tab), but not ordinary typed text;
- optional screenshots of the active window after important actions;
- human annotations that explain intent.

Each session is stored locally as a portable folder containing `session.json`, `events.jsonl`, `screenshots/`, graphical `documentation.html`, and an optional generated skill.

## Quick start

1. Open `WorkflowRecorder.App.exe`.
2. Enter a descriptive workflow name and choose an output folder.
3. Choose a **Capture target**: either a specific app window or **Entire screen** for the monitor you want to document. Confirm the choice in **Current target** and the live **Target preview**; select **Refresh windows** if the app was opened after the recorder.
4. Select **Start recording**. The recorder can minimize itself.
5. Perform the workflow. Use **Add milestone note** at important milestones to capture intent, such as “Exported the report as PDF.” While a GUI recording is active, press `Ctrl+Alt+M` from any app to open the same note dialog immediately.
6. Return to the recorder and select **Stop recording**.
7. Generate and review the HTML documentation. Remove or redact private screenshots before sharing.
8. Generate a skill only after checking that the timeline describes a repeatable, safe procedure.

The recorder is not a keylogger and does not capture continuous video. It records only semantic keys such as Enter, Tab, Esc, arrows, function keys, and shortcuts; it omits ordinary typing and Backspace. Command-key screenshots wait longer by default so their resulting state has time to render. Screenshots can still expose visible private information, so always review them.

## Command line

```powershell
WorkflowRecorder.Cli.exe inspect C:\path\to\session
WorkflowRecorder.Cli.exe generate-html C:\path\to\session
WorkflowRecorder.Cli.exe generate-skill C:\path\to\session my-workflow
WorkflowRecorder.Cli.exe verify-browser-test C:\path\to\session
WorkflowRecorder.Cli.exe record-controlled --target-process ChatGPT --name "My browser workflow"
WorkflowRecorder.Cli.exe record-controlled --target-screen primary --name "Whole-display workflow"
```

The built-in evaluation records five public websites in separate Edge tabs and closes them individually:

```powershell
WorkflowRecorder.Cli.exe record-browser-test --output C:\WorkflowRecorder\Sessions --wait-seconds 3
```

This uses a temporary Edge profile inside the session folder and does not alter the normal Edge profile.

## Privacy and limits

- Ordinary typed text is intentionally excluded. Shortcuts such as `Ctrl+T` and `Ctrl+W` are recorded. `Ctrl+Alt+M` is reserved by an active GUI recording to open the milestone-note dialog, and is not stored as a workflow action.
- Password fields reported by UI Automation are marked sensitive and do not receive screenshots.
- Add sensitive apps to `ExcludedProcesses` in `RecorderSettings` when integrating the core library.
- UI Automation provides semantic controls in many Win32, WPF, and Chromium surfaces, but not every app exposes useful names or automation IDs. Screenshots and annotations fill those gaps.
- A GUI session is locked to the selected capture target. A window target follows its Windows handle on any monitor; an entire-screen target captures everything visible on the selected display.
- Handle-based rendering can capture many covered windows. Some GPU-accelerated apps do not support it; the recorder labels a `target-screen-fallback` capture, which requires the target to be visible and unobstructed.
- Controlled browser automation can provide an exact browser-native screenshot with `image <absolute-path><TAB><description>`; these are labelled `provided-image` in the event data and HTML.
- Replay is review-driven: the generated skill is an instruction artifact, not an unattended macro.

## Build and package

Requires the .NET 8 SDK with Windows Desktop support.

```powershell
dotnet build .\WorkflowRecorder.slnx -c Release
dotnet run --project .\tests\WorkflowRecorder.SmokeTests -c Release
.\scripts\Publish-Windows.ps1
```

Publishing produces self-contained `win-x64` application and CLI folders under `dist`. Copy those folders to another Windows 10/11 x64 PC; the target PC does not need a separate .NET installation.

## Architecture

- `WorkflowRecorder.Core`: low-level input hooks, window metadata, UI Automation inspection, screenshots, session storage, documentation, skills, and verification.
- `WorkflowRecorder.App`: simple Windows Forms interface for interactive recording.
- `WorkflowRecorder.Cli`: artifact generation, inspection, verification, and deterministic browser evaluation.
- `WorkflowRecorder.SmokeTests`: offline end-to-end artifact test.

This is a Windows alternative inspired by Computer History and Record & Replay concepts, not an OpenAI product and not a drop-in implementation of the macOS feature.

## Open-source and security notes

Workflow Recorder is MIT licensed. Read [PRIVACY.md](PRIVACY.md) before recording and [SECURITY.md](SECURITY.md) before reporting a vulnerability. This repository intentionally excludes recordings, screenshots, session files, generated artifacts, portable ZIPs, and developer-machine settings.

The recorder has no built-in account, telemetry, cloud sync, or automatic upload feature. The optional browser evaluation intentionally opens public websites when the user runs that explicit command; it is separate from normal recording.

## Use with Codex

The repository ships separate ready-to-install packages for Codex and Claude Code. Both choose `WorkflowRecorder.Cli.exe` by default for recording; the GUI is used only when explicitly requested.

- **Codex:** install `codex-skill/windows-workflow-recorder` under `C:\Users\<your-user>\.codex\skills\`, then invoke it with `$windows-workflow-recorder <request>`. It is not a `/windows-workflow-recorder` slash command.
- **Claude Code:** install `claude-code/skills/windows-workflow-recorder` under `~/.claude/skills/`, then invoke it with `/windows-workflow-recorder <request>`.

Use [`docs/SKILL-INSTALLATION.md`](docs/SKILL-INSTALLATION.md) or `scripts/Install-WorkflowRecorderSkill.ps1` for the exact commands and a post-install check.

## Project hygiene

- Never commit a real recording, screenshot, generated HTML, or session folder.
- Never commit a `.zip`, `dist/`, `bin/`, `obj/`, `.vs/`, or `*.user` file.
- Publish portable binaries only as a reviewed GitHub Release attachment made by `scripts/Publish-Windows.ps1`.
- See [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/RELEASE.md](docs/RELEASE.md) for the public release process.
