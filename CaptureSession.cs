// AppTrace
// File:    CaptureSession.cs
// Purpose: Orchestrates one capture (elevation helpers, capture settings, and the CaptureSession that runs it).
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Diagnostics;
using System.Security.Principal;

namespace AppTrace;

/// <summary>Checks for, and obtains, Administrator rights (needed for the kernel trace).</summary>
public static class Elevation
{

    /// <summary>Whether this process is running with Administrator rights.</summary>
    /// <returns>True if elevated.</returns>
    public static bool IsElevated()
    {
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>Starts a new copy of this program with Administrator rights. False if the user declined the UAC prompt.</summary>
    /// <returns>True if the new copy was started; the caller should then close this one.</returns>
    public static bool RelaunchElevated()
    {
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath ?? "AppTrace.exe")
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(info)?.Dispose();

            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

}

/// <summary>Everything needed to run one capture.</summary>
public sealed class CaptureSettings
{

    /// <summary>Path of the program to run.</summary>
    public string Exe { get; set; } = "";

    /// <summary>Command-line arguments for the program.</summary>
    public string Args { get; set; } = "";

    /// <summary>Folder in which a new run folder (program-machine-timestamp) is created.</summary>
    public string OutputRoot { get; set; } = "";

    /// <summary>Try to start the program without Administrator rights (experimental).</summary>
    public bool DeElevate { get; set; }

    /// <summary>Seconds to keep listening after the program exits, so the final buffered events arrive.</summary>
    public int GraceSeconds { get; set; } = 3;

    /// <summary>Which optional event kinds to record.</summary>
    public MonitorOptions Options { get; set; } = new MonitorOptions();

}

/// <summary>Runs one capture: starts the ETW session, launches the program, waits, saves the trace.</summary>
public sealed class CaptureSession
{

    /// <summary>Events written so far (for the progress window).</summary>
    long _eventCount;

    /// <summary>Processes currently traced (for the progress window).</summary>
    int _processCount;

    /// <summary>Short description of what the capture is doing.</summary>
    volatile string _state = "Starting";

    /// <summary>Warning text for the end of the capture; empty if none.</summary>
    volatile string _warning = "";

    /// <summary>Number of events captured so far.</summary>
    public long EventCount
    {
        get
        {
            return Interlocked.Read(ref _eventCount);
        }
    }

    /// <summary>Number of processes currently being traced.</summary>
    public int ProcessCount
    {
        get
        {
            return _processCount;
        }
    }

    /// <summary>Current phase: Starting, Launching, Monitoring, Finishing or Done.</summary>
    public string State
    {
        get
        {
            return _state;
        }
    }

    /// <summary>Problems worth showing the user once the capture ends (dropped events, de-elevation not attaching).</summary>
    public string Warning
    {
        get
        {
            return _warning;
        }
    }

    /// <summary>Runs the capture on a background thread. Cancel the token to stop early. Returns the run folder.</summary>
    public Task<string> RunAsync(CaptureSettings settings, CancellationToken stop)
    {
        return Task.Run(() => Run(settings, stop));
    }

    /// <summary>The capture itself (runs on a worker thread): create the run folder, start tracing, launch, wait, save meta.json and summary.txt.</summary>
    /// <param name="settings">What to capture.</param>
    /// <param name="stop">Signalled when the user presses Stop.</param>
    /// <returns>The run folder.</returns>
    /// <exception cref="FileNotFoundException">The program does not exist.</exception>
    /// <exception cref="InvalidOperationException">The kernel session could not start, or the program could not be launched.</exception>
    string Run(CaptureSettings settings, CancellationToken stop)
    {
        string exe = Path.GetFullPath(settings.Exe);

        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("Program not found", exe);
        }

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        string runFolder = Path.Combine(settings.OutputRoot, Path.GetFileNameWithoutExtension(exe) + "-" + Environment.MachineName + "-" + stamp);

        Directory.CreateDirectory(runFolder);

        RunMeta meta = RunMeta.ForThisMachine(exe, settings.Args, settings.DeElevate);

        var paths = new NtPaths();

        var writer = new TraceWriter(Path.Combine(runFolder, "trace.jsonl"), paths);

        var monitor = new EtwMonitor(writer, paths, settings.Options);

        try
        {
            NativeSupport.EnsureKernelTraceControl();

            monitor.Start();
        }
        catch (Exception ex)
        {
            writer.Dispose();

            throw new InvalidOperationException("Could not start the Windows kernel trace session. " + ex.Message, ex);
        }

        try
        {
            monitor.ExpectRoot();

            _state = "Launching " + Path.GetFileName(exe);

            try
            {
                Launch(exe, settings.Args, settings.DeElevate);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Could not launch " + exe + ". " + ex.Message, ex);
            }

            _state = "Monitoring";

            var launched = Stopwatch.StartNew();

            while (!stop.IsCancellationRequested)
            {
                _eventCount = writer.Counts.Values.Sum();

                _processCount = monitor.TrackedCount;

                if (monitor.RootSeen && monitor.TrackedCount == 0)
                {
                    break;
                }

                if (!monitor.RootSeen && launched.Elapsed > TimeSpan.FromSeconds(20))
                {
                    _warning = "The launched program never appeared in the trace. Another tool may be holding the kernel logger.";

                    break;
                }

                stop.WaitHandle.WaitOne(250);
            }

            // ETW delivers in batches; give the last buffers time to arrive
            _state = "Finishing";

            Thread.Sleep(TimeSpan.FromSeconds(settings.GraceSeconds));

            monitor.Stop();
        }
        finally
        {
            monitor.Dispose();

            writer.Dispose();
        }

        _eventCount = writer.Counts.Values.Sum();

        meta.EndedUtc = DateTime.UtcNow;

        meta.Dropped = writer.Dropped;

        meta.Save(Path.Combine(runFolder, "meta.json"));

        var lines = writer.Counts.OrderBy(p => p.Key).Select(p => p.Key.PadRight(32) + p.Value.ToString().PadLeft(10));

        File.WriteAllLines(Path.Combine(runFolder, "summary.txt"), lines);

        if (writer.Dropped > 0)
        {
            _warning = writer.Dropped + " events were dropped because the queue filled up. Capture again without file reads and writes.";
        }
        else if (settings.DeElevate && writer.Counts.GetValueOrDefault("Process.Start") <= 1)
        {
            _warning = "Only one process was traced, so launching without Administrator rights may not have attached to the real program. Try again without it.";
        }

        _state = "Done";

        return runFolder;
    }

    /// <summary>Starts the program to be traced, in its own folder.</summary>
    /// <param name="exe">Full path of the program.</param>
    /// <param name="appArgs">Its arguments.</param>
    /// <param name="deElevate">True to start it through <c>runas /trustlevel</c> so it runs without Administrator rights.</param>
    static void Launch(string exe, string appArgs, bool deElevate)
    {
        ProcessStartInfo info;

        if (deElevate)
        {
            // Children inherit our elevated token; runas /trustlevel starts the program as a basic user.
            info = new ProcessStartInfo("runas.exe", "/trustlevel:0x20000 \"\\\"" + exe + "\\\" " + appArgs + "\"");
        }
        else
        {
            info = new ProcessStartInfo(exe, appArgs);
        }

        info.UseShellExecute = false;

        info.WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory;

        Process.Start(info)?.Dispose();
    }

}
