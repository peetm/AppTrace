// AppTrace
// File:    CompareView.cs
// Purpose: Comparison results: per-category difference panels and the summary tab.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AppTrace;

/// <summary>The differences for one category (processes, DLLs, registry or files).</summary>
public sealed class DiffPanel : UserControl
{

    /// <summary>Text of the "show everything" entry in the kind drop-down.</summary>
    const string _all = "(all differences)";

    /// <summary>All differences in this panel's category.</summary>
    readonly List<DiffRow> _rows;

    /// <summary>Grid of the filtered differences.</summary>
    readonly VirtualGrid _grid = new VirtualGrid();

    /// <summary>Shows the selected difference in full.</summary>
    readonly TextBox _detail = new TextBox();

    /// <summary>Filter by kind of difference.</summary>
    readonly ComboBox _kind;

    /// <summary>Free-text filter over item, A and B text.</summary>
    readonly TextBox _search = new TextBox { Width = 260, Margin = new Padding(2, 3, 6, 3) };

    /// <summary>Shows how many differences match the filters.</summary>
    readonly Label _count = new Label { AutoSize = true, Margin = new Padding(12, 7, 2, 0) };

    /// <summary>Delays filtering until typing pauses.</summary>
    readonly System.Windows.Forms.Timer _searchTimer = new System.Windows.Forms.Timer { Interval = 300 };

    /// <summary>The differences that pass the filters.</summary>
    List<DiffRow> _view = new List<DiffRow>();

    /// <summary>Builds the panel for one category.</summary>
    /// <param name="result">The comparison.</param>
    /// <param name="category">Process, Image, Registry or File.</param>
    public DiffPanel(CompareResult result, string category)
    {
        Dock = DockStyle.Fill;

        _rows = result.Rows.Where(r => r.Category == category).ToList();

        var kinds = new List<string> { _all, DiffKind.OnlyA, DiffKind.OnlyB, DiffKind.Differs };

        if (category == "Registry")
        {
            kinds.Add(DiffKind.Data);
        }

        if (category == "File")
        {
            kinds.Add(DiffKind.Bytes);
        }

        _kind = Ui.MakeCombo(190, kinds.ToArray());

        string nameA = "A: " + result.TraceA.Meta.MachineName;

        string nameB = "B: " + result.TraceB.Meta.MachineName;

        _grid.AddColumn("kind", "Difference", 160);
        _grid.AddColumn("item", category == "Registry" ? "Key / value" : "Item", 520);
        _grid.AddColumn("a", nameA, 260);
        _grid.AddColumn("b", nameB, 260);

        _detail.Multiline = true;
        _detail.ReadOnly = true;
        _detail.ScrollBars = ScrollBars.Vertical;
        _detail.Font = new Font("Consolas", 9f);
        _detail.Dock = DockStyle.Fill;

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4, 0, 4, 0)
        };

        bar.Controls.Add(Ui.MakeLabel("Show"));
        bar.Controls.Add(_kind);
        bar.Controls.Add(Ui.MakeLabel("Find"));
        bar.Controls.Add(_search);
        bar.Controls.Add(_count);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };

        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_detail);

        Controls.Add(split);
        Controls.Add(bar);

        Ui.SplitAt(split, 0.78);

        _grid.CellValueNeeded += OnCellValueNeeded;
        _grid.CellFormatting += OnCellFormatting;
        _grid.SelectionChanged += (sender, e) => ShowDetail(nameA, nameB);

        _kind.SelectedIndexChanged += (sender, e) => ApplyFilter();

        _search.TextChanged += (sender, e) =>
        {
            _searchTimer.Stop();
            _searchTimer.Start();
        };

        _searchTimer.Tick += (sender, e) =>
        {
            _searchTimer.Stop();

            ApplyFilter();
        };

        ApplyFilter();
    }

    /// <summary>The differences currently shown (after filtering).</summary>
    public IReadOnlyList<DiffRow> VisibleRows
    {
        get
        {
            return _view;
        }
    }

    /// <summary>Rebuilds the visible list from the kind and text filters and refreshes the grid.</summary>
    void ApplyFilter()
    {
        string kind = _kind.SelectedItem as string ?? _all;

        string text = _search.Text.Trim();

        IEnumerable<DiffRow> query = _rows;

        if (kind != _all)
        {
            query = query.Where(r => r.Kind == kind);
        }

        if (text.Length > 0)
        {
            query = query.Where(r => r.Item.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                                  || r.A.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0
                                  || r.B.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        _view = query.ToList();

        _grid.RowCount = 0;

        _grid.RowCount = _view.Count;

        _grid.Invalidate();

        _count.Text = _view.Count.ToString("N0") + " of " + _rows.Count.ToString("N0");
    }

    /// <summary>Supplies the text for a grid cell on demand (virtual mode).</summary>
    void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count)
        {
            return;
        }

        DiffRow r = _view[e.RowIndex];

        switch (e.ColumnIndex)
        {
            case 0: e.Value = r.Kind; break;
            case 1: e.Value = r.Item; break;
            case 2: e.Value = r.A; break;
            case 3: e.Value = r.B; break;
        }
    }

    /// <summary>Colours each row by its kind of difference.</summary>
    void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count || e.CellStyle == null)
        {
            return;
        }

        e.CellStyle.ForeColor = Color.Black;

        switch (_view[e.RowIndex].Kind)
        {
            case DiffKind.OnlyA: e.CellStyle.BackColor = Color.FromArgb(255, 232, 214); break;
            case DiffKind.OnlyB: e.CellStyle.BackColor = Color.FromArgb(220, 235, 255); break;
            case DiffKind.Differs: e.CellStyle.BackColor = Color.FromArgb(255, 247, 205); break;
            case DiffKind.Data: e.CellStyle.BackColor = Color.FromArgb(238, 228, 250); break;
            case DiffKind.Bytes: e.CellStyle.BackColor = Color.FromArgb(222, 245, 222); break;
        }
    }

    /// <summary>Fills the detail pane with the selected difference.</summary>
    /// <param name="nameA">Heading for trace A (machine name).</param>
    /// <param name="nameB">Heading for trace B (machine name).</param>
    void ShowDetail(string nameA, string nameB)
    {
        int index = _grid.CurrentCell?.RowIndex ?? -1;

        if (index < 0 || index >= _view.Count)
        {
            _detail.Text = "";

            return;
        }

        DiffRow r = _view[index];

        var sb = new StringBuilder();

        sb.AppendLine(r.Kind);
        sb.AppendLine(r.Item);
        sb.AppendLine();
        sb.AppendLine(nameA + "   " + r.A);
        sb.AppendLine(nameB + "   " + r.B);

        _detail.Text = sb.ToString();
    }

}

