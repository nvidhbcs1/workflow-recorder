# Security policy

## Supported versions

Security fixes are made on the latest `main` branch and the latest published release.

## Reporting a vulnerability

Do not publish a suspected vulnerability in a public issue. After this project is hosted, use the repository's private security-advisory channel. Until then, contact the maintainer through the private channel listed in the release notes.

Include the affected version, Windows version, a minimal reproduction, impact, and whether a recording/screenshot or untrusted UI content is involved. Do not include real sessions, screenshots, credentials, or personal data.

## Security boundaries

- Recording is explicit: the user chooses a target and starts/stops it.
- The app is local-first and has no automatic upload or cloud account flow.
- Ordinary typed text is intentionally not recorded; semantic command keys are.
- Password controls reported by UI Automation are marked sensitive and do not receive screenshots.
- Screenshots are the main residual privacy risk. Visible content can be sensitive even when key input is excluded.
- Generated skills and Computer Use handoffs are review artifacts, not unattended macros. Sending, publishing, deleting, permission, and payment actions must require confirmation at execution time.

## Maintainer release checklist

Before publishing a release, run the checks in [docs/RELEASE.md](docs/RELEASE.md), confirm that no private session/artifact is staged, and inspect the final ZIP contents.
