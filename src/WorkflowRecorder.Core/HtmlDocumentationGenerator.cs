using System.Net;
using System.Text;

namespace WorkflowRecorder.Core;

public static class HtmlDocumentationGenerator
{
    public static string Generate(string sessionDirectory, string? outputPath = null)
    {
        var session = SessionStore.LoadSession(sessionDirectory);
        var events = SessionStore.LoadEvents(sessionDirectory);
        outputPath ??= Path.Combine(sessionDirectory, "documentation.html");
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(outputDirectory);

        var documented = events
            .Where(item => item.Type is "annotation" or "mouse" or "keyboard" or "pointer" or "session")
            .ToList();
        var screenshots = documented.Count(item => item.Screenshot is not null);
        var applications = documented
            .Select(item => item.Application)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        html.AppendLine($"<title>{E(session.Name)} — Workflow documentation</title>");
        html.AppendLine("<style>");
        html.AppendLine(Css);
        html.AppendLine("</style></head><body>");
        html.AppendLine($"<header><div class=\"eyebrow\">Recorded workflow</div><h1>{E(session.Name)}</h1>");
        html.AppendLine($"<p class=\"lede\">A graphical, step-by-step record captured on {E(session.HostName)}. Review every step before reusing it.</p></header>");
        html.AppendLine($"<main data-event-count=\"{events.Count}\" data-screenshot-count=\"{screenshots}\">");
        html.AppendLine("<section class=\"summary\">");
        html.AppendLine(Metric("Started", session.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")));
        html.AppendLine(Metric("Recorded events", events.Count.ToString()));
        html.AppendLine(Metric("Graphical steps", screenshots.ToString()));
        html.AppendLine(Metric("Applications", applications.Count.ToString()));
        html.AppendLine("</section>");

        if (applications.Count > 0)
        {
            html.AppendLine($"<section class=\"apps\"><h2>Applications observed</h2><p>{string.Join(" · ", applications.Select(E))}</p></section>");
        }

        html.AppendLine("<section><div class=\"section-title\"><div><div class=\"eyebrow\">Timeline</div><h2>What happened</h2></div><p>Red markers indicate captured click locations.</p></div>");
        if (documented.Count == 0)
        {
            html.AppendLine("<div class=\"empty\">No documentable steps were recorded.</div>");
        }
        else
        {
            foreach (var item in documented)
            {
                html.AppendLine(RenderStep(item, sessionDirectory, outputDirectory));
            }
        }
        html.AppendLine("</section>");
        html.AppendLine("<section class=\"privacy\"><h2>Privacy note</h2><p>This document was generated locally. Ordinary typed text and password values are not recorded. Screenshots may still contain visible private information; review them before sharing.</p></section>");
        html.AppendLine("</main><footer>Generated locally by Workflow Recorder.</footer></body></html>");

        File.WriteAllText(outputPath, html.ToString(), new UTF8Encoding(false));
        return Path.GetFullPath(outputPath);
    }

    private static string RenderStep(WorkflowEvent item, string sessionDirectory, string outputDirectory)
    {
        var title = item.Type switch
        {
            "annotation" => item.Note ?? "Recorded note",
            "mouse" => $"{Cap(item.Action)} on {ControlLabel(item)}",
            "keyboard" when string.Equals(item.Shortcut, "Enter", StringComparison.OrdinalIgnoreCase) => "Submit the focused input with Enter",
            "keyboard" => $"Use {item.Shortcut ?? "the recorded keyboard action"}",
            "pointer" => "Moved the pointer toward the next control",
            "session" => item.Action == "start" ? "Recording started" : "Recording stopped",
            _ => Cap(item.Action)
        };
        var explanation = Explain(item);
        var time = item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
        var metadata = new List<string> { time };
        if (!string.IsNullOrWhiteSpace(item.Application)) metadata.Add(item.Application!);
        if (!string.IsNullOrWhiteSpace(item.WindowTitle)) metadata.Add(item.WindowTitle!);

        var builder = new StringBuilder();
        builder.AppendLine($"<article class=\"step\" id=\"step-{item.Step}\">");
        builder.AppendLine($"<div class=\"step-copy\"><div class=\"step-number\">{item.Step:00}</div><div><div class=\"meta\">{string.Join(" <span>•</span> ", metadata.Select(E))}</div><h3>{E(title)}</h3><p>{E(explanation)}</p>");
        if (item.Control is not null || !string.IsNullOrWhiteSpace(item.ScreenshotSource))
        {
            builder.AppendLine("<dl>");
            if (item.Control is not null)
            {
                AddDefinition(builder, "Control", item.Control.Name);
                AddDefinition(builder, "Type", item.Control.ControlType);
                AddDefinition(builder, "Automation ID", item.Control.AutomationId);
                AddDefinition(builder, "Framework", item.Control.FrameworkId);
            }
            AddDefinition(builder, "Screenshot source", item.ScreenshotSource);
            builder.AppendLine("</dl>");
        }
        builder.AppendLine("</div></div>");

        if (!string.IsNullOrWhiteSpace(item.Screenshot))
        {
            var absolute = Path.Combine(sessionDirectory, item.Screenshot.Replace('/', Path.DirectorySeparatorChar));
            var relative = Path.GetRelativePath(outputDirectory, absolute).Replace('\\', '/');
            builder.AppendLine($"<figure><a href=\"{A(relative)}\"><img loading=\"lazy\" src=\"{A(relative)}\" alt=\"Screenshot for step {item.Step}: {A(title)}\"></a><figcaption>Visual state after step {item.Step}</figcaption></figure>");
        }
        builder.AppendLine("</article>");
        return builder.ToString();
    }

    private static string Explain(WorkflowEvent item) => item.Type switch
    {
        "annotation" => item.Note ?? "A note was added to this point in the workflow.",
        "mouse" when item.Control?.Name is not null => $"In {item.Application ?? "the active application"}, the user activated the {item.Control.ControlType ?? "control"} named “{item.Control.Name}”.",
        "mouse" => $"In {item.Application ?? "the active application"}, the user performed a {item.Action} at screen position ({item.X}, {item.Y}).",
        "keyboard" when string.Equals(item.Shortcut, "Enter", StringComparison.OrdinalIgnoreCase) => $"Press Enter to submit the focused input in {item.Application ?? "the active application"}. Verify the expected result before continuing.",
        "keyboard" => $"Use {item.Shortcut ?? "the recorded keyboard action"} in {item.Application ?? "the active application"}.",
        "pointer" when item.CursorPath is not null => $"The pointer moved from ({item.CursorPath.StartX}, {item.CursorPath.StartY}) to ({item.CursorPath.EndX}, {item.CursorPath.EndY}) over {item.CursorPath.DurationMilliseconds} ms before the next action.",
        "session" => item.Note ?? "The recording state changed.",
        _ => item.Note ?? "An interaction was recorded."
    };

    private static string ControlLabel(WorkflowEvent item) =>
        item.Control?.Name ?? item.Control?.ControlType ?? $"screen position ({item.X}, {item.Y})";

    private static string Metric(string label, string value) =>
        $"<div class=\"metric\"><span>{E(label)}</span><strong>{E(value)}</strong></div>";

    private static void AddDefinition(StringBuilder builder, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"<dt>{E(key)}</dt><dd>{E(value)}</dd>");
        }
    }

