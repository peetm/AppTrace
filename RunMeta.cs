// AppTrace
// File:    RunMeta.cs
// Purpose: Machine/run facts saved beside a trace (meta.json) and the text normaliser used when comparing traces.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Text.Json;
using System.Text.RegularExpressions;

namespace AppTrace;

/// <summary>Facts about the machine and run, saved next to the trace (<c>meta.json</c>) so two runs can be compared fairly.</summary>
public sealed class RunMeta
{

    /// <summary>Computer name of the machine the trace was captured on.</summary>
    public string MachineName { get; set; } = "";

    /// <summary>Windows user name the program ran as.</summary>
    public string UserName { get; set; } = "";

    /// <summary>The user's profile folder (for example <c>C:\Users\pm</c>); replaced by <c>%USERPROFILE%</c> when comparing.</summary>
    public string UserProfile { get; set; } = "";

    /// <summary>The user's security identifier; replaced by <c>%USER_SID%</c> when comparing.</summary>
    public string UserSid { get; set; } = "";

    /// <summary>Windows version text and bitness.</summary>
    public string OsVersion { get; set; } = "";

    /// <summary>Full path of the program that was run.</summary>
    public string Exe { get; set; } = "";

    /// <summary>Command-line arguments the program was started with.</summary>
    public string Args { get; set; } = "";

    /// <summary>True if the program was started without Administrator rights.</summary>
    public bool DeElevated { get; set; }

    /// <summary>When the capture started (UTC).</summary>
    public DateTime StartedUtc { get; set; }

    /// <summary>When the capture ended (UTC).</summary>
    public DateTime EndedUtc { get; set; }

    /// <summary>Number of events lost because the writer's queue was full.</summary>
    public long Dropped { get; set; }

    /// <summary>JSON settings for <c>meta.json</c> (indented so it is easy to read).</summary>
    static readonly JsonSerializerOptions _options = new JsonSerializerOptions { WriteIndented = true };

    /// <summary>Writes this object to a JSON file.</summary>
    /// <param name="path">Destination file path (overwritten).</param>
    public void Save(string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(this, _options));
    }

    /// <summary>Reads a <c>meta.json</c> file.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The stored facts, or an empty <see cref="RunMeta"/> if the file does not exist.</returns>
    public static RunMeta Load(string path)
    {
        if (!File.Exists(path))
        {
            return new RunMeta();
        }

        return JsonSerializer.Deserialize<RunMeta>(File.ReadAllText(path)) ?? new RunMeta();
    }

    /// <summary>Describes the current machine and user for a run that is about to start.</summary>
    /// <param name="exe">The program being run.</param>
    /// <param name="args">Its command-line arguments.</param>
    /// <param name="deElevated">Whether it is being started without Administrator rights.</param>
    /// <returns>A new <see cref="RunMeta"/> with <see cref="StartedUtc"/> set to now.</returns>
    public static RunMeta ForThisMachine(string exe, string args, bool deElevated)
    {
        return new RunMeta
        {
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UserSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "",
            OsVersion = Environment.OSVersion.VersionString + (Environment.Is64BitOperatingSystem ? " (64-bit)" : " (32-bit)"),
            Exe = exe,
            Args = args,
            DeElevated = deElevated,
            StartedUtc = DateTime.UtcNow
        };
    }

}

/// <summary>
/// Rewrites machine-specific text (user name, profile folder, computer name, SIDs, GUIDs) into placeholders
/// so that two machines' traces compare cleanly.
/// </summary>
public sealed class Normalizer
{

    /// <summary>Matches a domain/machine SID such as <c>S-1-5-21-1-2-3-1001</c>.</summary>
    static readonly Regex _sid = new Regex(@"S-1-5-21-\d+-\d+-\d+(-\d+)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Matches a GUID, with or without braces.</summary>
    static readonly Regex _guid = new Regex(@"\{?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}?",
                                            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Literal machine-specific strings (from the run's metadata) and what to replace each with, in order.</summary>
    readonly List<(Regex pattern, string replacement)> _literals = new List<(Regex pattern, string replacement)>();

    /// <summary>Whether GUIDs are replaced by <c>{GUID}</c>.</summary>
    readonly bool _normalizeGuids;

    /// <summary>Creates a normaliser for one trace's machine.</summary>
    /// <param name="meta">The machine facts of the trace whose text will be normalised.</param>
    /// <param name="normalizeGuids">True to also replace every GUID with <c>{GUID}</c> (hides per-machine random IDs).</param>
    public Normalizer(RunMeta meta, bool normalizeGuids)
    {
        _normalizeGuids = normalizeGuids;

        // Longest first, so the profile folder is replaced before the bare user name inside it
        Add(meta.UserProfile, "%USERPROFILE%");
        Add(meta.UserSid, "%USER_SID%");
        Add(meta.MachineName, "%COMPUTERNAME%");
        Add(meta.UserName, "%USERNAME%");
    }

    /// <summary>Registers a literal to replace; ignored if shorter than three characters (too likely to match by accident).</summary>
    /// <param name="literal">The text to find (case-insensitive).</param>
    /// <param name="replacement">The placeholder to put in its place.</param>
    void Add(string literal, string replacement)
    {
        if (literal.Length >= 3)
        {
            _literals.Add((new Regex(Regex.Escape(literal), RegexOptions.IgnoreCase | RegexOptions.Compiled), replacement));
        }
    }

    /// <summary>Applies all replacements to a piece of text.</summary>
    /// <param name="text">A path, value name or value data.</param>
    /// <returns>The text with machine-specific parts replaced by placeholders.</returns>
    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        foreach (var (pattern, replacement) in _literals)
        {
            text = pattern.Replace(text, replacement);
        }

        text = _sid.Replace(text, "%SID%");

        if (_normalizeGuids)
        {
            text = _guid.Replace(text, "{GUID}");
        }

        return text;
    }

}
