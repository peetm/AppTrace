// AppTrace
// File:    EventRecord.cs
// Purpose: The data model for one captured event, and NTSTATUS-to-text formatting.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AppTrace;

/// <summary>
/// One captured action (a registry operation, file operation, process start/stop or DLL load).
/// Records are written to <c>trace.jsonl</c> as one JSON object per line (JSONL); null members are omitted.
/// </summary>
public sealed class EventRecord
{

    /// <summary>Sequence number in arrival order, assigned by <see cref="TraceWriter.Add"/>; starts at 1.</summary>
    public long Seq { get; set; }

    /// <summary>When the event happened, in UTC (taken from the ETW event timestamp).</summary>
    public DateTime TimeUtc { get; set; }

    /// <summary>Id of the process that performed the action.</summary>
    public int Pid { get; set; }

    /// <summary>Image name of the process that performed the action (for example <c>psp.exe</c>).</summary>
    public string Process { get; set; } = "";

    /// <summary>Registry, File, Process or Image.</summary>
    public string Category { get; set; } = "";

    /// <summary>Operation, e.g. SetValue, QueryValue, Create, Read, Write, Delete.</summary>
    public string Op { get; set; } = "";

    /// <summary>Registry key (full path) or file path (DOS form where it could be resolved).</summary>
    public string Path { get; set; } = "";

    /// <summary>Registry value name (registry value operations only); empty text means the key's default value.</summary>
    public string? ValueName { get; set; }

    /// <summary>Registry value type, read back from the registry when the event was processed.</summary>
    public string? ValueKind { get; set; }

    /// <summary>Registry value data, read back from the registry when the event was processed.</summary>
    public string? ValueData { get; set; }

    /// <summary>Result as hex NTSTATUS plus a name for the common ones (see <see cref="StatusText"/>).</summary>
    public string? Status { get; set; }

    /// <summary>Anything else: sizes, offsets, create disposition, command line, rename target...</summary>
    public string? Detail { get; set; }

    /// <summary>True for registry operations that read or write a named value (so <see cref="ValueName"/> is meaningful).</summary>
    [JsonIgnore]
    public bool IsRegistryValueOp
    {
        get
        {
            return Category == "Registry" && (Op == "SetValue" || Op == "QueryValue");
        }
    }

    /// <summary>Shared JSON settings: omit null members, and keep characters such as &amp;, &lt; and &gt; readable instead of \u-escaping them.</summary>
    static readonly JsonSerializerOptions _options = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Serialises this record to a single line of JSON.</summary>
    /// <returns>The JSON text, without a trailing newline.</returns>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, _options);
    }

    /// <summary>Parses one line of <c>trace.jsonl</c>.</summary>
    /// <param name="line">A single JSON object as written by <see cref="ToJson"/>.</param>
    /// <returns>The record, or null if the line holds a JSON null.</returns>
    public static EventRecord? FromJson(string line)
    {
        return JsonSerializer.Deserialize<EventRecord>(line, _options);
    }

}

/// <summary>Maps NTSTATUS numbers to readable text.</summary>
public static class StatusText
{

    /// <summary>Formats an NTSTATUS as <c>NAME (0xHHHHHHHH)</c>, or just the hex if the code isn't one of the common ones.</summary>
    /// <param name="status">The raw NTSTATUS value.</param>
    /// <returns>For example <c>NAME_NOT_FOUND (0xC0000034)</c>.</returns>
    public static string Format(int status)
    {
        string name = (uint)status switch
        {
            0x00000000 => "SUCCESS",
            0x00000103 => "PENDING",
            0x80000005 => "BUFFER_OVERFLOW",
            0x8000001A => "NO_MORE_ENTRIES",
            0xC0000034 => "NAME_NOT_FOUND",
            0xC000003A => "PATH_NOT_FOUND",
            0xC0000022 => "ACCESS_DENIED",
            0xC0000035 => "NAME_COLLISION",
            0xC0000043 => "SHARING_VIOLATION",
            0xC0000023 => "BUFFER_TOO_SMALL",
            0xC000000F => "NO_SUCH_FILE",
            0xC0000033 => "NAME_INVALID",
            0xC0000056 => "DELETE_PENDING",
            0xC000007F => "DISK_FULL",
            0xC00000BA => "IS_A_DIRECTORY",
            0xC0000101 => "DIR_NOT_EMPTY",
            _ => ""
        };

        string hex = "0x" + ((uint)status).ToString("X8");

        if (name.Length == 0)
        {
            return hex;
        }

        return name + " (" + hex + ")";
    }

}
