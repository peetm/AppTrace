// AppTrace
// File:    TraceWriter.cs
// Purpose: Queues captured events and writes them to trace.jsonl on a background thread.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Collections.Concurrent;
using System.Text;

namespace AppTrace;

/// <summary>
/// Takes events from the ETW callback thread, adds registry values, and writes them to disk on its own thread
/// so the ETW callback never blocks on disk or registry access.
/// </summary>
public sealed class TraceWriter : IDisposable
{

    /// <summary>Events waiting to be written; bounded so a runaway program cannot exhaust memory.</summary>
    readonly BlockingCollection<EventRecord> _queue = new BlockingCollection<EventRecord>(boundedCapacity: 500_000);

    /// <summary>The background thread that drains <see cref="_queue"/>.</summary>
    readonly Thread _thread;

    /// <summary>The open <c>trace.jsonl</c> file.</summary>
    readonly StreamWriter _writer;

    /// <summary>Used to read registry values back for value operations.</summary>
    readonly NtPaths _paths;

    /// <summary>Number of events written per "Category.Op" (for summary.txt).</summary>
    readonly ConcurrentDictionary<string, long> _counts = new ConcurrentDictionary<string, long>();

    /// <summary>Last sequence number handed out.</summary>
    long _seq;

    /// <summary>Number of events discarded because the queue was full.</summary>
    long _dropped;

    /// <summary>Creates the trace file and starts the writer thread.</summary>
    /// <param name="jsonlPath">Where to write the trace (overwritten).</param>
    /// <param name="paths">Helper used to read registry values back before each value event is written.</param>
    public TraceWriter(string jsonlPath, NtPaths paths)
    {
        _paths = paths;

        _writer = new StreamWriter(jsonlPath, false, new UTF8Encoding(false));

        _thread = new Thread(WriterLoop) { Name = "TraceWriter", IsBackground = true };

        _thread.Start();
    }

    /// <summary>Number of events lost because the queue was full (should be 0).</summary>
    public long Dropped
    {
        get
        {
            return Interlocked.Read(ref _dropped);
        }
    }

    /// <summary>Live per-"Category.Op" event counts.</summary>
    public IReadOnlyDictionary<string, long> Counts
    {
        get
        {
            return _counts;
        }
    }

    /// <summary>Called from the ETW thread. Never blocks; counts a drop if the queue is full.</summary>
    public void Add(EventRecord record)
    {
        record.Seq = Interlocked.Increment(ref _seq);

        if (!_queue.TryAdd(record))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    /// <summary>
    /// Writer thread body: for each successful registry value operation reads the current value from the live registry
    /// (the kernel event does not carry the data), then writes the record as one JSON line.
    /// </summary>
    void WriterLoop()
    {
        foreach (EventRecord record in _queue.GetConsumingEnumerable())
        {
            if (record.IsRegistryValueOp && !string.IsNullOrEmpty(record.Path)
                && (record.Status == null || record.Status.StartsWith("SUCCESS")))
            {
                var (kind, data) = _paths.ReadValue(record.Path, record.ValueName ?? "");

                record.ValueKind = kind;
                record.ValueData = data;
            }

            _writer.WriteLine(record.ToJson());

            _counts.AddOrUpdate(record.Category + "." + record.Op, 1, (key, old) => old + 1);
        }
    }

    /// <summary>Stops accepting events, writes everything still queued, closes the file.</summary>
    public void Dispose()
    {
        _queue.CompleteAdding();

        _thread.Join();

        _writer.Dispose();
    }

}
