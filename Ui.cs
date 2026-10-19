// AppTrace
// File:    Ui.cs
// Purpose: Shared UI helpers: a virtual-mode grid, label/combo factories, splitter placement, CSV export and error dialogs.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AppTrace;

/// <summary>A read-only grid that asks for each cell on demand, so millions of rows stay responsive.</summary>
public sealed class VirtualGrid : DataGridView
{

    /// <summary>Creates a read-only, full-row-select grid in virtual mode that fills its parent.</summary>
    public VirtualGrid()
    {
        DoubleBuffered = true;
        VirtualMode = true;
        ReadOnly = true;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        RowHeadersVisible = false;
        MultiSelect = true;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        BackgroundColor = SystemColors.Window;
        BorderStyle = BorderStyle.None;
        ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        Dock = DockStyle.Fill;
    }

    /// <summary>Adds a fixed-width, programmatically sortable text column.</summary>
    /// <param name="name">Internal column name.</param>
    /// <param name="header">Heading text.</param>
    /// <param name="width">Initial width in pixels (at 96 DPI).</param>
    public void AddColumn(string name, string header, int width)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Programmatic
        };

        Columns.Add(column);
    }

    /// <summary>Shows the sort arrow on one column and clears it on the rest.</summary>
    /// <param name="column">Index of the sorted column, or -1 to clear all arrows.</param>
    /// <param name="ascending">True for an up arrow, false for a down arrow.</param>
    public void ShowSortGlyph(int column, bool ascending)
    {
        foreach (DataGridViewColumn c in Columns)
        {
            c.HeaderCell.SortGlyphDirection = SortOrder.None;
        }

        if (column >= 0)
        {
            Columns[column].HeaderCell.SortGlyphDirection = ascending ? SortOrder.Ascending : SortOrder.Descending;
        }
    }

}

/// <summary>Small UI helpers shared by the windows.</summary>
public static class Ui
{

    /// <summary>Creates an auto-sized label with margins suited to a toolbar row.</summary>
    /// <param name="text">The label text.</param>
    public static Label MakeLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, Margin = new Padding(8, 7, 2, 0) };
    }

    /// <summary>Creates a drop-down list with the first item selected.</summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="items">The choices.</param>
    public static ComboBox MakeCombo(int width, params string[] items)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, Margin = new Padding(2, 3, 6, 3) };

        combo.Items.AddRange(items);

        if (items.Length > 0)
        {
            combo.SelectedIndex = 0;
        }

        return combo;
    }

    /// <summary>Puts the splitter at a fraction of the height once the control has a real size (setting it earlier throws).</summary>
    /// <param name="split">The splitter to position.</param>
    /// <param name="fraction">Where to put the splitter, as a fraction (0 to 1) of the container's height.</param>
    public static void SplitAt(SplitContainer split, double fraction)
    {
        bool done = false;

        EventHandler apply = (sender, e) =>
        {
            if (done || split.Height < 200)
            {
                return;
            }

            int low = split.Panel1MinSize;

            int high = split.Height - split.Panel2MinSize - split.SplitterWidth;

            if (high < low)
            {
                return;
            }

            try
            {
                split.SplitterDistance = Math.Min(high, Math.Max(low, (int)(split.Height * fraction)));

                done = true;
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        };

        split.SizeChanged += apply;
        split.HandleCreated += apply;
    }

    /// <summary>Quotes a value for CSV if it contains a comma, quote or line break.</summary>
    /// <param name="text">The cell text (null is treated as empty).</param>
    public static string CsvCell(string? text)
    {
        text ??= "";

        if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        return text;
    }

    /// <summary>Writes rows to a UTF-8 (with BOM, so Excel reads it correctly) CSV file.</summary>
    /// <param name="path">Destination file (overwritten).</param>
    /// <param name="rows">Each row's cell texts.</param>
    public static void WriteCsv(string path, IEnumerable<string[]> rows)
    {
        using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
        {
            foreach (string[] row in rows)
            {
                writer.WriteLine(string.Join(",", row.Select(CsvCell)));
            }
        }
    }

    /// <summary>Shows a simple error message box.</summary>
    /// <param name="owner">Window to show it over.</param>
    /// <param name="message">The text.</param>
    public static void ShowError(IWin32Window? owner, string message)
    {
        MessageBox.Show(owner, message, "AppTrace", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    /// <summary>Shows the full exception in a copyable box and appends it to apptrace-error.log beside the exe.</summary>
    /// <param name="owner">Window to show it over.</param>
    /// <param name="headline">Plain-language description of what failed.</param>
    /// <param name="ex">The exception whose full text (including inner exceptions) is shown.</param>
    public static void ShowException(IWin32Window? owner, string headline, Exception ex)
    {
        string detail = ex.ToString();

        string logNote = "";

        try
        {
            string log = Path.Combine(AppContext.BaseDirectory, "apptrace-error.log");

            File.AppendAllText(log, DateTime.Now.ToString("s") + "  " + headline + "\r\n" + detail + "\r\n\r\n");

            logNote = "Also saved to " + log;
        }
        catch (Exception)
        {
            // Read-only location (e.g. a USB stick); the text box still has everything.
        }

        using (var form = new Form())
        {
            form.AutoScaleDimensions = new SizeF(96f, 96f);
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.Text = "AppTrace";
            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = new Size(760, 440);
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;

            var message = new Label { Text = headline, Dock = DockStyle.Top, Height = 64, Padding = new Padding(8, 8, 8, 0) };

            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                Text = detail
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40 };

            var note = new Label { Text = logNote, Left = 8, Top = 12, Width = 460, AutoEllipsis = true };

            var copy = new Button { Text = "Copy", Left = 560, Top = 8, Width = 90 };

            var close = new Button { Text = "Close", Left = 660, Top = 8, Width = 90, DialogResult = DialogResult.OK };

            copy.Click += (sender, e) => Clipboard.SetText(headline + "\r\n\r\n" + detail);

            bottom.Controls.Add(note);
            bottom.Controls.Add(copy);
            bottom.Controls.Add(close);

            form.Controls.Add(box);
            form.Controls.Add(bottom);
            form.Controls.Add(message);

            form.AcceptButton = close;

            box.Select(0, 0);

            form.ShowDialog(owner);
        }
    }

}
