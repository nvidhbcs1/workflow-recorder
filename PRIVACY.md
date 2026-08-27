# Privacy

Workflow Recorder is designed for explicit, user-started local recording. It is not a background monitoring service or continuous-video recorder.

## Data the recorder can store locally

- timestamps, application/process names, and window titles;
- meaningful mouse actions and selected UI Automation metadata;
- compact cursor paths before a meaningful click;
- semantic keys and shortcuts such as Enter, Tab, Esc, arrows, function keys, and modifier shortcuts;
- screenshots taken after configured meaningful actions; and
- annotations the recorder user enters.

## Data intentionally excluded

- ordinary typed characters and Backspace events;
- password values; and
- continuous screen video, audio, camera input, clipboard contents, or browser history.

The recorder does not guarantee that screenshots are free of sensitive data. A visible password manager, message, document, notification, or another overlapping application can still appear in an image. Review every recording before generating documentation, sharing it, or publishing it.

## Storage and network behavior

Sessions are stored in a folder selected by the recorder user. The application has no built-in account, telemetry, cloud-sync, or automatic-upload mechanism. The optional browser evaluation is an explicit test command that opens public websites and should not be used with private browser profiles.

## Safe operating guidance

- Record a specific target window whenever possible instead of an entire display.
- Keep confidential applications out of the selected display and use the excluded-process list for sensitive apps.
- Stop recording before opening passwords, personal messages, financial records, health data, or authentication prompts.
- Treat generated HTML and skills as private until their screenshots and text have been reviewed.
- Do not commit session folders, screenshots, or generated documentation to a public repository.
