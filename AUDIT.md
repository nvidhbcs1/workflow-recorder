# GitHub publication preflight

Prepared: 2026-08-27

This repository copy was created from the audited public-source set only. It contains source code, tests, build scripts, public documentation, the reusable Codex skill, and GitHub Actions CI configuration.

## Deliberately excluded

- recordings, session JSON/JSONL files, screenshots, browser profiles, generated documentation, and generated skills;
- portable application builds, release ZIPs, debug symbols, and build output;
- local editor settings and developer-machine configuration; and
- personal paths, credentials, tokens, and private application data.

## Verification completed

```powershell
dotnet build .\WorkflowRecorder.slnx -c Release
dotnet run --project .\tests\WorkflowRecorder.SmokeTests\WorkflowRecorder.SmokeTests.csproj -c Release
```

Both checks passed with zero build warnings or errors. The release script was also checked: its ZIP contains only the portable app, CLI, and public README.

## Before publishing

1. Inspect `git diff --cached` and this file one more time.
2. Replace the GitHub badge placeholder in `README.md` with the real owner/repository name.
3. Configure the Git remote for the new empty GitHub repository.
4. Create the first commit and push it.
5. Create a release only after following `docs/RELEASE.md`.
