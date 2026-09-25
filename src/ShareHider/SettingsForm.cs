using ShareHider.Core;

namespace ShareHider;

/// <summary>Edits the hide list, detection, minimize and hotkey settings.</summary>
internal sealed class SettingsForm : Form
{
    private readonly ListBox _apps = new() { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended };
    private readonly TextBox _exeInput = new() { Width = 220, PlaceholderText = "e.g. outlook.exe" };
    private readonly ComboBox _running = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _autoDetect = new() { Text = "Auto-detect screen sharing", AutoSize = true };
    private readonly CheckBox _minimize = new() { Text = "Also minimize hidden apps (restored afterwards)", AutoSize = true };
    private readonly TextBox _hotkey = new() { Width = 160 };
    private readonly AppSettings _original;

    public SettingsForm(AppSettings settings)
    {
        _original = settings;
        Result = settings;

        Text = "ShareHider settings";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 480);
        MinimumSize = new Size(480, 420);

        _apps.Items.AddRange([.. settings.HiddenApps]);
        _autoDetect.Checked = settings.AutoDetect;
        _minimize.Checked = settings.MinimizeWindows;
        _hotkey.Text = settings.Hotkey;
        LoadRunningApps();

        var addTyped = new Button { Text = "Add", AutoSize = true };
        addTyped.Click += (_, _) => AddApp(_exeInput.Text, clearInput: true);
        var addRunning = new Button { Text = "Add", AutoSize = true };
        addRunning.Click += (_, _) => AddApp(_running.SelectedItem as string, clearInput: false);
        var remove = new Button { Text = "Remove selected", AutoSize = true };
        remove.Click += (_, _) => RemoveSelected();
        _apps.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
        _exeInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                AddApp(_exeInput.Text, clearInput: true);
                e.SuppressKeyPress = true;
            }
        };

        var ok = new Button { Text = "OK", AutoSize = true };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = cancel;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < 7; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        layout.Controls.Add(new Label { Text = "Hide these apps from the taskbar while sharing:", AutoSize = true });
        layout.Controls.Add(_apps);
        layout.Controls.Add(Row(remove));
        layout.Controls.Add(Row(new Label { Text = "Exe name:", AutoSize = true, Anchor = AnchorStyles.Left }, _exeInput, addTyped));
        layout.Controls.Add(Row(new Label { Text = "Running app:", AutoSize = true, Anchor = AnchorStyles.Left }, _running, addRunning));
        layout.Controls.Add(_autoDetect);
        layout.Controls.Add(_minimize);
        layout.Controls.Add(Row(new Label { Text = "Toggle hotkey:", AutoSize = true, Anchor = AnchorStyles.Left }, _hotkey,
            new Label { Text = "e.g. Ctrl+Alt+H", AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = SystemColors.GrayText }));
        layout.Controls.Add(Row(ok, cancel));
        Controls.Add(layout);
    }

    /// <summary>The edited settings. Only meaningful after the dialog returns OK.</summary>
    public AppSettings Result { get; private set; }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        row.Controls.AddRange(controls);
        return row;
    }

    private void LoadRunningApps()
    {
        var names = WindowEnumerator.VisibleWindows()
            .Where(w => w.ProcessName.Length > 0 && WindowEnumerator.HasTaskbarButton(w.Handle))
            .Select(w => w.ProcessName)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
        _running.Items.AddRange(names);
        if (names.Length > 0)
        {
            _running.SelectedIndex = 0;
        }
    }

    private void AddApp(string? input, bool clearInput)
    {
        if (!ExeName.TryNormalize(input, out var name))
        {
            MessageBox.Show(this, "Enter an exe file name such as outlook.exe (no folder path).",
                "ShareHider", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_apps.Items.Count >= AppSettings.MaxHiddenApps)
        {
            MessageBox.Show(this, $"The list is limited to {AppSettings.MaxHiddenApps} apps.",
                "ShareHider", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_apps.Items.Contains(name))
        {
            _apps.Items.Add(name);
        }

        if (clearInput)
        {
            _exeInput.Clear();
        }
    }

    private void RemoveSelected()
    {
        foreach (var item in _apps.SelectedItems.Cast<object>().ToList())
        {
            _apps.Items.Remove(item);
        }
    }

    private void Accept()
    {
        if (!Hotkey.TryParse(_hotkey.Text, out var hotkey))
        {
            MessageBox.Show(this,
                "The hotkey needs Ctrl, Alt or Win plus a letter, digit or F1-F24, for example Ctrl+Alt+H.",
                "ShareHider", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Result = new AppSettings
        {
            HiddenApps = _apps.Items.Cast<string>().ToList(),
            AutoDetect = _autoDetect.Checked,
            MinimizeWindows = _minimize.Checked,
            Hotkey = hotkey.ToString(),
            CustomSignatures = _original.CustomSignatures,
        }.Normalized();
        DialogResult = DialogResult.OK;
    }
}
