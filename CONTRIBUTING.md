# Contributing

## Local setup

Install the .NET 8 SDK with Windows Desktop support, then run:

```powershell
dotnet build .\WorkflowRecorder.slnx -c Release
dotnet run --project .\tests\WorkflowRecorder.SmokeTests -c Release
```

## Privacy requirements

Do not submit real session folders, screenshots, generated HTML, browser profiles, portable binaries, API keys, credentials, or personal paths. Use synthetic fixtures only. The repository `.gitignore` excludes common generated data, but contributors must review `git status` before committing.

## Changes

- Keep ordinary text input excluded from recorder events.
- Preserve the sensitive-control and screenshot review safeguards.
- Add or update a regression test for changes to event capture, screenshot timing, documentation, or skill generation.
- Keep the app local-first; new telemetry, cloud sync, or external connections require an explicit privacy review and documentation update.

Report security issues privately as described in [SECURITY.md](SECURITY.md).
