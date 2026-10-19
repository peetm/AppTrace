// AppTrace
// File:    EtwMonitor.cs
// Purpose: Core of the capture engine: owns the kernel ETW session and shared helpers (partial class, see EtwMonitor.*.cs).
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Collections.Concurrent;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace AppTrace;

/// <summary>Chooses which optional (high-volume) event kinds a capture records.</summary>
public sealed class MonitorOptions
{

    /// <summary>Record DLL/EXE image loads.</summary>
    public bool IncludeImages { get; set; } = true;

    /// <summary>Record every file read and write (produces much larger traces).</summary>
    public bool IncludeReadWrite { get; set; } = true;

    /// <summary>Record file/registry close, cleanup and flush operations.</summary>
    public bool IncludeCloseCleanup { get; set; } = true;

}

/// <summary>
/// Runs a kernel ETW session (registry, file I/O, process, image load) and keeps only the events
/// belonging to the launched application and every process it starts.
/// </summary>
public sealed partial class EtwMonitor : IDisposable
{

    /// <summary>Destination for the events that pass the filter.</summary>
    readonly TraceWriter _writer;

    /// <summary>Converts kernel paths to DOS paths.</summary>
    readonly NtPaths _paths;

    /// <summary>Which optional events to record.</summary>
    readonly MonitorOptions _options;

    /// <summary>AppTrace's own process id; the launched program's parent, used to recognise the traced root process.</summary>
    readonly int _selfPid = Environment.ProcessId;

    /// <summary>Process ids currently being traced (the launched program and its descendants) and their image names.</summary>
    readonly ConcurrentDictionary<int, string> _tracked = new ConcurrentDictionary<int, string>();

    /// <summary>The kernel ETW session; null until <see cref="Start"/>.</summary>
    TraceEventSession? _session;

    /// <summary>Thread that pumps ETW events into the handlers.</summary>
    Thread? _thread;

    /// <summary>True between <see cref="ExpectRoot"/> and the root process starting.</summary>
    volatile bool _expectRoot;

    /// <summary>True once the root process has been seen to start.</summary>
    volatile bool _rootSeen;

    /// <summary>Creates a monitor; nothing is traced until <see cref="Start"/>.</summary>
    /// <param name="writer">Where captured events go.</param>
    /// <param name="paths">Path helper used to resolve file names.</param>
    /// <param name="options">Optional event kinds to record.</param>
    public EtwMonitor(TraceWriter writer, NtPaths paths, MonitorOptions options)
    {
        _writer = writer;
        _paths = paths;
        _options = options;
    }

    /// <summary>Number of processes (the program and anything it started) currently being traced.</summary>
    public int TrackedCount
    {
        get
        {
            return _tracked.Count;
        }
    }

    /// <summary>True once the launched program's process has been seen to start.</summary>
    public bool RootSeen
    {
        get
        {
            return _rootSeen;
        }
    }

    /// <summary>Starts the session. Call before launching the application so nothing at startup is missed.</summary>
    public void Start()
    {
        // The kernel logger is a singleton on most Windows versions; this throws if another tool owns it.
        _session = new TraceEventSession(KernelTraceEventParser.KernelSessionName);

        _session.StopOnDispose = true;

        _session.BufferSizeMB = 64;

        var keywords = KernelTraceEventParser.Keywords.Process
                     | KernelTraceEventParser.Keywords.Registry
                     | KernelTraceEventParser.Keywords.FileIOInit
                     | KernelTraceEventParser.Keywords.FileIO
                     | KernelTraceEventParser.Keywords.DiskFileIO;

        if (_options.IncludeImages)
        {
            keywords |= KernelTraceEventParser.Keywords.ImageLoad;
        }

        _session.EnableKernelProvider(keywords);

        HookProcessEvents();

        HookRegistryEvents();

        HookFileEvents();

        _thread = new Thread(() => _session.Source.Process()) { Name = "EtwProcessing", IsBackground = true };

        _thread.Start();
    }

    /// <summary>The next process whose parent is this monitor becomes the root of the traced tree.</summary>
    public void ExpectRoot()
    {
        _expectRoot = true;
    }

    /// <summary>Stops the ETW session and waits (up to 10 seconds) for queued events to be processed.</summary>
    public void Stop()
    {
        if (_session != null)
        {
            _session.Stop();

            _thread?.Join(TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>Disposes the session, which also stops it.</summary>
    public void Dispose()
    {
        _session?.Dispose();
    }

    /// <summary>Whether a process is part of the traced tree.</summary>
    /// <param name="pid">Process id from the event.</param>
    bool IsTracked(int pid)
    {
        return _tracked.ContainsKey(pid);
    }

    /// <summary>Image name of a traced process, or empty text if it is not traced.</summary>
    /// <param name="pid">Process id.</param>
    string NameOf(int pid)
    {
        return _tracked.TryGetValue(pid, out string? name) ? name : "";
    }

    /// <summary>Creates an <see cref="EventRecord"/> stamped with the current time and hands it to the writer.</summary>
    /// <param name="pid">Process that performed the action.</param>
    /// <param name="category">Registry, File, Process or Image.</param>
    /// <param name="op">The operation name.</param>
    /// <param name="path">Key or file path.</param>
    /// <param name="valueName">Registry value name, if any.</param>
    /// <param name="status">Formatted result, if known.</param>
    /// <param name="detail">Extra text; empty text is stored as nothing.</param>
    void Emit(int pid, string category, string op, string path, string? valueName = null, string? status = null, string? detail = null)
    {
        _writer.Add(new EventRecord
        {
            TimeUtc = DateTime.UtcNow,
            Pid = pid,
            Process = NameOf(pid),
            Category = category,
            Op = op,
            Path = path,
            ValueName = valueName,
            Status = status,
            Detail = string.IsNullOrEmpty(detail) ? null : detail
        });
    }

    /// <summary>Payload fields already shown elsewhere (or just noise), left out of <see cref="Payload"/>.</summary>
    static readonly HashSet<string> _skipFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FileName", "IrpPtr", "FileObject", "FileKey", "TTID", "KeyHandle", "KeyName", "ProcessID", "ProcessId",
        "ThreadID", "ThreadId", "InitialTime", "ElapsedTimeMSec", "Status", "NtStatus", "CommandLine",
        "ImageFileName", "DirectoryName", "UniqueProcessKey", "ParentID", "ParentId", "SessionID", "SessionId",
        "UserSID", "PageDirectoryBase", "PackageFullName", "ApplicationId"
    };

    /// <summary>Formats every remaining payload field as name=value. Keeps working if a provider adds fields.</summary>
    /// <param name="data">Any ETW event.</param>
    /// <returns>Space-separated <c>name=value</c> pairs; empty if nothing remains.</returns>
    static string Payload(TraceEvent data)
    {
        var sb = new StringBuilder();

        string[] names = data.PayloadNames;

        for (int i = 0; i < names.Length; i = i + 1)
        {
            if (_skipFields.Contains(names[i]))
            {
                continue;
            }

            string text = data.PayloadString(i);

            if (string.IsNullOrEmpty(text) || text == "0")
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(names[i]).Append('=').Append(text);
        }

        return sb.ToString();
    }

}
