using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms;
using WorkflowRecorder.Core;

namespace WorkflowRecorder.Cli;

internal static class Program
{
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                PrintHelp();
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "record-browser-test" => await RecordBrowserTestAsync(args[1..]),
                "record-controlled" => await RecordControlledAsync(args[1..]),
                "generate-html" => GenerateHtml(args[1..]),
                "generate-skill" => GenerateSkill(args[1..]),
                "verify-browser-test" => Verify(args[1..]),
                "inspect" => Inspect(args[1..]),
                _ => Unknown(args[0])
            };
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"ERROR: {error.Message}");
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }

    private static async Task<int> RecordControlledAsync(string[] args)
    {
        var output = Option(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "evaluation", "sessions");
        var name = Option(args, "--name") ?? "Controlled recorded workflow";
        var target = ResolveTargetWindow(args);
        var targetScreen = ResolveTargetScreen(args);
        if (target is not null && targetScreen is not null)
        {
            throw new ArgumentException("Choose either a window target or --target-screen, not both.");
        }
        var settings = new RecorderSettings
        {
            OutputDirectory = Path.GetFullPath(output),
            CaptureScreenshots = true,
            CaptureWindowChanges = true,
            ScreenshotDelayMilliseconds = 450,
            TargetWindowHandle = target is null ? null : (long)target.Handle,
            RequireTargetWindow = target is not null,
            CaptureTargetKind = targetScreen is null ? CaptureTargetKind.Window : CaptureTargetKind.Screen,
            TargetScreenDeviceName = targetScreen?.DeviceName
        };

        using var engine = new RecorderEngine();
        engine.StatusChanged += (_, message) => Console.WriteLine(message);
        engine.EventRecorded += (_, item) => Console.WriteLine($"[{item.Step:000}] {item.Type}/{item.Action} {item.Note ?? item.Shortcut ?? item.Control?.Name}");
        var session = engine.Start(name, settings);
        Console.WriteLine($"SESSION={session.DirectoryPath}");
        Console.WriteLine(targetScreen is not null
            ? $"TARGET=Entire screen | {targetScreen.DeviceName} | {targetScreen.Bounds.Width}x{targetScreen.Bounds.Height}"
            : target is null
                ? "TARGET=automatic foreground (use --target-process, --target-title, --target-handle, or --target-screen to prevent unrelated captures)"
                : $"TARGET={target.ProcessName} | {target.Title} | HWND=0x{target.Handle:X}");
        Console.WriteLine("READY: use 'note <description>', 'image <absolute-path><TAB><description>', or 'stop'.");

        while (await Console.In.ReadLineAsync() is { } line)
        {
            if (line.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            if (line.StartsWith("note ", StringComparison.OrdinalIgnoreCase) && line.Length > 5)
            {
                engine.AddAnnotation(line[5..].Trim(), true);
                await Task.Delay(600);
                continue;
            }
            if (line.StartsWith("image ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line[6..].Split('\t', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    await engine.AddAnnotationWithScreenshotAsync(parts[1].Trim(), parts[0].Trim());
                    continue;
                }
            }
            Console.WriteLine("IGNORED: use 'note <description>', 'image <absolute-path><TAB><description>', or 'stop'.");
        }

        var completed = await engine.StopAsync() ?? throw new InvalidOperationException("Recording did not complete.");
        var html = HtmlDocumentationGenerator.Generate(completed.DirectoryPath);
        Console.WriteLine($"HTML={html}");
        return 0;
    }

    private static async Task<int> RecordBrowserTestAsync(string[] args)
    {
        var output = Option(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "evaluation", "sessions");
        var name = Option(args, "--name") ?? "Access five websites with multiple tabs and close them one by one";
        var browserPath = Option(args, "--browser") ?? FindEdge();
        var waitSeconds = int.TryParse(Option(args, "--wait-seconds"), out var parsed) ? Math.Clamp(parsed, 1, 20) : 3;
        var urls = new[]
        {
            "https://example.com",
            "https://www.wikipedia.org",
            "https://github.com",
            "https://www.microsoft.com",
            "https://openai.com"
        };

        var settings = new RecorderSettings
        {
            OutputDirectory = Path.GetFullPath(output),
            CaptureScreenshots = true,
            CaptureWindowChanges = true,
            ScreenshotDelayMilliseconds = 450
        };

        var browserProcessName = Path.GetFileNameWithoutExtension(browserPath);
        var existingBrowserProcesses = Process.GetProcessesByName(browserProcessName)
            .Select(item => item.Id)
            .ToHashSet();

        using var engine = new RecorderEngine();
        engine.StatusChanged += (_, message) => Console.WriteLine(message);
        engine.EventRecorded += (_, item) => Console.WriteLine($"[{item.Step:000}] {item.Type}/{item.Action} {item.Note ?? item.Shortcut ?? item.Control?.Name}");
        var session = engine.Start(name, settings);
        Console.WriteLine($"SESSION={session.DirectoryPath}");

        var profile = Path.Combine(session.DirectoryPath, "browser-profile");
        Directory.CreateDirectory(profile);
        var launch = new ProcessStartInfo
        {
            FileName = browserPath,
            UseShellExecute = false,
            Arguments = $"--user-data-dir=\"{profile}\" --new-window --no-first-run --disable-first-run-ui --disable-sync \"{urls[0]}\""
        };
        using var browser = Process.Start(launch) ?? throw new InvalidOperationException("Unable to start the browser.");
        var window = await WaitForWindowAsync(browser, browserProcessName, existingBrowserProcesses, TimeSpan.FromSeconds(25));
        engine.PinTargetWindow(window);
        Win32WindowService.SetForegroundWindow(window);
        await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
        engine.AddAnnotation($"Visited website: {urls[0]}", true);
        await PauseAsync(waitSeconds);

        for (var index = 1; index < urls.Length; index++)
        {
            Win32WindowService.SetForegroundWindow(window);
            SendKeys.SendWait("^t");
            await Task.Delay(600);
            engine.AddAnnotation($"Opened new tab: {urls[index]}", true);
            await Task.Delay(700);
            SendKeys.SendWait(urls[index]);
            SendKeys.SendWait("{ENTER}");
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
            engine.AddAnnotation($"Visited website: {urls[index]}", true);
            await PauseAsync(waitSeconds);
        }

        for (var index = urls.Length - 1; index >= 0; index--)
        {
            Win32WindowService.SetForegroundWindow(window);
            engine.AddAnnotation($"Closing tab: {urls[index]}", true);
            await PauseAsync(1);
            SendKeys.SendWait("^w");
            await Task.Delay(900);
            engine.AddAnnotation($"Closed tab: {urls[index]}", true);
            await PauseAsync(1);
        }

        var completed = await engine.StopAsync() ?? throw new InvalidOperationException("Recording did not complete.");
        var html = HtmlDocumentationGenerator.Generate(completed.DirectoryPath);
        var skill = SkillGenerator.Generate(completed.DirectoryPath, "browser-five-site-tab-workflow");
        var verification = SessionVerifier.VerifyBrowserEvaluation(completed.DirectoryPath);
        var verificationPath = Path.Combine(completed.DirectoryPath, "verification.json");
        File.WriteAllText(verificationPath, JsonSerializer.Serialize(verification, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"HTML={html}");
        Console.WriteLine($"SKILL={skill}");
        Console.WriteLine($"VERIFICATION={verificationPath}");
        foreach (var check in verification.Checks) Console.WriteLine($"PASS: {check}");
        foreach (var failure in verification.Failures) Console.WriteLine($"FAIL: {failure}");
        return verification.Passed ? 0 : 2;
    }

    private static int GenerateHtml(string[] args)
    {
        Require(args, 1, "generate-html <session-directory> [output-html]");
        Console.WriteLine(HtmlDocumentationGenerator.Generate(args[0], args.Length > 1 ? args[1] : null));
        return 0;
    }

    private static int GenerateSkill(string[] args)
    {
        Require(args, 1, "generate-skill <session-directory> [skill-name] [output-directory]");
        Console.WriteLine(SkillGenerator.Generate(args[0], args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null));
        return 0;
    }

    private static int Verify(string[] args)
    {
        Require(args, 1, "verify-browser-test <session-directory>");
        var result = SessionVerifier.VerifyBrowserEvaluation(args[0]);
        foreach (var check in result.Checks) Console.WriteLine($"PASS: {check}");
        foreach (var failure in result.Failures) Console.WriteLine($"FAIL: {failure}");
        return result.Passed ? 0 : 2;
    }

    private static int Inspect(string[] args)
    {
        Require(args, 1, "inspect <session-directory>");
        var session = SessionStore.LoadSession(args[0]);
        var events = SessionStore.LoadEvents(args[0]);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            session.Id,
            session.Name,
            session.StartedAtUtc,
            session.EndedAtUtc,
            EventCount = events.Count,
            ScreenshotCount = events.Count(item => item.Screenshot is not null),
            Applications = events.Select(item => item.Application).Where(item => item is not null).Distinct().Order().ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static async Task<nint> WaitForWindowAsync(
        Process launcher,
        string processName,
        IReadOnlySet<int> existingProcessIds,
        TimeSpan timeout)
    {
        var stopAt = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < stopAt)
        {
            try
            {
                launcher.Refresh();
                if (!launcher.HasExited && launcher.MainWindowHandle != 0)
                {
                    return launcher.MainWindowHandle;
                }
            }
            catch (InvalidOperationException) { }

            foreach (var candidate in Process.GetProcessesByName(processName))
            {
                try
                {
                    candidate.Refresh();
                    if (!existingProcessIds.Contains(candidate.Id) && candidate.MainWindowHandle != 0)
                    {
                        return candidate.MainWindowHandle;
                    }
                }
                finally
                {
                    candidate.Dispose();
                }
            }

            await Task.Delay(250);
        }
        throw new TimeoutException("The browser did not create a visible window.");
    }

    private static async Task PauseAsync(int seconds) => await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, seconds)));

    private static WindowContext? ResolveTargetWindow(string[] args)
    {
        var handleText = Option(args, "--target-handle");
        if (!string.IsNullOrWhiteSpace(handleText))
        {
            if (handleText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                handleText = handleText[2..];
                if (long.TryParse(handleText, System.Globalization.NumberStyles.HexNumber, null, out var hexHandle))
                {
                    return RequireValidTarget((nint)hexHandle);
                }
            }
            else if (long.TryParse(handleText, out var decimalHandle))
            {
                return RequireValidTarget((nint)decimalHandle);
            }
            throw new ArgumentException("--target-handle must be a decimal handle or hexadecimal value beginning with 0x.");
        }

        var process = Option(args, "--target-process");
        var title = Option(args, "--target-title");
        if (process is null && title is null)
        {
            return null;
        }

        var matches = Win32WindowService.ListTopLevelWindows()
            .Where(window => process is null || string.Equals(window.ProcessName, process, StringComparison.OrdinalIgnoreCase))
            .Where(window => title is null || window.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            throw new InvalidOperationException("No visible window matched the requested target.");
        }
        if (matches.Length > 1)
        {
            var choices = string.Join(Environment.NewLine, matches.Take(8).Select(window => $"  0x{window.Handle:X}  {window.ProcessName} — {window.Title}"));
            throw new InvalidOperationException($"More than one window matched. Add --target-title or use --target-handle:{Environment.NewLine}{choices}");
        }
        return matches[0];
    }

    private static WindowContext RequireValidTarget(nint handle)
    {
        var target = Win32WindowService.FromHandle(handle);
        return target.IsValid ? target : throw new InvalidOperationException("The requested target window is not available.");
    }

    private static Screen? ResolveTargetScreen(string[] args)
    {
        var requested = Option(args, "--target-screen");
        if (string.IsNullOrWhiteSpace(requested))
        {
            return null;
        }
        if (requested.Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            return Screen.PrimaryScreen ?? throw new InvalidOperationException("Windows did not report a primary screen.");
        }
        var screen = Screen.AllScreens.FirstOrDefault(candidate =>
            string.Equals(candidate.DeviceName, requested, StringComparison.OrdinalIgnoreCase) ||
            candidate.DeviceName.EndsWith(requested, StringComparison.OrdinalIgnoreCase));
        if (screen is not null)
        {
            return screen;
        }
        var available = string.Join(", ", Screen.AllScreens.Select(candidate => candidate.DeviceName));
        throw new InvalidOperationException($"No screen matched '{requested}'. Use 'primary' or one of: {available}");
    }

    private static string FindEdge()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Microsoft Edge was not found. Pass --browser with an executable path.");
    }

    private static string? Option(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }
        return null;
    }

    private static void Require(string[] args, int count, string usage)
    {
        if (args.Length < count) throw new ArgumentException($"Usage: {usage}");
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Workflow Recorder CLI

            Commands:
              record-browser-test [--output DIR] [--browser EXE] [--wait-seconds N]
              record-controlled [--output DIR] [--name NAME] [--target-process NAME] [--target-title TEXT] [--target-handle HWND] [--target-screen primary|DISPLAY]
              generate-html <session-directory> [output-html]
              generate-skill <session-directory> [skill-name] [output-directory]
              verify-browser-test <session-directory>
              inspect <session-directory>
            """);
    }
}