/// <summary>The result of comparing two traces: a summary tab and one tab per category.</summary>
/// <summary>The result of comparing two traces: a summary tab and one tab per category.</summary>
public sealed class CompareView : UserControl
{

    /// <summary>The comparison being shown.</summary>
    readonly CompareResult _result;

    /// <summary>The per-category panels (kept so they can be reached later).</summary>
    readonly List<DiffPanel> _panels = new List<DiffPanel>();

    /// <summary>Builds the tabs, header and export buttons.</summary>
    /// <param name="result">The comparison to show.</param>
    public CompareView(CompareResult result)
    {
        _result = result;

        Dock = DockStyle.Fill;

        var tabs = new TabControl { Dock = DockStyle.Fill };

        tabs.TabPages.Add(SummaryPage());

        AddCategory(tabs, "Processes", "Process");
        AddCategory(tabs, "DLLs / images", "Image");
        AddCategory(tabs, "Registry", "Registry");
        AddCategory(tabs, "Files", "File");

        var header = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 58,
            Padding = new Padding(8, 6, 8, 0),
            Text = Describe("A", result.TraceA) + "\r\n" + Describe("B", result.TraceB)
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 0, 4, 0) };

        var saveReport = new Button { Text = "Save report (Markdown)...", AutoSize = true };

        var saveCsv = new Button { Text = "Export differences to CSV...", AutoSize = true };

        saveReport.Click += OnSaveReport;

        saveCsv.Click += OnSaveCsv;

        buttons.Controls.Add(saveReport);
        buttons.Controls.Add(saveCsv);

        Controls.Add(tabs);
        Controls.Add(buttons);
        Controls.Add(header);
    }

    /// <summary>One-line description of a trace for the header.</summary>
    /// <param name="label">"A" or "B".</param>
    /// <param name="trace">The trace.</param>
    static string Describe(string label, LoadedTrace trace)
    {
        RunMeta m = trace.Meta;

        return label + ": " + m.MachineName + "  |  " + m.OsVersion + "  |  user " + m.UserName + "  |  "
               + trace.Events.Count.ToString("N0") + " events" + (m.Dropped > 0 ? "  |  " + m.Dropped.ToString("N0") + " DROPPED" : "")
               + "  |  " + trace.DisplayName;
    }

    /// <summary>Adds a tab for one category, with its difference count in the title.</summary>
    /// <param name="tabs">The tab control.</param>
    /// <param name="title">Tab title.</param>
    /// <param name="category">Process, Image, Registry or File.</param>
    void AddCategory(TabControl tabs, string title, string category)
    {
        int count = _result.Rows.Count(r => r.Category == category);

        var page = new TabPage(title + " (" + count.ToString("N0") + ")");

        var panel = new DiffPanel(_result, category);

        _panels.Add(panel);

        page.Controls.Add(panel);

        tabs.TabPages.Add(page);
    }

    /// <summary>Builds the Summary tab: difference counts plus event counts per operation.</summary>
    TabPage SummaryPage()
    {
        var page = new TabPage("Summary");

        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Top,
            Height = 150,
            Font = new Font("Consolas", 9.5f),
            Text = SummaryText()
        };

        var grid = new VirtualGrid();

        grid.AddColumn("op", "Operation", 260);
        grid.AddColumn("a", "A: " + _result.TraceA.Meta.MachineName, 120);
        grid.AddColumn("b", "B: " + _result.TraceB.Meta.MachineName, 120);

        grid.CellValueNeeded += (sender, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _result.Counts.Count)
            {
                return;
            }

            CountRow row = _result.Counts[e.RowIndex];

            e.Value = e.ColumnIndex == 0 ? row.Operation : (e.ColumnIndex == 1 ? row.A.ToString("N0") : row.B.ToString("N0"));
        };

        grid.RowCount = _result.Counts.Count;

        page.Controls.Add(grid);
        page.Controls.Add(text);

        return page;
    }

    /// <summary>Text summarising how many differences of each kind were found per category.</summary>
    string SummaryText()
    {
        var sb = new StringBuilder();

        sb.AppendLine("Differences found (machine-specific names, SIDs" + (_result.NormalizedGuids ? " and GUIDs" : "") + " were normalised first):");
        sb.AppendLine();

        foreach (string category in new[] { "Process", "Image", "Registry", "File" })
        {
            var rows = _result.Rows.Where(r => r.Category == category).ToList();

            sb.AppendLine(category.PadRight(10)
                          + rows.Count(r => r.Kind == DiffKind.OnlyA).ToString("N0").PadLeft(8) + " only on A"
                          + rows.Count(r => r.Kind == DiffKind.OnlyB).ToString("N0").PadLeft(8) + " only on B"
                          + rows.Count(r => r.Kind == DiffKind.Differs).ToString("N0").PadLeft(8) + " behaved differently"
                          + (category == "Registry" ? rows.Count(r => r.Kind == DiffKind.Data).ToString("N0").PadLeft(8) + " different data" : "")
                          + (category == "File" ? rows.Count(r => r.Kind == DiffKind.Bytes).ToString("N0").PadLeft(8) + " different bytes written" : ""));
        }

        return sb.ToString();
    }

    /// <summary>Saves the comparison as a Markdown report.</summary>
    void OnSaveReport(object? sender, EventArgs e)
    {
        using (var dialog = new SaveFileDialog { Filter = "Markdown (*.md)|*.md", FileName = "compare-report.md" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                File.WriteAllText(dialog.FileName, _result.ToMarkdown(5000));
            }
            catch (Exception ex)
            {
                Ui.ShowError(this, "Could not write the file: " + ex.Message);
            }
        }
    }

    /// <summary>Saves every difference to a CSV file.</summary>
    void OnSaveCsv(object? sender, EventArgs e)
    {
        using (var dialog = new SaveFileDialog { Filter = "CSV file (*.csv)|*.csv", FileName = "differences.csv" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                var rows = new List<string[]> { new[] { "Category", "Difference", "Item", "A: " + _result.TraceA.Meta.MachineName, "B: " + _result.TraceB.Meta.MachineName } };

                rows.AddRange(_result.Rows.Select(r => new[] { r.Category, r.Kind, r.Item, r.A, r.B }));

                Ui.WriteCsv(dialog.FileName, rows);
            }
            catch (Exception ex)
            {
                Ui.ShowError(this, "Could not write the file: " + ex.Message);
            }
        }
    }

}
