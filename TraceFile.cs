// AppTrace
// File:    TraceFile.cs
// Purpose: Loading a saved trace (trace.jsonl + meta.json) into memory.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

namespace AppTrace;

/// <summary>A trace loaded into memory together with the machine facts saved beside it.</summary>
public sealed class LoadedTrace
{

    /// <summary>Path of the <c>trace.jsonl</c> file that was read.</summary>
    public string TracePath { get; set; } = "";

    /// <summary>The run folder that contains the trace file and <c>meta.json</c>.</summary>
    public string Folder { get; set; } = "";

    /// <summary>Machine and run facts from <c>meta.json</c> (empty if that file was missing).</summary>
    public RunMeta Meta { get; set; } = new RunMeta();

    /// <summary>All the events, in the order they were captured.</summary>
    public List<EventRecord> Events { get; set; } = new List<EventRecord>();

    /// <summary>Short name for tabs and headings: the run folder's name.</summary>
    public string DisplayName
    {
        get
        {
            return Path.GetFileName(Folder.TrimEnd('\\', '/'));
        }
    }

}

/// <summary>Reads traces from disk.</summary>
public static class TraceFile
{

    /// <summary>Accepts a run folder or a .jsonl file and returns the path of the trace file.</summary>
    /// <param name="pathOrFolder">A run folder or the path of a trace file.</param>
    /// <returns><c>trace.jsonl</c> inside the folder, or the path unchanged if it is not a folder.</returns>
    public static string ResolveTracePath(string pathOrFolder)
    {
        if (Directory.Exists(pathOrFolder))
        {
            return Path.Combine(pathOrFolder, "trace.jsonl");
        }

        return pathOrFolder;
    }

    /// <summary>Reads trace.jsonl and meta.json. Reports the number of lines read so far through progress.</summary>
    /// <param name="pathOrFolder">A run folder or a trace file.</param>
    /// <param name="progress">Optional; receives the running line count every 50,000 lines and at the end.</param>
    /// <param name="cancel">Cancels the load (throws <see cref="OperationCanceledException"/>).</param>
    /// <returns>The loaded trace.</returns>
    /// <exception cref="FileNotFoundException">There is no trace file at the resolved path.</exception>
    public static LoadedTrace Load(string pathOrFolder, IProgress<long>? progress = null, CancellationToken cancel = default)
    {
        string tracePath = ResolveTracePath(pathOrFolder);

        if (!File.Exists(tracePath))
        {
            throw new FileNotFoundException("No trace file found", tracePath);
        }

        string folder = Path.GetDirectoryName(Path.GetFullPath(tracePath)) ?? ".";

        var trace = new LoadedTrace
        {
            TracePath = tracePath,
            Folder = folder,
            Meta = RunMeta.Load(Path.Combine(folder, "meta.json"))
        };

        long lines = 0;

        foreach (string line in File.ReadLines(tracePath))
        {
            cancel.ThrowIfCancellationRequested();

            lines = lines + 1;

            if (line.Length == 0)
            {
                continue;
            }

            EventRecord? record = EventRecord.FromJson(line);

            if (record != null)
            {
                trace.Events.Add(record);
            }

            if (lines % 50_000 == 0)
            {
                progress?.Report(lines);
            }
        }

        progress?.Report(lines);

        return trace;
    }

}
