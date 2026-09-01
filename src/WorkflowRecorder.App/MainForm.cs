using System.Diagnostics;
using WorkflowRecorder.Core;

namespace WorkflowRecorder.App;

public sealed class MainForm : Form
{
    private readonly RecorderEngine _engine = new();
    private readonly TextBox _nameBox = new() { Text = "My workflow", Dock = DockStyle.Fill };
    private readonly TextBox _outputBox = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _targetBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 570 };
    private readonly Button _refreshTargetsButton = new() { Text = "Refresh windows", AutoSize = true };
    private readonly Label _activeTarget = new() { AutoSize = true, ForeColor = Color.FromArgb(20, 92, 123) };
    private readonly PictureBox _targetPreview = new()
    {
        Width = 260,
        Height = 110,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = Color.White,
        SizeMode = PictureBoxSizeMode.Zoom,
        Margin = new Padding(0, 0, 14, 0)
    };
    private readonly Label _previewStatus = new() { AutoSize = true, MaximumSize = new Size(330, 0), ForeColor = Color.FromArgb(82, 102, 113) };
    private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 2000 };
    private readonly WindowThumbnailPreview _windowThumbnail = new();
    private readonly CheckBox _screenshots = new() { Text = "Capture a screenshot after meaningful actions", Checked = true, AutoSize = true };
    private readonly CheckBox _cursorPaths = new() { Text = "Record meaningful cursor paths before clicks", Checked = true, AutoSize = true };
    private readonly CheckBox _commandKeys = new() { Text = "Record Enter, Tab, Esc, arrows, and app shortcuts (not typed text)", Checked = true, AutoSize = true };
    private readonly NumericUpDown _commandScreenshotDelay = new() { Minimum = 0, Maximum = 5000, Increment = 100, Value = 900, Width = 82 };
    private readonly CheckBox _minimize = new() { Text = "Minimize while recording", Checked = true, AutoSize = true };
    private readonly Button _startButton = new() { Text = "Start recording", AutoSize = true };
    private readonly Button _stopButton = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly Button _noteButton = new() { Text = "Add milestone note", AutoSize = true, Enabled = false };
    private readonly ToolTip _toolTip = new();
    private readonly Button _htmlButton = new() { Text = "Generate HTML", AutoSize = true, Enabled = false };
    private readonly Button _skillButton = new() { Text = "Generate skill", AutoSize = true, Enabled = false };
    private readonly Button _folderButton = new() { Text = "Open session folder", AutoSize = true, Enabled = false };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.FromArgb(64, 81, 91) };
    private readonly ListView _events = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
    private string? _lastSessionDirectory;

    public MainForm()
    {
        Text = "Workflow Recorder";
        Width = 900;
        Height = 820;
        MinimumSize = new Size(720, 750);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 248, 249);
        _outputBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Workflow Recorder Sessions");

        _events.Columns.Add("Step", 65);
        _events.Columns.Add("Time", 110);
        _events.Columns.Add("Application", 150);
        _events.Columns.Add("Action", 170);
        _events.Columns.Add("Detail", 360);

        BuildLayout();
        WireEvents();
        FormClosing += OnFormClosing;
        Shown += (_, _) =>
        {
            RefreshTargets();
            _previewTimer.Start();
        };
        _status.Text = "Ready. Typed text and password values are not recorded.";
    }

    private void BuildLayout()
    {
        var title = new Label
        {
            Text = "Workflow Recorder",
            Font = new Font("Segoe UI Variable Display", 24, FontStyle.Bold),
            AutoSize = true,
            ForeColor = Color.FromArgb(21, 33, 43)
        };
        var intro = new Label
        {
            Text = "Record UI actions, semantic keys, compact cursor paths, and event-triggered screenshots for documentation and reusable skills.",
            AutoSize = true,
            ForeColor = Color.FromArgb(82, 102, 113)
        };

        var fields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(0, 14, 0, 8) };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.Controls.Add(new Label { Text = "Session name", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        fields.Controls.Add(_nameBox, 1, 0);
        fields.Controls.Add(new Label { Text = "Save sessions to", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        fields.Controls.Add(_outputBox, 1, 1);
        fields.Controls.Add(new Label { Text = "Capture target", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        var targetPicker = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        targetPicker.Controls.Add(_targetBox);
        targetPicker.Controls.Add(_refreshTargetsButton);
        fields.Controls.Add(targetPicker, 1, 2);
        fields.Controls.Add(new Label { Text = "Current target", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        fields.Controls.Add(_activeTarget, 1, 3);
        fields.Controls.Add(new Label { Text = "Target preview", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        var preview = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
        preview.Controls.Add(_targetPreview);
        preview.Controls.Add(_previewStatus);
        fields.Controls.Add(preview, 1, 4);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        options.Controls.Add(_screenshots);
        options.Controls.Add(_cursorPaths);
        options.Controls.Add(_commandKeys);
        var commandDelay = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        commandDelay.Controls.Add(new Label { Text = "Wait after command keys before screenshot (ms):", AutoSize = true, Margin = new Padding(0, 4, 4, 0) });
        commandDelay.Controls.Add(_commandScreenshotDelay);
        options.Controls.Add(commandDelay);
        options.Controls.Add(_minimize);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 12, 0, 12), WrapContents = true, Margin = Padding.Empty };
        buttons.Controls.AddRange([_startButton, _stopButton, _noteButton, _htmlButton, _skillButton, _folderButton]);
        _toolTip.SetToolTip(_noteButton, "Add a labelled milestone to the workflow timeline and capture the current target.");
        _noteButton.AccessibleDescription = "Adds a labelled milestone to the workflow timeline and captures the current target.";

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(22, 18, 22, 0),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(intro, 0, 1);
        header.Controls.Add(buttons, 0, 2);
        header.Controls.Add(options, 0, 3);
        header.Controls.Add(fields, 0, 4);
        header.SizeChanged += (_, _) =>
        {
            var availableWidth = Math.Max(320, header.ClientSize.Width - header.Padding.Horizontal);
            intro.MaximumSize = new Size(availableWidth, 0);
        };

        var statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(22, 12, 22, 0) };
        statusPanel.Controls.Add(_status);
        Controls.Add(_events);
        Controls.Add(statusPanel);
        Controls.Add(header);
    }

    private void WireEvents()
    {
        _startButton.Click += (_, _) => StartRecording();
        _stopButton.Click += async (_, _) => await StopRecordingAsync();
        _noteButton.Click += (_, _) => AddNote();
        _htmlButton.Click += (_, _) => GenerateHtml();
        _skillButton.Click += (_, _) => GenerateSkill();
        _folderButton.Click += (_, _) => OpenFolder();
        _refreshTargetsButton.Click += (_, _) => RefreshTargets();
        _targetBox.SelectedIndexChanged += (_, _) => UpdateTargetLabel();
        _previewTimer.Tick += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized && !_targetBox.DroppedDown)
            {
                UpdateTargetPreview();
            }
        };
        _engine.EventRecorded += (_, item) => BeginInvoke(() => AddEventRow(item));
        _engine.StatusChanged += (_, message) => BeginInvoke(() => _status.Text = message);
        FormClosed += (_, _) =>
        {
            _previewTimer.Stop();
            _previewTimer.Dispose();
            _windowThumbnail.Dispose();
            var image = _targetPreview.Image;
            _targetPreview.Image = null;
            image?.Dispose();
        };
    }

    private void StartRecording()
    {
        try
        {
            if (_targetBox.SelectedItem is not ICaptureTargetItem selectedTarget)
            {
                throw new InvalidOperationException("Choose an application window or an entire screen to capture. Use Refresh windows if it is not listed.");
            }
            _events.Items.Clear();
            var settings = new RecorderSettings
            {
                OutputDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_outputBox.Text.Trim())),
                CaptureScreenshots = _screenshots.Checked,
                CaptureCursorPaths = _cursorPaths.Checked,
                CaptureCommandKeys = _commandKeys.Checked,
                CommandKeyScreenshotDelayMilliseconds = Decimal.ToInt32(_commandScreenshotDelay.Value)
            };
            switch (selectedTarget)
            {
                case TargetWindowItem windowTarget:
                    settings.TargetWindowHandle = (long)windowTarget.Window.Handle;
                    settings.RequireTargetWindow = true;
                    break;
                case TargetScreenItem screenTarget:
                    settings.CaptureTargetKind = CaptureTargetKind.Screen;
                    settings.TargetScreenDeviceName = screenTarget.Screen.DeviceName;
                    break;
            }
            var session = _engine.Start(_nameBox.Text, settings);
            _lastSessionDirectory = session.DirectoryPath;
            _activeTarget.Text = $"Currently capturing: {selectedTarget}";
            UpdateTargetPreview();
            SetRecordingUi(true);
            if (_minimize.Checked)
            {
                WindowState = FormWindowState.Minimized;
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Unable to start", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RefreshTargets()
    {
        var selectedKey = (_targetBox.SelectedItem as ICaptureTargetItem)?.Key;
        var ownProcessId = Environment.ProcessId;
        var windowItems = Win32WindowService.ListTopLevelWindows()
            .Where(window => window.ProcessId != ownProcessId)
            .Where(window => !window.ProcessName.StartsWith("codex-computer-use", StringComparison.OrdinalIgnoreCase))
            .Where(window => !window.Title.Contains("Computer Use Cursor Overlay", StringComparison.OrdinalIgnoreCase))
            .Select(window => new TargetWindowItem(window))
            .ToArray();
        var screenItems = Screen.AllScreens
            .Select(screen => new TargetScreenItem(screen))
            .ToArray();
        var items = screenItems.Cast<ICaptureTargetItem>().Concat(windowItems).ToArray();

        _targetBox.BeginUpdate();
        try
        {
            _targetBox.Items.Clear();
            _targetBox.Items.Add("Select a window or entire screen…");
            _targetBox.Items.AddRange(items);
            var selectedIndex = selectedKey is null
                ? 0
                : Array.FindIndex(items, item => item.Key == selectedKey);
            _targetBox.SelectedIndex = selectedKey is null
                ? 0
                : (selectedIndex >= 0 ? selectedIndex + 1 : 0);
        }
        finally
        {
            _targetBox.EndUpdate();
        }
        _status.Text = windowItems.Length == 0
            ? "No application windows found. You can still select an entire screen."
            : "Choose an exact app window or an entire screen. A window can be on any monitor or behind another window.";
        UpdateTargetLabel();
    }

    private void UpdateTargetLabel()
    {
        if (_engine.IsRecording)
        {
            return;
        }
        _activeTarget.Text = _targetBox.SelectedItem is ICaptureTargetItem target
            ? $"Ready to capture: {target}"
            : "No capture target selected.";
        UpdateTargetPreview();
    }

    private void UpdateTargetPreview()
    {
        if (_targetBox.SelectedItem is not ICaptureTargetItem target)
        {
            _windowThumbnail.Hide();
            ReplaceTargetPreview(null);
            _previewStatus.Text = "Choose a window or display to show its preview.";
            return;
        }

        Bitmap? preview;
        string? source = null;
        string? error;
        switch (target)
        {
            case TargetWindowItem windowTarget:
                if (_windowThumbnail.Show(this, _targetPreview, windowTarget.Window.Handle, out error))
                {
                    ReplaceTargetPreview(null);
                    _previewStatus.Text = $"Live preview of {target}\nUpdated {DateTime.Now:HH:mm:ss} · Windows desktop thumbnail";
                    return;
                }
                preview = ScreenCaptureService.CreateWindowPreview(windowTarget.Window, _targetPreview.Size, out source, out error);
                break;
            case TargetScreenItem screenTarget:
                _windowThumbnail.Hide();
                preview = ScreenCaptureService.CreateScreenPreview(ToRectInfo(screenTarget.Screen), _targetPreview.Size, out error);
                source = "entire-screen";
                break;
            default:
                return;
        }

        ReplaceTargetPreview(preview);
        _previewStatus.Text = preview is null
            ? $"Preview unavailable: {error ?? "Windows did not return an image."}"
            : $"Live preview of {target}\nUpdated {DateTime.Now:HH:mm:ss} · {source}";
    }

    private void ReplaceTargetPreview(Image? next)
    {
        var previous = _targetPreview.Image;
        _targetPreview.Image = next;
        previous?.Dispose();
    }

    private static RectInfo ToRectInfo(Screen screen) => new()
    {
        Left = screen.Bounds.Left,
        Top = screen.Bounds.Top,
        Width = screen.Bounds.Width,
        Height = screen.Bounds.Height
    };

    private async Task StopRecordingAsync()
    {
        try
        {
            var session = await _engine.StopAsync();
            if (session is not null)
            {
                _lastSessionDirectory = session.DirectoryPath;
            }
            SetRecordingUi(false);
            WindowState = FormWindowState.Normal;
            Activate();
            if (!string.IsNullOrWhiteSpace(_activeTarget.Text))
            {
                _activeTarget.Text = _activeTarget.Text.Replace("Currently capturing:", "Last capture target:");
            }
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Unable to stop", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddNote()
    {
        var note = PromptDialog.Show(
            this,
            "What did you just complete? This adds a labelled timeline milestone and captures the current target.",
            "Add milestone note");
        if (!string.IsNullOrWhiteSpace(note))
        {
            _engine.AddAnnotation(note.Trim(), true);
        }
    }

    private void GenerateHtml()
    {
        if (_lastSessionDirectory is null) return;
        try
        {
            var path = HtmlDocumentationGenerator.Generate(_lastSessionDirectory);
            _status.Text = $"Generated {path}";
            OpenPath(path);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Unable to generate HTML", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void GenerateSkill()
    {
        if (_lastSessionDirectory is null) return;
        try
        {
            var path = SkillGenerator.Generate(_lastSessionDirectory);
            _status.Text = $"Generated {path}";
            OpenPath(Path.GetDirectoryName(path)!);
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, "Unable to generate skill", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenFolder()
    {
        if (_lastSessionDirectory is not null)
        {
            OpenPath(_lastSessionDirectory);
        }
    }

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void SetRecordingUi(bool recording)
    {
        _startButton.Enabled = !recording;
        _stopButton.Enabled = recording;
        _noteButton.Enabled = recording;
        _nameBox.Enabled = !recording;
        _outputBox.Enabled = !recording;
        _screenshots.Enabled = !recording;
        _cursorPaths.Enabled = !recording;
        _commandKeys.Enabled = !recording;
        _commandScreenshotDelay.Enabled = !recording;
        _targetBox.Enabled = !recording;
        _refreshTargetsButton.Enabled = !recording;
        _htmlButton.Enabled = !recording && _lastSessionDirectory is not null;
        _skillButton.Enabled = !recording && _lastSessionDirectory is not null;
        _folderButton.Enabled = _lastSessionDirectory is not null;
    }

    private void AddEventRow(WorkflowEvent item)
    {
        var detail = item.Note ?? item.Control?.Name ?? item.Shortcut ??
            (item.CursorPath is { } path ? $"{path.DistancePixels}px in {path.DurationMilliseconds}ms" : item.WindowTitle) ?? string.Empty;
        var row = new ListViewItem(item.Step.ToString());
        row.SubItems.Add(item.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"));
        row.SubItems.Add(item.Application ?? string.Empty);
        row.SubItems.Add(item.Action);
        row.SubItems.Add(detail);
        _events.Items.Add(row);
        row.EnsureVisible();
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_engine.IsRecording)
        {
            _engine.Dispose();
            return;
        }

        eventArgs.Cancel = true;
        var answer = MessageBox.Show(this, "Stop and save the current recording before exiting?", "Recording active", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
        {
            await StopRecordingAsync();
            _engine.Dispose();
            FormClosing -= OnFormClosing;
            Close();
        }
    }
}

internal interface ICaptureTargetItem
{
    string Key { get; }
}

internal sealed class TargetWindowItem(WindowContext window) : ICaptureTargetItem
{
    public WindowContext Window { get; } = window;
    public string Key => $"window:{Window.Handle}";

    public override string ToString()
    {
        var title = Window.Title.Length > 80 ? Window.Title[..80] + "…" : Window.Title;
        return $"{Window.ProcessName} — {title}";
    }
}

internal sealed class TargetScreenItem(Screen screen) : ICaptureTargetItem
{
    public Screen Screen { get; } = screen;
    public string Key => $"screen:{Screen.DeviceName}";

    public override string ToString()
    {
        var primary = Screen.Primary ? "Primary display" : Screen.DeviceName;
        return $"Entire screen — {primary} ({Screen.Bounds.Width} × {Screen.Bounds.Height})";
    }
}

internal static class PromptDialog
{
    public static string? Show(IWin32Window owner, string label, string title)
    {
        using var form = new Form
        {
            Text = title,
            Width = 540,
            Height = 190,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false
        };
        var prompt = new Label { Text = label, Left = 18, Top = 18, Width = 480, AutoSize = true };
        var input = new TextBox { Left = 18, Top = 50, Width = 485 };
        var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, Left = 340, Width = 75, Top = 92 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 428, Width = 75, Top = 92 };
        form.Controls.AddRange([prompt, input, ok, cancel]);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
    }
}
