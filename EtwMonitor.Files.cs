// AppTrace
// File:    EtwMonitor.Files.cs
// Purpose: File-system event handlers for the capture engine (create, read, write, delete, rename, directory and info operations).
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace AppTrace;

/// <summary>File I/O half of <see cref="EtwMonitor"/>: turns kernel file events into <see cref="EventRecord"/>s.</summary>
public sealed partial class EtwMonitor
{

    /// <summary>File object -> NT path, learned from create events (the read/write events carry only the object). ETW thread only.</summary>
    readonly Dictionary<ulong, string> _fileByObject = new Dictionary<ulong, string>();

    /// <summary>Operations whose outcome arrives later in an end-of-operation event, keyed by IRP. ETW thread only.</summary>
    readonly Dictionary<ulong, EventRecord> _pending = new Dictionary<ulong, EventRecord>();

    /// <summary>Size at which the lookup tables are cleared, to bound memory use on very long runs.</summary>
    const int _mapLimit = 200_000;

    /// <summary>Subscribes to the kernel file I/O events, respecting <see cref="MonitorOptions"/>.</summary>
    void HookFileEvents()
    {
        var kernel = _session!.Source.Kernel;

        kernel.FileIOCreate += (FileIOCreateTraceData d) =>
        {
            if (!IsTracked(d.ProcessID))
            {
                return;
            }

            if (_fileByObject.Count > _mapLimit)
            {
                _fileByObject.Clear();
            }

            _fileByObject[(ulong)d.FileObject] = d.FileName ?? "";

            Defer((ulong)d.IrpPtr, d.ProcessID, "Create", _paths.ToDosPath(d.FileName ?? ""), Payload(d));
        };

        kernel.FileIOOperationEnd += (FileIOOpEndTraceData d) =>
        {
            if (!_pending.Remove((ulong)d.IrpPtr, out EventRecord? record))
            {
                return;
            }

            record.Status = StatusText.Format(d.NtStatus);

            if (record.Op == "Create")
            {
                record.Detail = (record.Detail + " result=" + CreateResult((long)d.ExtraInfo)).Trim();
            }

            _writer.Add(record);
        };

        // Operations whose result matters for comparing machines
        kernel.FileIODelete += (FileIOInfoTraceData d) => OnInfo("Delete", d, deferred: true);
        kernel.FileIORename += (FileIOInfoTraceData d) => OnInfo("Rename", d, deferred: true);
        kernel.FileIOSetInfo += (FileIOInfoTraceData d) => OnInfo("SetInfo", d, deferred: true);
        kernel.FileIOQueryInfo += (FileIOInfoTraceData d) => OnInfo("QueryInfo", d, deferred: false);
        kernel.FileIOFSControl += (FileIOInfoTraceData d) => OnInfo("FSControl", d, deferred: false);

        kernel.FileIODirEnum += (FileIODirEnumTraceData d) =>
        {
            if (IsTracked(d.ProcessID))
            {
                Emit(d.ProcessID, "File", "DirEnum", _paths.ToDosPath(d.DirectoryName ?? ""), detail: Payload(d));
            }
        };

        kernel.FileIODirNotify += (FileIODirEnumTraceData d) =>
        {
            if (IsTracked(d.ProcessID))
            {
                Emit(d.ProcessID, "File", "DirNotify", _paths.ToDosPath(d.DirectoryName ?? ""), detail: Payload(d));
            }
        };

        if (_options.IncludeReadWrite)
        {
            kernel.FileIORead += (FileIOReadWriteTraceData d) => OnReadWrite("Read", d);
            kernel.FileIOWrite += (FileIOReadWriteTraceData d) => OnReadWrite("Write", d);
        }

        if (_options.IncludeCloseCleanup)
        {
            kernel.FileIOCleanup += (FileIOSimpleOpTraceData d) => OnSimple("Cleanup", d, forget: false);
            kernel.FileIOClose += (FileIOSimpleOpTraceData d) => OnSimple("Close", d, forget: true);
            kernel.FileIOFlush += (FileIOSimpleOpTraceData d) => OnSimple("Flush", d, forget: false);
        }
    }

