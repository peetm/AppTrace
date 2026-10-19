// AppTrace
// File:    EtwMonitor.Process.cs
// Purpose: Process start/stop and DLL (image) load handlers; decides which processes are traced.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace AppTrace;

/// <summary>Process half of <see cref="EtwMonitor"/>: follows the launched program and every process it starts.</summary>
public sealed partial class EtwMonitor
{

    /// <summary>
    /// Subscribes to process and image events. The first process started by AppTrace after <see cref="ExpectRoot"/> becomes
    /// the root; any process whose parent is traced is traced too, and removed when it stops.
    /// </summary>
    void HookProcessEvents()
    {
        var kernel = _session!.Source.Kernel;

        kernel.ProcessStart += (ProcessTraceData d) =>
        {
            bool isRoot = _expectRoot && d.ParentID == _selfPid;

            if (!isRoot && !_tracked.ContainsKey(d.ParentID))
            {
                return;
            }

            if (isRoot)
            {
                _expectRoot = false;

                _rootSeen = true;
            }

            _tracked[d.ProcessID] = Path.GetFileName(d.ImageFileName ?? "");

            Emit(d.ProcessID, "Process", "Start", d.ImageFileName ?? "", detail: "parent=" + d.ParentID + " cmd=" + d.CommandLine);
        };

        kernel.ProcessStop += (ProcessTraceData d) =>
        {
            if (!_tracked.ContainsKey(d.ProcessID))
            {
                return;
            }

            Emit(d.ProcessID, "Process", "Stop", d.ImageFileName ?? "", detail: Payload(d));

            _tracked.TryRemove(d.ProcessID, out _);
        };

        if (_options.IncludeImages)
        {
            kernel.ImageLoad += (ImageLoadTraceData d) =>
            {
                if (IsTracked(d.ProcessID))
                {
                    Emit(d.ProcessID, "Image", "Load", _paths.ToDosPath(d.FileName), detail: Payload(d));
                }
            };
        }
    }

}
