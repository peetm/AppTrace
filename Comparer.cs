// AppTrace
// File:    Comparer.cs
// Purpose: Compares two loaded traces and renders the result (differences, counts, Markdown report).
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Text;
using System.Text.RegularExpressions;

namespace AppTrace;

/// <summary>The kinds of difference reported (also the text shown in the Kind column).</summary>
public static class DiffKind
{

    /// <summary>The item occurs only in trace A.</summary>
    public const string OnlyA = "Only on A";

    /// <summary>The item occurs only in trace B.</summary>
    public const string OnlyB = "Only on B";

    /// <summary>Both traces have the item but the operations or results differ.</summary>
    public const string Differs = "Behaved differently";

    /// <summary>A registry value was written/read with different data.</summary>
    public const string Data = "Different data";

    /// <summary>A file received a different total number of bytes written.</summary>
    public const string Bytes = "Different bytes written";

}

/// <summary>One line of the comparison: an item and what each machine did with it.</summary>
public sealed class DiffRow
{

    /// <summary>Process, Image, Registry or File.</summary>
    public string Category { get; set; } = "";

    /// <summary>One of the DiffKind values.</summary>
    public string Kind { get; set; } = "";

    /// <summary>The normalised key/value or file or image path.</summary>
    public string Item { get; set; } = "";

    /// <summary>What trace A did with the item (operations and outcomes, or data).</summary>
    public string A { get; set; } = "";

    /// <summary>What trace B did with the item.</summary>
    public string B { get; set; } = "";

}

/// <summary>Number of events of one operation in each trace.</summary>
public sealed class CountRow
{

    /// <summary>Category and operation, e.g. <c>Registry.SetValue</c>.</summary>
    public string Operation { get; set; } = "";

    /// <summary>Event count in trace A.</summary>
    public int A { get; set; }

    /// <summary>Event count in trace B.</summary>
    public int B { get; set; }

}

/// <summary>The outcome of comparing two traces.</summary>
public sealed class CompareResult
{

    /// <summary>The first trace (shown as "A").</summary>
    public LoadedTrace TraceA { get; set; } = new LoadedTrace();

    /// <summary>The second trace (shown as "B").</summary>
    public LoadedTrace TraceB { get; set; } = new LoadedTrace();

    /// <summary>True if GUIDs were replaced by a placeholder before comparing.</summary>
    public bool NormalizedGuids { get; set; }

    /// <summary>Event counts per operation.</summary>
    public List<CountRow> Counts { get; set; } = new List<CountRow>();

    /// <summary>All differences found.</summary>
    public List<DiffRow> Rows { get; set; } = new List<DiffRow>();

    /// <summary>Distinct items seen per category on each side.</summary>
    public Dictionary<string, (int a, int b)> Distinct { get; set; } = new Dictionary<string, (int a, int b)>();

    /// <summary>Categories in report order with their headings.</summary>
    static readonly (string category, string title)[] _sections =
    {
        ("Process", "Processes started"),
        ("Image", "DLLs / images loaded"),
        ("Registry", "Registry"),
        ("File", "Files")
    };

    /// <summary>Renders the comparison as a Markdown report.</summary>
    /// <param name="maxRows">Maximum rows listed per table (the rest are summarised as a count).</param>
    /// <returns>Markdown text.</returns>
    public string ToMarkdown(int maxRows)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# AppTrace comparison");
        sb.AppendLine();

        Describe(sb, "A", TraceA);
        Describe(sb, "B", TraceB);

        sb.AppendLine("Machine-specific text (user profile, user name, computer name, SIDs" + (NormalizedGuids ? ", GUIDs" : "") + ") was normalised before comparing.");
        sb.AppendLine();

        sb.AppendLine("## Event counts");
        sb.AppendLine();
        sb.AppendLine("| Operation | A | B |");
        sb.AppendLine("|---|---:|---:|");

        foreach (CountRow count in Counts)
        {
            sb.AppendLine("| " + count.Operation + " | " + count.A + " | " + count.B + " |");
        }

        sb.AppendLine();

        foreach (var (category, title) in _sections)
        {
            var distinct = Distinct.GetValueOrDefault(category);

            sb.AppendLine("## " + title);
            sb.AppendLine();
            sb.AppendLine(distinct.a + " distinct items on A, " + distinct.b + " on B.");
            sb.AppendLine();

            Table(sb, DiffKind.OnlyA, new[] { "Item", "Operations -> outcome" }, category, maxRows, row => new[] { row.Item, row.A });
            Table(sb, DiffKind.OnlyB, new[] { "Item", "Operations -> outcome" }, category, maxRows, row => new[] { row.Item, row.B });
            Table(sb, DiffKind.Differs, new[] { "Item", "A", "B" }, category, maxRows, row => new[] { row.Item, row.A, row.B });

            if (category == "Registry")
            {
                Table(sb, DiffKind.Data, new[] { "Value", "A (type: data)", "B (type: data)" }, category, maxRows, row => new[] { row.Item, row.A, row.B });
            }

            if (category == "File")
            {
                Table(sb, DiffKind.Bytes, new[] { "File", "A bytes written", "B bytes written" }, category, maxRows, row => new[] { row.Item, row.A, row.B });
            }
        }

