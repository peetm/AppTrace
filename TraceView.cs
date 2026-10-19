// AppTrace
// File:    TraceView.cs
// Purpose: Tab content that shows one captured trace: filter bar, virtual grid, detail pane, CSV export.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AppTrace;

/// <summary>Shows one captured trace in a filterable, sortable grid with a detail pane.</summary>
public sealed class TraceView : UserControl
{

    /// <summary>Text of the "no filter" entry in the drop-downs.</summary>
    const string _all = "(all)";

    /// <summary>The trace being shown.</summary>
    readonly LoadedTrace _trace;

    /// <summary>Grid of the filtered events (cell text is supplied on demand).</summary>
    readonly VirtualGrid _grid = new VirtualGrid();

    /// <summary>Shows the selected event in full.</summary>
    readonly TextBox _detail = new TextBox();

    /// <summary>Filter by category.</summary>
    readonly ComboBox _category;

    /// <summary>Filter by process name.</summary>
    readonly ComboBox _process;

    /// <summary>Filter by result: any, failures only, success only.</summary>
    readonly ComboBox _status = Ui.MakeCombo(110, "Any status", "Failures only", "Success only");

    /// <summary>Free-text filter over path, value name, value data, details and operation.</summary>
    readonly TextBox _search = new TextBox { Width = 260, Margin = new Padding(2, 3, 6, 3) };

    /// <summary>Shows how many events match the filters.</summary>
    readonly Label _count = new Label { AutoSize = true, Margin = new Padding(12, 7, 2, 0) };

    /// <summary>Delays filtering until typing pauses, so large traces stay responsive.</summary>
    readonly System.Windows.Forms.Timer _searchTimer = new System.Windows.Forms.Timer { Interval = 300 };

    /// <summary>The events that pass the filters, in display (sorted) order.</summary>
    List<EventRecord> _view = new List<EventRecord>();

    /// <summary>Index of the column the view is sorted by; -1 for capture order.</summary>
    int _sortColumn = -1;

    /// <summary>Direction of the sort.</summary>
    bool _sortAscending = true;

