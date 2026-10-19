// AppTrace
// File:    NtPaths.cs
// Purpose: Translating kernel (NT) paths to DOS paths and reading registry values back from the live registry.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AppTrace;

/// <summary>Converts kernel (NT) paths to the forms people recognise, and reads registry values back.</summary>
public sealed class NtPaths
{

    /// <summary>Win32 <c>QueryDosDeviceW</c>: returns the NT device name behind a drive letter such as <c>C:</c>.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint QueryDosDeviceW(string lpDeviceName, StringBuilder lpTargetPath, uint ucchMax);

    /// <summary>NT device name and drive letter pairs, longest device name first.</summary>
    readonly List<(string device, string drive)> _devices = new List<(string device, string drive)>();

    /// <summary>64-bit view of HKEY_LOCAL_MACHINE, used for <c>\REGISTRY\MACHINE</c> keys.</summary>
    readonly RegistryKey _hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    /// <summary>64-bit view of HKEY_USERS, used for <c>\REGISTRY\USER</c> keys.</summary>
    readonly RegistryKey _hku = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);

    /// <summary>Builds the table mapping NT device names (<c>\Device\HarddiskVolumeN</c>) to drive letters.</summary>
    public NtPaths()
    {
        for (char letter = 'A'; letter <= 'Z'; letter = (char)(letter + 1))
        {
            string drive = letter + ":";

            var target = new StringBuilder(1024);

            if (QueryDosDeviceW(drive, target, (uint)target.Capacity) != 0)
            {
                _devices.Add((target.ToString(), drive));
            }
        }

        // Longest device names first so \Device\HarddiskVolume12 wins over \Device\HarddiskVolume1
        _devices.Sort((a, b) => b.device.Length.CompareTo(a.device.Length));
    }

    /// <summary>\Device\HarddiskVolume3\Users\me\a.txt becomes C:\Users\me\a.txt</summary>
    /// <param name="ntPath">A path as reported by the kernel.</param>
    /// <returns>The DOS-style path, or the input unchanged if no drive matches.</returns>
    public string ToDosPath(string ntPath)
    {
        if (string.IsNullOrEmpty(ntPath))
        {
            return ntPath;
        }

        foreach (var entry in _devices)
        {
            if (ntPath.StartsWith(entry.device, StringComparison.OrdinalIgnoreCase)
                && (ntPath.Length == entry.device.Length || ntPath[entry.device.Length] == '\\'))
            {
                return entry.drive + ntPath.Substring(entry.device.Length);
            }
        }

        if (ntPath.StartsWith(@"\Device\Mup\", StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + ntPath.Substring(@"\Device\Mup\".Length);
        }

        return ntPath;
    }

    /// <summary>
    /// Reads a value back from the live registry. Key is a kernel path such as
    /// \REGISTRY\MACHINE\SOFTWARE\X or \REGISTRY\USER\S-1-5-21-...\Software\X.
    /// This is the value at the time the event is processed, which can be slightly after the event itself.
    /// </summary>
    /// <param name="kernelKeyPath">Key path in kernel form.</param>
    /// <param name="valueName">Name of the value; empty text for the key's default value.</param>
    /// <returns>The value's type and a readable form of its data, or <c>(null, null)</c> if it can't be read.</returns>
    public (string? kind, string? data) ReadValue(string kernelKeyPath, string valueName)
    {
        try
        {
            string? subKey;

            RegistryKey? baseKey;

            if (TrySplit(kernelKeyPath, @"\REGISTRY\MACHINE", out subKey))
            {
                baseKey = _hklm;
            }
            else if (TrySplit(kernelKeyPath, @"\REGISTRY\USER", out subKey))
            {
                baseKey = _hku;
            }
            else
            {
                return (null, null);
            }

            using (RegistryKey? key = string.IsNullOrEmpty(subKey) ? null : baseKey.OpenSubKey(subKey, false))
            {
                if (key == null)
                {
                    return (null, null);
                }

                object? value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

                if (value == null)
                {
                    return (null, null);
                }

                RegistryValueKind kind = key.GetValueKind(valueName);

                return (kind.ToString(), Format(value, kind));
            }
        }
        catch (Exception)
        {
            // Key vanished, access denied, etc. The event itself is still logged.
            return (null, null);
        }
    }

    /// <summary>Removes <paramref name="prefix"/> from the start of <paramref name="path"/> if present.</summary>
    /// <param name="path">The full kernel key path.</param>
    /// <param name="prefix">Root to look for, such as <c>\REGISTRY\MACHINE</c>.</param>
    /// <param name="rest">The remaining sub-key path (no leading backslash) when the prefix matched.</param>
    /// <returns>True if the path starts with the prefix.</returns>
    static bool TrySplit(string path, string prefix, out string? rest)
    {
        rest = null;

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        rest = path.Substring(prefix.Length).TrimStart('\\');

        return true;
    }

    /// <summary>Renders registry data as text: DWORD/QWORD as hex and decimal, multi-strings joined by " | ", binary as hex (first 64 bytes).</summary>
    /// <param name="value">The data as returned by the registry API.</param>
    /// <param name="kind">The registry value type.</param>
    /// <returns>A readable string.</returns>
    static string Format(object value, RegistryValueKind kind)
    {
        switch (kind)
        {
            case RegistryValueKind.DWord:
                return "0x" + ((int)value).ToString("X8") + " (" + ((uint)(int)value) + ")";

            case RegistryValueKind.QWord:
                return "0x" + ((long)value).ToString("X16") + " (" + ((ulong)(long)value) + ")";

            case RegistryValueKind.MultiString:
                return string.Join(" | ", (string[])value);

            case RegistryValueKind.Binary:
                byte[] bytes = (byte[])value;

                const int limit = 64;

                string hex = BitConverter.ToString(bytes, 0, Math.Min(bytes.Length, limit)).Replace('-', ' ');

                return bytes.Length > limit
                    ? hex + " ... (" + bytes.Length + " bytes)"
                    : hex + " (" + bytes.Length + " bytes)";

            default:
                return value.ToString() ?? "";
        }
    }

}