        return sb.ToString();
    }

    /// <summary>Writes the one-line description of a trace (machine, OS, user, program, event count) to the report.</summary>
    /// <param name="sb">Report being built.</param>
    /// <param name="label">"A" or "B".</param>
    /// <param name="trace">The trace to describe.</param>
    static void Describe(StringBuilder sb, string label, LoadedTrace trace)
    {
        RunMeta m = trace.Meta;

        sb.AppendLine("**" + label + "** - " + m.MachineName + " (" + m.OsVersion + "), user " + m.UserName
                      + ", `" + m.Exe + "` " + m.Args + (m.DeElevated ? " (de-elevated)" : "")
                      + ", " + trace.Events.Count + " events"
                      + (m.Dropped > 0 ? ", **" + m.Dropped + " events dropped**" : ""));
        sb.AppendLine();
    }

    /// <summary>Writes one table of differences of a given kind within a category.</summary>
    /// <param name="sb">Report being built.</param>
    /// <param name="kind">Which <see cref="DiffKind"/> to list.</param>
    /// <param name="columns">Column headings.</param>
    /// <param name="category">Process, Image, Registry or File.</param>
    /// <param name="maxRows">Row limit.</param>
    /// <param name="project">Picks the cell texts for a row.</param>
    void Table(StringBuilder sb, string kind, string[] columns, string category, int maxRows, Func<DiffRow, string[]> project)
    {
        List<DiffRow> list = Rows.Where(r => r.Category == category && r.Kind == kind).ToList();

        sb.AppendLine("### " + kind + " (" + list.Count + ")");
        sb.AppendLine();

        if (list.Count == 0)
        {
            sb.AppendLine("_None._");
            sb.AppendLine();

            return;
        }

        sb.AppendLine("| " + string.Join(" | ", columns) + " |");
        sb.AppendLine("|" + string.Concat(columns.Select(_ => "---|")));

        foreach (DiffRow row in list.Take(maxRows))
        {
            sb.AppendLine("| " + string.Join(" | ", project(row).Select(Cell)) + " |");
        }

        if (list.Count > maxRows)
        {
            sb.AppendLine();
            sb.AppendLine("_" + (list.Count - maxRows) + " more not shown._");
        }

        sb.AppendLine();
    }

    /// <summary>Makes text safe to put in a Markdown table cell (escapes pipes, removes line breaks).</summary>
    static string Cell(string text)
    {
        return text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

}

/// <summary>Compares two loaded traces.</summary>
public static class Comparer
{

    /// <summary>Everything one side did with one item, combined into a signature that can be compared.</summary>
    sealed class Agg
    {

        /// <summary>Operations performed.</summary>
        public SortedSet<string> Ops = new SortedSet<string>();

        /// <summary>Results seen (OK, NAME_NOT_FOUND, Opened, Created...).</summary>
        public SortedSet<string> Outcomes = new SortedSet<string>();

        /// <summary>Registry value type, if data was seen.</summary>
        public string? Kind;

        /// <summary>Registry value data (normalised), if seen.</summary>
        public string? Data;

        /// <summary>Total bytes written to a file.</summary>
        public long BytesWritten;

        /// <summary>Text such as <c>OpenKey,QueryValue -&gt; OK</c>; two items "behave the same" when their signatures are equal.</summary>
        public string Signature
        {
            get
            {
                string ops = string.Join(",", Ops);

                return Outcomes.Count == 0 ? ops : ops + " -> " + string.Join(",", Outcomes);
            }
        }

    }

    /// <summary>Operations ignored when comparing, since they differ without meaning anything.</summary>
    static readonly HashSet<string> _noiseOps = new HashSet<string> { "CloseKey", "Close", "Cleanup", "Flush", "Stop" };

    /// <summary>Extracts the byte count from a write event's detail text.</summary>
    static readonly Regex _ioSize = new Regex(@"IoSize=(\d+)", RegexOptions.Compiled);

    /// <summary>Extracts the create/open outcome from a file event's detail text.</summary>
    static readonly Regex _result = new Regex(@"result=(\w+)", RegexOptions.Compiled);

    /// <summary>Categories compared, in report order.</summary>
    static readonly string[] _categories = { "Process", "Image", "Registry", "File" };