    private static string Cap(string? value) => string.IsNullOrWhiteSpace(value)
        ? "Recorded action"
        : char.ToUpperInvariant(value[0]) + value[1..];

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
    private static string A(string value) => WebUtility.HtmlEncode(value);

    private const string Css = """
        :root{--ink:#15212b;--muted:#61707c;--line:#dce4e9;--paper:#fff;--soft:#f4f7f8;--accent:#d9352b;--blue:#246b8e}
        *{box-sizing:border-box}body{margin:0;background:linear-gradient(180deg,#eaf2f5 0,#f8fafb 380px);color:var(--ink);font:16px/1.55 "Segoe UI",system-ui,sans-serif}
        header,main,footer{width:min(1120px,calc(100% - 40px));margin:auto}header{padding:72px 0 32px}.eyebrow{text-transform:uppercase;letter-spacing:.16em;font-weight:700;font-size:.75rem;color:var(--blue)}h1{font-size:clamp(2.35rem,6vw,4.6rem);line-height:1.02;margin:.18em 0}.lede{font-size:1.15rem;color:var(--muted);max-width:760px}
        .summary{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin:20px 0 34px}.metric,.apps,.privacy,.empty{background:rgba(255,255,255,.88);border:1px solid var(--line);border-radius:16px;padding:18px}.metric span{display:block;color:var(--muted);font-size:.82rem}.metric strong{font-size:1.18rem}.apps{margin-bottom:48px}.apps h2,.privacy h2{margin:0 0 6px}.apps p,.privacy p{margin:0;color:var(--muted)}
        .section-title{display:flex;justify-content:space-between;align-items:end;gap:24px;margin-bottom:18px}.section-title h2{font-size:2rem;margin:0}.section-title p{color:var(--muted);margin:0}
        .step{display:grid;grid-template-columns:minmax(300px,.9fr) minmax(360px,1.1fr);gap:24px;background:var(--paper);border:1px solid var(--line);border-radius:20px;padding:24px;margin:0 0 18px;box-shadow:0 12px 34px rgba(34,70,88,.07)}.step-copy{display:flex;gap:18px}.step-number{flex:0 0 42px;height:42px;border-radius:50%;display:grid;place-items:center;background:var(--ink);color:white;font-weight:800}.meta{color:var(--muted);font-size:.78rem;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:470px}.meta span{padding:0 3px}.step h3{font-size:1.35rem;line-height:1.2;margin:8px 0}.step p{color:#43525d}.step dl{display:grid;grid-template-columns:max-content 1fr;gap:5px 12px;font-size:.84rem}.step dt{color:var(--muted)}.step dd{margin:0;overflow-wrap:anywhere}
        figure{margin:0;align-self:center}figure img{display:block;width:100%;max-height:520px;object-fit:contain;background:#0c1115;border-radius:12px;border:1px solid #b9c6ce}figcaption{color:var(--muted);font-size:.78rem;margin-top:6px}.privacy{margin:42px 0}footer{padding:10px 0 50px;color:var(--muted);font-size:.85rem}
        @media(max-width:850px){.summary{grid-template-columns:repeat(2,1fr)}.step{grid-template-columns:1fr}.section-title{align-items:start;flex-direction:column}}
        @media(max-width:520px){header,main,footer{width:min(100% - 24px,1120px)}.summary{grid-template-columns:1fr}.step{padding:16px}.meta{max-width:260px}}
        @media print{body{background:white}.step{break-inside:avoid;box-shadow:none}.step img{max-height:400px}}
        """;
}