    /// <summary>Builds the view and shows all events.</summary>
    /// <param name="trace">The trace to display.</param>
    public TraceView(LoadedTrace trace)
    {
        _trace = trace;

        Dock = DockStyle.Fill;

        var categories = new List<string> { _all };

        categories.AddRange(trace.Events.Select(e => e.Category).Distinct().OrderBy(x => x));

        _category = Ui.MakeCombo(100, categories.ToArray());

        var processes = new List<string> { _all };

        processes.AddRange(trace.Events.Select(e => e.Process).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        _process = Ui.MakeCombo(160, processes.ToArray());

        BuildColumns();

        BuildLayout();

        _grid.CellValueNeeded += OnCellValueNeeded;

        _grid.ColumnHeaderMouseClick += OnHeaderClick;

        _grid.SelectionChanged += (sender, e) => ShowDetail();

        _grid.CellMouseDown += OnCellMouseDown;

        _category.SelectedIndexChanged += (sender, e) => ApplyFilter();
        _process.SelectedIndexChanged += (sender, e) => ApplyFilter();
        _status.SelectedIndexChanged += (sender, e) => ApplyFilter();

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

    /// <summary>The trace this view is showing.</summary>
    public LoadedTrace Trace
    {
        get
        {
            return _trace;
        }
    }

    /// <summary>Adds the grid's columns (the order matches <see cref="OnCellValueNeeded"/>).</summary>
    void BuildColumns()
    {
        _grid.AddColumn("seq", "#", 70);
        _grid.AddColumn("time", "Time (UTC)", 100);
        _grid.AddColumn("pid", "PID", 60);
        _grid.AddColumn("process", "Process", 130);
        _grid.AddColumn("category", "Category", 75);
        _grid.AddColumn("op", "Operation", 120);
        _grid.AddColumn("path", "Path / key", 440);
        _grid.AddColumn("valueName", "Value name", 150);
        _grid.AddColumn("valueKind", "Type", 80);
        _grid.AddColumn("valueData", "Value data", 260);
        _grid.AddColumn("status", "Result", 170);
        _grid.AddColumn("detail", "Details", 320);
    }

    /// <summary>Arranges the machine summary, filter bar, grid, detail pane and context menu.</summary>
    void BuildLayout()
    {
        RunMeta m = _trace.Meta;

        var info = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 38,
            Padding = new Padding(8, 6, 8, 0),
            Text = m.MachineName + "  |  " + m.OsVersion + "  |  user " + m.UserName + "\r\n"
                   + m.Exe + " " + m.Args + "  |  " + _trace.Events.Count.ToString("N0") + " events"
                   + (m.Dropped > 0 ? "  |  " + m.Dropped.ToString("N0") + " dropped" : "")
        };

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(4, 0, 4, 0)
        };

        var export = new Button { Text = "Export view to CSV...", AutoSize = true, Margin = new Padding(12, 2, 2, 2) };

        export.Click += OnExport;

        bar.Controls.Add(Ui.MakeLabel("Category"));
        bar.Controls.Add(_category);
        bar.Controls.Add(Ui.MakeLabel("Process"));
        bar.Controls.Add(_process);
        bar.Controls.Add(_status);
        bar.Controls.Add(Ui.MakeLabel("Find"));
        bar.Controls.Add(_search);
        bar.Controls.Add(_count);
        bar.Controls.Add(export);

        _detail.Multiline = true;
        _detail.ReadOnly = true;
        _detail.ScrollBars = ScrollBars.Vertical;
        _detail.Font = new Font("Consolas", 9f);
        _detail.Dock = DockStyle.Fill;

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };

        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_detail);

        // Fill first, then the edge-docked controls
        Controls.Add(split);
        Controls.Add(bar);
        Controls.Add(info);

        Ui.SplitAt(split, 0.78);

        var menu = new ContextMenuStrip();

        menu.Items.Add("Copy path", null, (sender, e) => CopyCurrent(r => r.Path));
        menu.Items.Add("Copy value data", null, (sender, e) => CopyCurrent(r => r.ValueData ?? ""));
        menu.Items.Add("Copy details", null, (sender, e) => CopyCurrent(r => r.Detail ?? ""));

        _grid.ContextMenuStrip = menu;
    }

    /// <summary>Rebuilds <see cref="_view"/> from the current filters and sort, and refreshes the grid and count.</summary>
    void ApplyFilter()
    {
        string category = _category.SelectedItem as string ?? _all;

        string process = _process.SelectedItem as string ?? _all;

        int statusMode = _status.SelectedIndex;

        string text = _search.Text.Trim();

        IEnumerable<EventRecord> query = _trace.Events;

        if (category != _all)
        {
            query = query.Where(e => e.Category == category);
        }

        if (process != _all)
        {
            query = query.Where(e => string.Equals(e.Process, process, StringComparison.OrdinalIgnoreCase));
        }

        if (statusMode == 1)
        {
            query = query.Where(e => e.Status != null && !e.Status.StartsWith("SUCCESS"));
        }
        else if (statusMode == 2)
        {
            query = query.Where(e => e.Status != null && e.Status.StartsWith("SUCCESS"));
        }

        if (text.Length > 0)
        {
            query = query.Where(e => Contains(e.Path, text) || Contains(e.ValueName, text) || Contains(e.ValueData, text)
                                  || Contains(e.Detail, text) || Contains(e.Op, text));
        }

        _view = query.ToList();

        SortView();

        _grid.RowCount = 0;

        _grid.RowCount = _view.Count;

        _grid.Invalidate();

        _count.Text = _view.Count.ToString("N0") + " of " + _trace.Events.Count.ToString("N0") + " events";

        ShowDetail();
    }

    /// <summary>Case-insensitive substring test that tolerates null.</summary>
    /// <param name="haystack">Text to search (may be null).</param>
    /// <param name="needle">Text to look for.</param>
    static bool Contains(string? haystack, string needle)
    {
        return haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>Supplies the text for a grid cell on demand (virtual mode).</summary>
    void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count)
        {
            return;
        }

        EventRecord r = _view[e.RowIndex];

        switch (e.ColumnIndex)
        {
            case 0: e.Value = r.Seq; break;
            case 1: e.Value = r.TimeUtc.ToString("HH:mm:ss.fff"); break;
            case 2: e.Value = r.Pid; break;
            case 3: e.Value = r.Process; break;
            case 4: e.Value = r.Category; break;
            case 5: e.Value = r.Op; break;
            case 6: e.Value = r.Path; break;
            case 7: e.Value = r.ValueName; break;
            case 8: e.Value = r.ValueKind; break;
            case 9: e.Value = r.ValueData; break;
            case 10: e.Value = r.Status; break;
            case 11: e.Value = r.Detail; break;
        }
    }

    /// <summary>Sorts by the clicked column; clicking the same column again reverses the order.</summary>
    void OnHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex == _sortColumn)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = e.ColumnIndex;

            _sortAscending = true;
        }

        SortView();

        _grid.ShowSortGlyph(_sortColumn, _sortAscending);

        _grid.Invalidate();
    }

    /// <summary>Sorts <see cref="_view"/> by the chosen column (numerically for # and PID, otherwise as text).</summary>
    void SortView()
    {
        if (_sortColumn < 0)
        {
            return;
        }

        if (_sortColumn == 0 || _sortColumn == 2)
        {
            Func<EventRecord, long> number = _sortColumn == 0 ? (r => r.Seq) : (r => r.Pid);

            _view = (_sortAscending ? _view.OrderBy(number) : _view.OrderByDescending(number)).ToList();

            return;
        }

        Func<EventRecord, string> text = TextKey(_sortColumn);

        _view = (_sortAscending ? _view.OrderBy(text, StringComparer.OrdinalIgnoreCase) : _view.OrderByDescending(text, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Chooses the text a column sorts on.</summary>
    /// <param name="column">Column index.</param>
    static Func<EventRecord, string> TextKey(int column)
    {
        switch (column)
        {
            case 1: return r => r.TimeUtc.ToString("HH:mm:ss.fff");
            case 3: return r => r.Process;
            case 4: return r => r.Category;
            case 5: return r => r.Op;
            case 6: return r => r.Path;
            case 7: return r => r.ValueName ?? "";
            case 8: return r => r.ValueKind ?? "";
            case 9: return r => r.ValueData ?? "";
            case 10: return r => r.Status ?? "";
            default: return r => r.Detail ?? "";
        }
    }

    /// <returns>The event on the grid's current row, or null if there is none.</returns>
    EventRecord? Current()
    {
        int index = _grid.CurrentCell?.RowIndex ?? -1;

        return index >= 0 && index < _view.Count ? _view[index] : null;
    }

    /// <summary>Fills the detail pane with every field of the selected event.</summary>
    void ShowDetail()
    {
        EventRecord? r = Current();

        if (r == null)
        {
            _detail.Text = "";

            return;
        }

        var sb = new StringBuilder();

        sb.AppendLine("#" + r.Seq + "   " + r.TimeUtc.ToString("yyyy-MM-dd HH:mm:ss.fff") + " UTC");
        sb.AppendLine("Process   " + r.Process + " (pid " + r.Pid + ")");
        sb.AppendLine("Action    " + r.Category + " / " + r.Op);
        sb.AppendLine("Path      " + r.Path);

        if (r.ValueName != null)
        {
            sb.AppendLine("Value     " + (r.ValueName.Length == 0 ? "(Default)" : r.ValueName)
                          + (r.ValueKind != null ? "  [" + r.ValueKind + "]" : "")
                          + (r.ValueData != null ? "  =  " + r.ValueData : ""));
        }

        if (r.Status != null)
        {
            sb.AppendLine("Result    " + r.Status);
        }

        if (r.Detail != null)
        {
            sb.AppendLine("Details   " + r.Detail);
        }

        _detail.Text = sb.ToString();
    }

    /// <summary>Makes a right-click select the cell under the pointer so the context menu acts on it.</summary>
    void OnCellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0)
        {
            _grid.CurrentCell = _grid[e.ColumnIndex, e.RowIndex];
        }
    }

    /// <summary>Copies one field of the selected event to the clipboard (does nothing if it is empty).</summary>
    /// <param name="pick">Chooses which field.</param>
    void CopyCurrent(Func<EventRecord, string> pick)
    {
        EventRecord? r = Current();

        if (r != null)
        {
            string text = pick(r);

            if (text.Length > 0)
            {
                Clipboard.SetText(text);
            }
        }
    }

    /// <summary>Saves the events currently shown (after filtering and sorting) to a CSV file.</summary>
    void OnExport(object? sender, EventArgs e)
    {
        using (var dialog = new SaveFileDialog { Filter = "CSV file (*.csv)|*.csv", FileName = _trace.DisplayName + ".csv" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            try
            {
                var header = new[] { "Seq", "TimeUtc", "Pid", "Process", "Category", "Operation", "Path", "ValueName", "ValueType", "ValueData", "Result", "Details" };

                var rows = new List<string[]> { header };

                rows.AddRange(_view.Select(r => new[]
                {
                    r.Seq.ToString(), r.TimeUtc.ToString("o"), r.Pid.ToString(), r.Process, r.Category, r.Op, r.Path,
                    r.ValueName ?? "", r.ValueKind ?? "", r.ValueData ?? "", r.Status ?? "", r.Detail ?? ""
                }));

                Ui.WriteCsv(dialog.FileName, rows);
            }
            catch (Exception ex)
            {
                Ui.ShowError(this, "Could not write the file: " + ex.Message);
            }
        }
    }

}
