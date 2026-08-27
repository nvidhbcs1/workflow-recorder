using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using WorkflowRecorder.Core;

var artifactsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts"));
Directory.CreateDirectory(artifactsRoot);
RunSemanticInputRegression();
RunScreenCaptureRegression(artifactsRoot);
RunWindowCaptureRegression(artifactsRoot);
var settings = new RecorderSettings
{
    OutputDirectory = artifactsRoot,
    CaptureScreenshots = true,
    CaptureWindowChanges = false
};

string sessionDirectory;
using (var store = SessionStore.Create("Smoke test <five sites>", settings))
{
    sessionDirectory = store.Session.DirectoryPath;
    store.Append(new WorkflowEvent { Type = "session", Action = "start", Note = "Recording started." });

    var urls = new[]
    {
        "https://example.com",
        "https://www.wikipedia.org",
        "https://github.com",
        "https://www.microsoft.com",
        "https://openai.com"
    };

    for (var index = 0; index < urls.Length; index++)
    {
        if (index > 0)
        {
            AddGraphicalAnnotation(store, $"Opened new tab: {urls[index]}", $"Tab {index + 1} opened");
        }
        AddGraphicalAnnotation(store, $"Visited website: {urls[index]}", $"Visited {urls[index]}");
    }

    for (var index = urls.Length - 1; index >= 0; index--)
    {
        AddGraphicalAnnotation(store, $"Closed tab: {urls[index]}", $"Closed {urls[index]}");
    }

    store.Append(new WorkflowEvent { Type = "annotation", Action = "note", Note = "Literal HTML must be escaped: <script>alert('x')</script>" });
    store.Append(new WorkflowEvent { Type = "keyboard", Action = "command-key", Application = "Telegram", Shortcut = "Enter" });
    store.Append(new WorkflowEvent { Type = "session", Action = "stop", Note = "Recording stopped." });
    store.Complete();
}

var htmlPath = HtmlDocumentationGenerator.Generate(sessionDirectory);
var skillPath = SkillGenerator.Generate(sessionDirectory, "browser-five-site-tab-workflow");
var result = SessionVerifier.VerifyBrowserEvaluation(sessionDirectory);

Assert(result.Passed, "Synthetic browser evaluation should pass: " + string.Join("; ", result.Failures));
var html = File.ReadAllText(htmlPath);
Assert(html.Contains("&lt;script&gt;", StringComparison.Ordinal), "HTML must encode note content.");
Assert(!html.Contains("<script>alert", StringComparison.OrdinalIgnoreCase), "HTML must not contain injected script markup.");
Assert(html.Contains("Submit the focused input with Enter", StringComparison.Ordinal), "Enter must be described as a submit action in workflow documentation.");
var skill = File.ReadAllText(skillPath);
Assert(skill.StartsWith("---", StringComparison.Ordinal), "Generated skill needs YAML frontmatter.");
Assert(skill.Contains("## Workflow", StringComparison.Ordinal), "Generated skill needs a workflow section.");
Assert(skill.Contains("press Enter to submit the focused input", StringComparison.OrdinalIgnoreCase), "Generated skills must retain Enter as a submit action.");
Assert(SessionStore.LoadEvents(sessionDirectory).Count >= 17, "Expected a complete synthetic event timeline.");

var summaryPath = Path.Combine(sessionDirectory, "smoke-test-result.json");
File.WriteAllText(summaryPath, JsonSerializer.Serialize(new
{
    Passed = true,
    SessionDirectory = sessionDirectory,
    Html = htmlPath,
    Skill = skillPath,
    Checks = result.Checks
}, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"PASS: smoke test artifacts: {sessionDirectory}");
return 0;

static void AddGraphicalAnnotation(SessionStore store, string note, string caption)
{
    var step = store.Session.EventCount + 1;
    var screenshotPath = store.ScreenshotPathForStep(step, "synthetic");
    using var image = new Bitmap(960, 540);
    using var graphics = Graphics.FromImage(image);
    graphics.Clear(Color.FromArgb(240, 245, 248));
    using var titleFont = new Font("Segoe UI", 28, FontStyle.Bold);
    using var bodyFont = new Font("Segoe UI", 16, FontStyle.Regular);
    graphics.DrawString("Workflow Recorder smoke test", titleFont, Brushes.DarkSlateGray, 52, 64);
    graphics.DrawString(caption, bodyFont, Brushes.Black, new RectangleF(54, 150, 850, 280));
    image.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);

    store.Append(new WorkflowEvent
    {
        Type = "annotation",
        Action = "note",
        Application = "msedge",
        WindowTitle = caption,
        Note = note,
        Screenshot = store.RelativeScreenshotPath(screenshotPath)
    });
}