    /// <summary>Compares two traces, after normalising machine-specific text.</summary>
    /// <param name="a">The first trace.</param>
    /// <param name="b">The second trace.</param>
    /// <param name="normalizeGuids">True to treat different GUIDs as the same.</param>
    /// <returns>Counts and every difference found.</returns>
    public static CompareResult Compare(LoadedTrace a, LoadedTrace b, bool normalizeGuids)
    {
        var result = new CompareResult { TraceA = a, TraceB = b, NormalizedGuids = normalizeGuids };

        var aggA = Aggregate(a, normalizeGuids);

        var aggB = Aggregate(b, normalizeGuids);

        var names = a.Events.Concat(b.Events).Select(e => e.Category + "." + e.Op).Distinct().OrderBy(x => x).ToList();

        var countsA = a.Events.GroupBy(e => e.Category + "." + e.Op).ToDictionary(g => g.Key, g => g.Count());

        var countsB = b.Events.GroupBy(e => e.Category + "." + e.Op).ToDictionary(g => g.Key, g => g.Count());

        foreach (string name in names)
        {
            result.Counts.Add(new CountRow { Operation = name, A = countsA.GetValueOrDefault(name), B = countsB.GetValueOrDefault(name) });
        }

        foreach (string category in _categories)
        {
            var itemsA = aggA.GetValueOrDefault(category) ?? new Dictionary<string, Agg>();

            var itemsB = aggB.GetValueOrDefault(category) ?? new Dictionary<string, Agg>();

            result.Distinct[category] = (itemsA.Count, itemsB.Count);

            foreach (var pair in itemsA.OrderBy(p => p.Key))
            {
                if (!itemsB.TryGetValue(pair.Key, out Agg? other))
                {
                    result.Rows.Add(Row(category, DiffKind.OnlyA, pair.Key, pair.Value.Signature, ""));

                    continue;
                }

                if (pair.Value.Signature != other.Signature)
                {
                    result.Rows.Add(Row(category, DiffKind.Differs, pair.Key, pair.Value.Signature, other.Signature));
                }

                if (category == "Registry" && pair.Value.Data != null && other.Data != null && pair.Value.Data != other.Data)
                {
                    result.Rows.Add(Row(category, DiffKind.Data, pair.Key, pair.Value.Kind + ": " + pair.Value.Data, other.Kind + ": " + other.Data));
                }

                if (category == "File" && pair.Value.BytesWritten != other.BytesWritten)
                {
                    result.Rows.Add(Row(category, DiffKind.Bytes, pair.Key, pair.Value.BytesWritten.ToString("N0"), other.BytesWritten.ToString("N0")));
                }
            }

            foreach (var pair in itemsB.Where(p => !itemsA.ContainsKey(p.Key)).OrderBy(p => p.Key))
            {
                result.Rows.Add(Row(category, DiffKind.OnlyB, pair.Key, "", pair.Value.Signature));
            }
        }

        return result;
    }

    /// <summary>Creates a <see cref="DiffRow"/>.</summary>
    static DiffRow Row(string category, string kind, string item, string a, string b)
    {
        return new DiffRow { Category = category, Kind = kind, Item = item, A = a, B = b };
    }

    /// <summary>Groups a trace's events by category and normalised item, combining each item's events into an <see cref="Agg"/>.</summary>
    /// <param name="trace">The trace.</param>
    /// <param name="normalizeGuids">Whether to normalise GUIDs.</param>
    /// <returns>Category -> item -> aggregate.</returns>
    static Dictionary<string, Dictionary<string, Agg>> Aggregate(LoadedTrace trace, bool normalizeGuids)
    {
        var normalizer = new Normalizer(trace.Meta, normalizeGuids);

        var byCategory = new Dictionary<string, Dictionary<string, Agg>>();

        foreach (EventRecord e in trace.Events)
        {
            if (_noiseOps.Contains(e.Op))
            {
                continue;
            }

            if (!byCategory.TryGetValue(e.Category, out Dictionary<string, Agg>? items))
            {
                items = new Dictionary<string, Agg>(StringComparer.OrdinalIgnoreCase);

                byCategory[e.Category] = items;
            }

            string key = normalizer.Apply(e.Path);

            if (e.ValueName != null)
            {
                key = key + "  ::  " + (e.ValueName.Length == 0 ? "(Default)" : normalizer.Apply(e.ValueName));
            }

            if (!items.TryGetValue(key, out Agg? agg))
            {
                agg = new Agg();

                items[key] = agg;
            }

            Add(agg, e, normalizer);
        }

        return byCategory;
    }

    /// <summary>Folds one event into an item's aggregate.</summary>
    /// <param name="agg">The item's aggregate.</param>
    /// <param name="e">The event.</param>
    /// <param name="normalizer">Used to normalise registry data text.</param>
    static void Add(Agg agg, EventRecord e, Normalizer normalizer)
    {
        agg.Ops.Add(e.Op);

        if (e.Status != null)
        {
            agg.Outcomes.Add(e.Status.StartsWith("SUCCESS") ? "OK" : e.Status.Split(' ')[0]);
        }

        if (e.Detail != null)
        {
            Match result = _result.Match(e.Detail);

            if (result.Success)
            {
                agg.Outcomes.Add(result.Groups[1].Value);
            }

            Match size = _ioSize.Match(e.Detail);

            if (e.Op == "Write" && size.Success && long.TryParse(size.Groups[1].Value, out long bytes))
            {
                agg.BytesWritten = agg.BytesWritten + bytes;
            }
        }

        // A value written by the app beats one it merely read
        if (e.ValueData != null && (agg.Data == null || e.Op == "SetValue"))
        {
            agg.Data = normalizer.Apply(e.ValueData);

            agg.Kind = e.ValueKind;
        }
    }

}
