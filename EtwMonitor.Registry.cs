// AppTrace
// File:    EtwMonitor.Registry.cs
// Purpose: Registry event handlers: key-name tracking and conversion of kernel registry events to records.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

namespace AppTrace;

/// <summary>Registry half of <see cref="EtwMonitor"/>.</summary>
public sealed partial class EtwMonitor
{

    /// <summary>Key control block (KCB) address -> full key path. Only touched from the single ETW processing thread.</summary>
    readonly Dictionary<ulong, string> _kcbNames = new Dictionary<ulong, string>();

    /// <summary>Subscribes to registry events: KCB events build the handle-to-name table, the others are recorded.</summary>
    void HookRegistryEvents()
    {
        var kernel = _session!.Source.Kernel;

        // Name bookkeeping: these fire for every process, so they must not be filtered.
        Action<RegistryTraceData> learn = (RegistryTraceData d) =>
        {
            if (!string.IsNullOrEmpty(d.KeyName))
            {
                _kcbNames[(ulong)d.KeyHandle] = d.KeyName;
            }
        };

        kernel.RegistryKCBCreate += learn;
        kernel.RegistryKCBRundownBegin += learn;
        kernel.RegistryKCBRundownEnd += learn;
        kernel.RegistryKCBDelete += (RegistryTraceData d) => _kcbNames.Remove((ulong)d.KeyHandle);

        // Operations that name a (sub)key relative to a base handle.
        kernel.RegistryOpen += (RegistryTraceData d) => OnRegistry("OpenKey", d, relativeIsSubKey: true);
        kernel.RegistryCreate += (RegistryTraceData d) => OnRegistry("CreateKey", d, relativeIsSubKey: true);

        // Operations on a value of the key behind the handle; the relative name is the value name.
        kernel.RegistrySetValue += (RegistryTraceData d) => OnRegistry("SetValue", d, relativeIsValue: true);
        kernel.RegistryQueryValue += (RegistryTraceData d) => OnRegistry("QueryValue", d, relativeIsValue: true);
        kernel.RegistryDeleteValue += (RegistryTraceData d) => OnRegistry("DeleteValue", d, relativeIsValue: true);
        kernel.RegistryQueryMultipleValue += (RegistryTraceData d) => OnRegistry("QueryMultipleValue", d);

        // Operations on the key itself.
        kernel.RegistryQuery += (RegistryTraceData d) => OnRegistry("QueryKey", d);
        kernel.RegistryDelete += (RegistryTraceData d) => OnRegistry("DeleteKey", d);
        kernel.RegistryEnumerateKey += (RegistryTraceData d) => OnRegistry("EnumerateKey", d);
        kernel.RegistryEnumerateValueKey += (RegistryTraceData d) => OnRegistry("EnumerateValue", d);
        kernel.RegistrySetInformation += (RegistryTraceData d) => OnRegistry("SetKeyInfo", d);
        kernel.RegistryFlush += (RegistryTraceData d) => OnRegistry("Flush", d);

        if (_options.IncludeCloseCleanup)
        {
            kernel.RegistryClose += (RegistryTraceData d) => OnRegistry("CloseKey", d);
        }
    }

    /// <summary>
    /// Records a registry operation by the traced process, working out the full key path from the handle table
    /// (or showing <c>&lt;unresolved key&gt;</c> if the key was opened before the session started).
    /// </summary>
    /// <param name="op">Operation name to record.</param>
    /// <param name="d">The kernel event.</param>
    /// <param name="relativeIsSubKey">True for open/create, where the event's key name is a sub-key of the handle's key.</param>
    /// <param name="relativeIsValue">True for value operations, where the event carries the value name instead.</param>
    void OnRegistry(string op, RegistryTraceData d, bool relativeIsSubKey = false, bool relativeIsValue = false)
    {
        if (!IsTracked(d.ProcessID))
        {
            return;
        }

        string relative = d.KeyName ?? "";

        // For value operations the kernel puts the value name in its own field and leaves the key name empty
        string valueField = relativeIsValue ? (d.ValueName ?? "") : "";

        string path;

        string? valueName = null;

        if (relative.StartsWith(@"\REGISTRY", StringComparison.OrdinalIgnoreCase))
        {
            // Already a full path
            path = relative;
        }
        else if (_kcbNames.TryGetValue((ulong)d.KeyHandle, out string? baseName))
        {
            if (relativeIsSubKey && relative.Length > 0)
            {
                path = baseName + "\\" + relative;
            }
            else
            {
                path = baseName;

                if (relativeIsValue)
                {
                    valueName = valueField.Length > 0 ? valueField : relative;
                }
            }
        }
        else
        {
            // Key was opened before the session started and the rundown didn't cover it
            path = "<unresolved key 0x" + ((ulong)d.KeyHandle).ToString("X") + ">" + (relative.Length > 0 ? "\\" + relative : "");

            if (relativeIsValue)
            {
                valueName = valueField.Length > 0 ? valueField : relative;
            }
        }

        Emit(d.ProcessID, "Registry", op, path, valueName, StatusText.Format(d.Status), Payload(d));
    }

}
