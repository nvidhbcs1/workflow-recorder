# Release process

1. Start from a clean working tree and review `git status --ignored` for recordings, screenshots, browser profiles, session folders, and local packages.
2. Run the release checks:

   ```powershell
   dotnet build .\WorkflowRecorder.slnx -c Release
   dotnet run --project .\tests\WorkflowRecorder.SmokeTests -c Release
   .\scripts\Publish-Windows.ps1
   ```

3. Inspect `dist\WorkflowRecorder-win-x64.zip`; it must contain only the portable app, CLI, and public README.
4. Scan the ZIP file list for screenshots, sessions, `.user` files, `bin`, `obj`, debug symbols, and personal paths. Do not publish the ZIP if any appear.
5. Create a GitHub Release and attach the reviewed ZIP. Publish its SHA-256 checksum alongside the release.

Never use a personal test-transfer ZIP as a public release artifact.