    /// <summary>Holds an operation until its end-of-operation event supplies the result (NTSTATUS).</summary>
    /// <param name="irp">I/O request packet address that links the start and end events.</param>
    /// <param name="pid">Process that issued the operation.</param>
    /// <param name="op">Operation name.</param>
    /// <param name="path">DOS path of the file.</param>
    /// <param name="detail">Extra payload text.</param>
    void Defer(ulong irp, int pid, string op, string path, string detail)
    {
        if (_pending.Count > _mapLimit)
        {
            _pending.Clear();
        }

        _pending[irp] = new EventRecord
        {
            TimeUtc = DateTime.UtcNow,
            Pid = pid,
            Process = NameOf(pid),
            Category = "File",
            Op = op,
            Path = path,
            Detail = string.IsNullOrEmpty(detail) ? null : detail
        };
    }

    /// <summary>Finds a file's path: from the event if present, else from earlier create events, else a placeholder.</summary>
    /// <param name="name">File name carried by the event, if any.</param>
    /// <param name="fileObject">Kernel file object address.</param>
    /// <returns>A DOS path, or <c>&lt;unknown file 0x...&gt;</c>.</returns>
    string FileNameOf(string? name, ulong fileObject)
    {
        if (!string.IsNullOrEmpty(name))
        {
            return _paths.ToDosPath(name);
        }

        if (_fileByObject.TryGetValue(fileObject, out string? known))
        {
            return _paths.ToDosPath(known);
        }

        return "<unknown file 0x" + fileObject.ToString("X") + ">";
    }

    /// <summary>Handles delete, rename, set-info, query-info and FS-control events.</summary>
    /// <param name="op">Operation name to record.</param>
    /// <param name="d">The kernel event.</param>
    /// <param name="deferred">True if the result should be awaited from the end-of-operation event.</param>
    void OnInfo(string op, FileIOInfoTraceData d, bool deferred)
    {
        if (!IsTracked(d.ProcessID))
        {
            return;
        }

        string path = FileNameOf(d.FileName, (ulong)d.FileObject);

        if (deferred)
        {
            Defer((ulong)d.IrpPtr, d.ProcessID, op, path, Payload(d));
        }
        else
        {
            Emit(d.ProcessID, "File", op, path, detail: Payload(d));
        }
    }

    /// <summary>Handles file read and write events.</summary>
    /// <param name="op">"Read" or "Write".</param>
    /// <param name="d">The kernel event (offset and size appear in the detail).</param>
    void OnReadWrite(string op, FileIOReadWriteTraceData d)
    {
        if (IsTracked(d.ProcessID))
        {
            Emit(d.ProcessID, "File", op, FileNameOf(d.FileName, (ulong)d.FileObject), detail: Payload(d));
        }
    }

    /// <summary>Handles cleanup, close and flush events.</summary>
    /// <param name="op">Operation name to record.</param>
    /// <param name="d">The kernel event.</param>
    /// <param name="forget">True for Close: the file object is no longer valid, so drop it from the name table.</param>
    void OnSimple(string op, FileIOSimpleOpTraceData d, bool forget)
    {
        if (!IsTracked(d.ProcessID))
        {
            return;
        }

        Emit(d.ProcessID, "File", op, FileNameOf(d.FileName, (ulong)d.FileObject));

        if (forget)
        {
            _fileByObject.Remove((ulong)d.FileObject);
        }
    }

    /// <summary>Names the outcome of a create/open (the I/O status information value).</summary>
    /// <param name="extraInfo">0 superseded, 1 opened, 2 created, 3 overwritten, 4 exists, 5 does not exist.</param>
    /// <returns>The outcome name, or the number if unknown.</returns>
    static string CreateResult(long extraInfo)
    {
        return extraInfo switch
        {
            0 => "Superseded",
            1 => "Opened",
            2 => "Created",
            3 => "Overwritten",
            4 => "Exists",
            5 => "DoesNotExist",
            _ => extraInfo.ToString()
        };
    }

}
