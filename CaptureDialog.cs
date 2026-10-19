// AppTrace
// File:    CaptureDialog.cs
// Purpose: The New capture dialog: choose the program, arguments, output folder and options.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Drawing;
using System.Windows.Forms;

namespace AppTrace;

/// <summary>Asks which program to monitor and how.</summary>
/// <summary>Asks which program to monitor and how. Choices are remembered between runs through <see cref="AppSettings"/>.</summary>
public sealed class CaptureDialog : Form
{

    /// <summary>Path of the program to capture.</summary>
    readonly TextBox _exe = new TextBox();

    /// <summary>Its command-line arguments.</summary>
    readonly TextBox _args = new TextBox();

    /// <summary>Folder in which the capture folder is created.</summary>
    readonly TextBox _output = new TextBox();

    /// <summary>Record DLL loads.</summary>
    readonly CheckBox _images = new CheckBox { Text = "Log DLL loads", AutoSize = true, Checked = true };

    /// <summary>Record every file read and write.</summary>
    readonly CheckBox _readWrite = new CheckBox { Text = "Log every file read and write (large logs)", AutoSize = true, Checked = true };

    /// <summary>Record close, cleanup and flush operations.</summary>
    readonly CheckBox _close = new CheckBox { Text = "Log close / cleanup / flush operations", AutoSize = true, Checked = true };

    /// <summary>Start the program without Administrator rights.</summary>
    readonly CheckBox _deElevate = new CheckBox { Text = "Start the program without Administrator rights (experimental)", AutoSize = true };

    /// <summary>Builds the dialog. The layout is table-based and DPI-aware so nothing is clipped on high-resolution screens.</summary>
    /// <param name="previous">Settings of the previous capture in this run (takes priority), or null to use the remembered settings.</param>
    public CaptureDialog(CaptureSettings? previous)
    {
        // Scale with the screen's DPI so nothing is clipped on high-resolution displays
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "New capture";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 320);

        AppSettings saved = AppSettings.Current;

        _exe.Text = previous?.Exe ?? saved.Program;
        _args.Text = previous?.Args ?? saved.Args;
        _output.Text = previous?.OutputRoot ?? saved.EffectiveCaptureFolder();
        _images.Checked = previous?.Options.IncludeImages ?? saved.LogImages;
        _readWrite.Checked = previous?.Options.IncludeReadWrite ?? saved.LogReadWrite;
        _close.Checked = previous?.Options.IncludeCloseCleanup ?? saved.LogCloseCleanup;
        _deElevate.Checked = previous?.DeElevate ?? saved.DeElevate;

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(12, 12, 12, 0)
        };

        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        for (int row = 0; row < 3; row = row + 1)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        AddRow(table, 0, "Program", _exe, BrowseProgram);
        AddRow(table, 1, "Arguments", _args, null);
        AddRow(table, 2, "Save to", _output, BrowseFolder);

        var options = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 10, 3, 3)
        };

        options.Controls.Add(_images);
        options.Controls.Add(_readWrite);
        options.Controls.Add(_close);
        options.Controls.Add(_deElevate);

        table.Controls.Add(options, 1, 3);

        table.SetColumnSpan(options, 2);

        var start = new Button { Text = "Start", AutoSize = true, MinimumSize = new Size(88, 30) };

        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(88, 30), DialogResult = DialogResult.Cancel };

        start.Click += OnStart;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8)
        };

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(start);

        // Fill first, then the edge-docked control
        Controls.Add(table);
        Controls.Add(buttons);

        AcceptButton = start;
        CancelButton = cancel;
    }

    /// <summary>The chosen settings (valid when the dialog result is OK).</summary>
    public CaptureSettings Settings { get; private set; } = new CaptureSettings();

    /// <returns>The folder captures go to when nothing else was chosen (remembered folder, else a <c>traces</c> folder beside the exe).</returns>
    public static string DefaultOutputRoot()
    {
        return AppSettings.Current.EffectiveCaptureFolder();
    }

    /// <summary>Adds a label, a text box and an optional Browse button as one row of the layout table.</summary>
    /// <param name="table">The layout table.</param>
    /// <param name="row">Row index.</param>
    /// <param name="label">Caption.</param>
    /// <param name="box">The text box.</param>
    /// <param name="browse">Called with the text box when Browse is pressed; null for no button.</param>
    static void AddRow(TableLayoutPanel table, int row, string label, TextBox box, Action<TextBox>? browse)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 6) }, 0, row);

        box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        box.Margin = new Padding(3, 6, 3, 6);

        table.Controls.Add(box, 1, row);

        if (browse != null)
        {
            var button = new Button { Text = "Browse...", AutoSize = true, MinimumSize = new Size(88, 28), Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 3, 3) };

            button.Click += (sender, e) => browse(box);

            table.Controls.Add(button, 2, row);
        }
    }

    /// <summary>Lets the user pick the program, starting in the folder of the current entry.</summary>
    /// <param name="box">The text box to fill in.</param>
    void BrowseProgram(TextBox box)
    {
        string start = AppSettings.FolderOf(box.Text.Trim().Trim('"'));

        using (var dialog = new OpenFileDialog
        {
            Title = "Choose the program to monitor",
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = start
        })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                box.Text = dialog.FileName;
            }
        }
    }

    /// <summary>Lets the user pick the captures folder.</summary>
    /// <param name="box">The text box to fill in.</param>
    void BrowseFolder(TextBox box)
    {
        using (var dialog = new FolderBrowserDialog { Description = "Choose where capture folders are created", SelectedPath = box.Text })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                box.Text = dialog.SelectedPath;
            }
        }
    }

    /// <summary>Checks the entries, builds <see cref="Settings"/>, remembers the choices, and closes with OK.</summary>
    void OnStart(object? sender, EventArgs e)
    {
        string exe = _exe.Text.Trim().Trim('"');

        if (!File.Exists(exe))
        {
            Ui.ShowError(this, "Choose the program to monitor.");

            return;
        }

        if (_output.Text.Trim().Length == 0)
        {
            Ui.ShowError(this, "Choose where to save the capture.");

            return;
        }

        Settings = new CaptureSettings
        {
            Exe = exe,
            Args = _args.Text.Trim(),
            OutputRoot = _output.Text.Trim(),
            DeElevate = _deElevate.Checked,
            Options = new MonitorOptions
            {
                IncludeImages = _images.Checked,
                IncludeReadWrite = _readWrite.Checked,
                IncludeCloseCleanup = _close.Checked
            }
        };

        AppSettings saved = AppSettings.Current;

        saved.Program = Settings.Exe;
        saved.Args = Settings.Args;
        saved.CaptureFolder = Settings.OutputRoot;
        saved.LogImages = Settings.Options.IncludeImages;
        saved.LogReadWrite = Settings.Options.IncludeReadWrite;
        saved.LogCloseCleanup = Settings.Options.IncludeCloseCleanup;
        saved.DeElevate = Settings.DeElevate;

        saved.Save();

        DialogResult = DialogResult.OK;
    }

}
