// AppTrace
// File:    CompareDialog.cs
// Purpose: The Compare dialog: choose two traces (and whether to normalise GUIDs).
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Drawing;
using System.Windows.Forms;

namespace AppTrace;

/// <summary>Asks for the two traces to compare.</summary>
/// <summary>Asks for the two traces to compare. Choices are remembered between runs through <see cref="AppSettings"/>.</summary>
public sealed class CompareDialog : Form
{

    /// <summary>Trace A (file or run folder).</summary>
    readonly TextBox _pathA = new TextBox();

    /// <summary>Trace B (file or run folder).</summary>
    readonly TextBox _pathB = new TextBox();

    /// <summary>When ticked, differing GUIDs are reported instead of being normalised away.</summary>
    readonly CheckBox _keepGuids = new CheckBox { Text = "Don't normalise GUIDs (treat different GUIDs as differences)", AutoSize = true };

    /// <summary>Builds the dialog. The layout is table-based and DPI-aware so nothing is clipped on high-resolution screens.</summary>
    /// <param name="initialA">Trace to pre-fill as A (for example the trace in the current tab); null to use the remembered one.</param>
    public CompareDialog(string? initialA = null)
    {
        // Scale with the screen's DPI so nothing is clipped on high-resolution displays
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Compare two traces";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 250);

        AppSettings saved = AppSettings.Current;

        _pathA.Text = string.IsNullOrEmpty(initialA) ? saved.CompareA : initialA;
        _pathB.Text = saved.CompareB;
        _keepGuids.Checked = saved.KeepGuids;

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

        for (int row = 0; row < 4; row = row + 1)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        AddRow(table, 0, "Trace A", _pathA);
        AddRow(table, 1, "Trace B", _pathB);

        var hint = new Label
        {
            Text = "Pick each run's trace.jsonl (or paste its folder). meta.json beside it supplies the machine details.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 44,
            Margin = new Padding(3, 8, 3, 3)
        };

        table.Controls.Add(hint, 0, 2);

        table.SetColumnSpan(hint, 3);

        _keepGuids.Margin = new Padding(3, 3, 3, 3);

        table.Controls.Add(_keepGuids, 0, 3);

        table.SetColumnSpan(_keepGuids, 3);

        var ok = new Button { Text = "Compare", AutoSize = true, MinimumSize = new Size(88, 30) };

        var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(88, 30), DialogResult = DialogResult.Cancel };

        ok.Click += OnCompare;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8)
        };

        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        // Fill first, then the edge-docked control
        Controls.Add(table);
        Controls.Add(buttons);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>Trace A as entered (trimmed, quotes removed).</summary>
    public string PathA
    {
        get
        {
            return _pathA.Text.Trim().Trim('"');
        }
    }

    /// <summary>Trace B as entered (trimmed, quotes removed).</summary>
    public string PathB
    {
        get
        {
            return _pathB.Text.Trim().Trim('"');
        }
    }

    /// <summary>True if GUIDs should not be normalised.</summary>
    public bool KeepGuids
    {
        get
        {
            return _keepGuids.Checked;
        }
    }

    /// <summary>Adds a label, a text box and a Browse button for one trace as a row of the layout table.</summary>
    /// <param name="table">The layout table.</param>
    /// <param name="row">Row index.</param>
    /// <param name="label">Caption.</param>
    /// <param name="box">The text box.</param>
    void AddRow(TableLayoutPanel table, int row, string label, TextBox box)
    {
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 6) }, 0, row);

        box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        box.Margin = new Padding(3, 6, 3, 6);

        table.Controls.Add(box, 1, row);

        var browse = new Button { Text = "Browse...", AutoSize = true, MinimumSize = new Size(88, 28), Anchor = AnchorStyles.Left, Margin = new Padding(3, 3, 3, 3) };

        browse.Click += (sender, e) =>
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "Choose " + label,
                Filter = "Trace (trace.jsonl)|trace.jsonl|JSON lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
                CheckFileExists = true,
                InitialDirectory = AppSettings.Current.StartFolder(AppSettings.CapturesFolderOf(box.Text.Trim().Trim('"')))
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    box.Text = dialog.FileName;
                }
            }
        };

        table.Controls.Add(browse, 2, row);
    }

    /// <summary>Checks that both traces exist and differ, remembers the choices, and closes with OK.</summary>
    void OnCompare(object? sender, EventArgs e)
    {
        if (!Exists(PathA) || !Exists(PathB))
        {
            Ui.ShowError(this, "Choose two existing traces.");

            return;
        }

        if (string.Equals(Path.GetFullPath(TraceFile.ResolveTracePath(PathA)), Path.GetFullPath(TraceFile.ResolveTracePath(PathB)), StringComparison.OrdinalIgnoreCase))
        {
            Ui.ShowError(this, "Trace A and Trace B are the same file. Choose two different runs.");

            return;
        }

        AppSettings saved = AppSettings.Current;

        saved.CompareA = PathA;
        saved.CompareB = PathB;
        saved.KeepGuids = KeepGuids;

        string captures = AppSettings.CapturesFolderOf(PathB);

        if (captures.Length > 0)
        {
            saved.OpenFolder = captures;
        }

        saved.Save();

        DialogResult = DialogResult.OK;
    }

    /// <summary>Whether a trace file exists at the path (a folder means its <c>trace.jsonl</c>).</summary>
    /// <param name="path">File or run folder.</param>
    static bool Exists(string path)
    {
        return path.Length > 0 && File.Exists(TraceFile.ResolveTracePath(path));
    }

}
