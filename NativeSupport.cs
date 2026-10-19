// AppTrace
// File:    NativeSupport.cs
// Purpose: Makes TraceEvent work in a single-file exe by unpacking and loading the embedded KernelTraceControl.dll.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AppTrace;

/// <summary>
/// TraceEvent needs its native KernelTraceControl.dll to start a kernel session. It looks for it next to the
/// assembly, which does not exist in a single-file exe, so the DLL is carried inside AppTrace.exe as a resource,
/// unpacked to a temp folder, loaded from there, and TraceEvent is told it is already loaded.
/// </summary>
public static class NativeSupport
{

    /// <summary>Name of the embedded resource (and of the file it is unpacked to).</summary>
    const string ResourceName = "KernelTraceControl.dll";

    /// <summary>Serialises initialisation.</summary>
    static readonly object Gate = new object();

    /// <summary>Handle of the loaded DLL; zero until loaded.</summary>
    static IntPtr _handle;

    /// <summary>Whether the DllImport resolver for TraceEvent has been registered.</summary>
    static bool _resolverSet;

    /// <summary>Safe to call repeatedly. Does nothing when the DLL was not embedded (normal dotnet build/run layout).</summary>
    public static void EnsureKernelTraceControl()
    {
        lock (Gate)
        {
            if (_handle != IntPtr.Zero)
            {
                return;
            }

            Assembly mine = typeof(NativeSupport).Assembly;

            byte[] bytes;

            using (Stream? stream = mine.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    return;
                }

                using (var copy = new MemoryStream())
                {
                    stream.CopyTo(copy);

                    bytes = copy.ToArray();
                }
            }

            string path = Extract(bytes);

            _handle = NativeLibrary.Load(path);

            Assembly traceEvent = typeof(Microsoft.Diagnostics.Tracing.Session.TraceEventSession).Assembly;

            if (!_resolverSet)
            {
                try
                {
                    IntPtr handle = _handle;

                    NativeLibrary.SetDllImportResolver(traceEvent, (name, assembly, searchPath) =>
                    {
                        if (name.StartsWith("KernelTraceControl", StringComparison.OrdinalIgnoreCase))
                        {
                            return handle;
                        }

                        return IntPtr.Zero;
                    });
                }
                catch (InvalidOperationException)
                {
                    // A resolver was already registered; the module is loaded in the process so lookup by name still finds it.
                }

                _resolverSet = true;
            }

            // TraceEvent would otherwise try to load the DLL from a path that does not exist in a single-file exe.
            Type? control = traceEvent.GetType("Microsoft.Diagnostics.Tracing.ETWKernelControl");

            FieldInfo? loaded = control?.GetField("s_KernelTraceControlLoaded", BindingFlags.NonPublic | BindingFlags.Static);

            loaded?.SetValue(null, true);
        }
    }

    /// <summary>
    /// Writes the DLL to a folder named after a hash of its content, under the temp folder (or beside the exe if that
    /// fails), so different versions never collide and a copy already there is reused.
    /// </summary>
    /// <param name="bytes">The DLL image.</param>
    /// <returns>Full path of the unpacked file.</returns>
    /// <exception cref="IOException">No candidate folder was writable.</exception>
    static string Extract(byte[] bytes)
    {
        string hash = Convert.ToHexString(SHA256.HashData(bytes)).Substring(0, 12);

        var candidates = new List<string>
        {
            Path.Combine(Path.GetTempPath(), "AppTrace-native", hash),
            Path.Combine(AppContext.BaseDirectory, "native-" + hash)
        };

        Exception? last = null;

        foreach (string folder in candidates)
        {
            try
            {
                Directory.CreateDirectory(folder);

                string target = Path.Combine(folder, ResourceName);

                if (!File.Exists(target) || new FileInfo(target).Length != bytes.Length)
                {
                    string temp = target + "." + Environment.ProcessId + ".tmp";

                    File.WriteAllBytes(temp, bytes);

                    try
                    {
                        File.Move(temp, target, true);
                    }
                    catch (IOException)
                    {
                        // Another AppTrace instance has it loaded or got there first; its copy is identical.
                        File.Delete(temp);
                    }
                }

                return target;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new IOException("Could not unpack " + ResourceName + " to a temp folder: " + last?.Message, last);
    }

}