static void RunWindowCaptureRegression(string artifactsRoot)
{
    using var target = new Form
    {
        Text = "Workflow Recorder capture target",
        BackColor = Color.FromArgb(25, 90, 210),
        StartPosition = FormStartPosition.Manual,
        Location = new Point(80, 80),
        Size = new Size(440, 280),
        ShowInTaskbar = false
    };
    using var occluder = new Form
    {
        Text = "Occluding window",
        BackColor = Color.FromArgb(220, 35, 35),
        StartPosition = FormStartPosition.Manual,
        Location = target.Location,
        Size = target.Size,
        ShowInTaskbar = false,
        TopMost = true
    };

    target.Show();
    Application.DoEvents();
    occluder.Show();
    occluder.BringToFront();
    Application.DoEvents();

    var targetContext = Win32WindowService.FromHandle(target.Handle);
    Assert(targetContext.IsValid, "The target test window must be discoverable by handle.");
    Assert(Win32WindowService.ListTopLevelWindows().Any(window => window.Handle == target.Handle), "Window enumeration must include the target window.");
    var output = Path.Combine(artifactsRoot, "window-handle-occlusion-test.png");
    var capture = ScreenCaptureService.CaptureWindow(targetContext, output);
    Assert(capture.Success, "Capturing a target window by handle should succeed.");
    Assert(capture.Source == "window-handle", "The test must use handle-based rendering, not visible screen pixels.");

    using var result = new Bitmap(output);
    var center = result.GetPixel(result.Width / 2, result.Height / 2);
    Assert(center.B > center.R + 50, "An occluding red window must not replace the blue target in the screenshot.");

    using (var engine = new RecorderEngine())
    {
        var importSession = engine.Start("Provided image regression", new RecorderSettings
        {
            OutputDirectory = artifactsRoot,
            CaptureScreenshots = true,
            CaptureWindowChanges = false,
            ScreenshotDelayMilliseconds = 0,
            TargetWindowHandle = (long)target.Handle,
            RequireTargetWindow = true
        });
        engine.AddAnnotationWithScreenshotAsync("Exact browser-native state", output).GetAwaiter().GetResult();
        engine.StopAsync().GetAwaiter().GetResult();
        var imported = SessionStore.LoadEvents(importSession.DirectoryPath)
            .Single(item => item.Note == "Exact browser-native state");
        Assert(imported.ScreenshotSource == "provided-image", "Provided screenshots must be labelled in the event record.");
        Assert(File.Exists(Path.Combine(importSession.DirectoryPath, imported.Screenshot!.Replace('/', Path.DirectorySeparatorChar))), "Provided screenshots must be normalized into the session folder.");
    }
    occluder.Close();
    target.Close();
}

static void RunScreenCaptureRegression(string artifactsRoot)
{
    var primary = Screen.PrimaryScreen ?? throw new InvalidOperationException("A primary screen is required for the screen capture test.");
    var bounds = new RectInfo
    {
        Left = primary.Bounds.Left,
        Top = primary.Bounds.Top,
        Width = 1,
        Height = 1
    };
    var output = Path.Combine(artifactsRoot, "entire-screen-capture-test.png");
    var capture = ScreenCaptureService.CaptureScreen(bounds, output);
    Assert(capture.Success, "Capturing a selected screen should succeed.");
    Assert(capture.Source == "entire-screen", "Screen captures must be labelled as entire-screen.");
    Assert(File.Exists(output), "The screen capture image should be saved.");

    using var preview = ScreenCaptureService.CreateScreenPreview(bounds, new Size(80, 80), out var previewError);
    Assert(preview is not null, "A selected screen should produce an in-memory preview: " + previewError);
    if (preview is not null)
    {
        Assert(preview.Width <= 80 && preview.Height <= 80, "The screen preview must fit inside its requested display area.");
    }
}

static void RunSemanticInputRegression()
{
    var timestamp = DateTimeOffset.UtcNow;
    var enter = InputHookService.ClassifyKeyForRecording(Keys.Enter, false, false, false, false, timestamp);
    Assert(enter?.Shortcut == "Enter" && enter.Kind == "command-key", "Enter must be recorded as a command key.");

    var altTab = InputHookService.ClassifyKeyForRecording(Keys.Tab, false, true, false, false, timestamp);
    Assert(altTab?.Shortcut == "Alt+Tab" && altTab.Kind == "window-switch", "Alt+Tab must be recorded as a window-switch action.");

    var typedLetter = InputHookService.ClassifyKeyForRecording(Keys.A, false, false, false, false, timestamp);
    Assert(typedLetter is null, "Ordinary typed letters must not be recorded.");

    var backspace = InputHookService.ClassifyKeyForRecording(Keys.Back, false, false, false, false, timestamp);
    Assert(backspace is null, "Ordinary text editing keys must not be recorded as workflow steps.");

    Assert(InputHookService.ShouldRecordCursorPath(90, 1), "A meaningful, recent cursor path must be recorded.");
    Assert(!InputHookService.ShouldRecordCursorPath(89, 100), "Tiny cursor movements must not become workflow steps.");
    Assert(!InputHookService.ShouldRecordCursorPath(150, 5001), "Stale cursor movements must not become workflow steps.");

    var settings = new RecorderSettings { ScreenshotDelayMilliseconds = 350, CommandKeyScreenshotDelayMilliseconds = 900 };
    Assert(
        RecorderEngine.GetScreenshotDelayMilliseconds(settings, new WorkflowEvent { Type = "keyboard", Action = "command-key" }) == 900,
        "Command keys must wait for the post-action screenshot delay.");
    Assert(
        RecorderEngine.GetScreenshotDelayMilliseconds(settings, new WorkflowEvent { Type = "mouse", Action = "left-click" }) == 350,
        "Pointer actions must continue using the normal screenshot delay.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
