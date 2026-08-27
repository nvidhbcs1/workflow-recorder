namespace WorkflowRecorder.Core;

public sealed record VerificationResult(bool Passed, IReadOnlyList<string> Checks, IReadOnlyList<string> Failures);

public static class SessionVerifier
{
    public static VerificationResult VerifyBrowserEvaluation(string sessionDirectory)
    {
        var checks = new List<string>();
        var failures = new List<string>();
        var session = SessionStore.LoadSession(sessionDirectory);
        var events = SessionStore.LoadEvents(sessionDirectory);
        var notes = events.Where(item => item.Type == "annotation").Select(item => item.Note ?? string.Empty).ToList();
        var visitNotes = notes.Where(note => note.StartsWith("Visited website:", StringComparison.OrdinalIgnoreCase)).ToList();
        var openNotes = notes.Where(note => note.StartsWith("Opened new tab:", StringComparison.OrdinalIgnoreCase)).ToList();
        var closeNotes = notes.Where(note => note.StartsWith("Closed tab:", StringComparison.OrdinalIgnoreCase)).ToList();
        var screenshotEvents = events.Where(item => item.Screenshot is not null).ToList();
        var htmlPath = Path.Combine(sessionDirectory, "documentation.html");
        var skillFiles = Directory.Exists(Path.Combine(sessionDirectory, "generated-skill"))
            ? Directory.GetFiles(Path.Combine(sessionDirectory, "generated-skill"), "SKILL.md", SearchOption.AllDirectories)
            : [];

        Check(session.EndedAtUtc is not null, "Recording session was completed.", checks, failures);
        Check(visitNotes.Count >= 5, $"Visited at least five websites ({visitNotes.Count} recorded).", checks, failures);
        Check(openNotes.Count >= 4, $"Used multiple tabs ({openNotes.Count + 1} total tabs recorded).", checks, failures);
        Check(closeNotes.Count >= 5, $"Closed webpages one by one ({closeNotes.Count} closures recorded).", checks, failures);
        Check(screenshotEvents.Count >= 10, $"Captured graphical evidence ({screenshotEvents.Count} screenshots).", checks, failures);
        Check(screenshotEvents.All(item => File.Exists(Path.Combine(sessionDirectory, item.Screenshot!.Replace('/', Path.DirectorySeparatorChar)))), "Every referenced screenshot exists.", checks, failures);
        Check(File.Exists(htmlPath) && File.ReadAllText(htmlPath).Contains("<img", StringComparison.OrdinalIgnoreCase), "HTML documentation exists and contains graphical steps.", checks, failures);
        Check(skillFiles.Length > 0 && File.ReadAllText(skillFiles[0]).Contains("## Workflow", StringComparison.Ordinal), "Generated SKILL.md exists and contains workflow instructions.", checks, failures);

        return new VerificationResult(failures.Count == 0, checks, failures);
    }

    private static void Check(bool condition, string message, List<string> checks, List<string> failures)
    {
        if (condition)
        {
            checks.Add(message);
        }
        else
        {
            failures.Add(message);
        }
    }
}
