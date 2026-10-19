// AppTrace
// File:    AppSettings.cs
// Purpose: Remembered user choices (folders, last program, options, window size), saved as JSON beside the exe.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Text.Json;

namespace AppTrace;

/// <summary>
/// Remembered choices (folders, last program, capture options, window size). Stored beside AppTrace.exe so it travels
/// with the stick; if that folder is read-only it falls back to %LOCALAPPDATA%\AppTrace.
/// </summary>
public sealed class AppSettings
{

    /// <summary>Program last chosen in the New capture dialog.</summary>
    public string Program { get; set; } = "";

    /// <summary>Arguments last used for that program.</summary>
    public string Args { get; set; } = "";

    /// <summary>Folder in which capture folders are created; empty means the default (a <c>traces</c> folder beside the exe).</summary>
    public string CaptureFolder { get; set; } = "";

    /// <summary>Capture option: record DLL loads.</summary>
    public bool LogImages { get; set; } = true;

    /// <summary>Capture option: record every file read and write.</summary>
    public bool LogReadWrite { get; set; } = true;

    /// <summary>Capture option: record close, cleanup and flush operations.</summary>
    public bool LogCloseCleanup { get; set; } = true;

    /// <summary>Capture option: start the program without Administrator rights.</summary>
    public bool DeElevate { get; set; }

    /// <summary>Folder where the Open and Compare file pickers start (the folder holding the most recently used capture).</summary>
    public string OpenFolder { get; set; } = "";

    /// <summary>Trace A last used in the Compare dialog.</summary>
    public string CompareA { get; set; } = "";

    /// <summary>Trace B last used in the Compare dialog.</summary>
    public string CompareB { get; set; } = "";

    /// <summary>Compare option: do not normalise GUIDs.</summary>
    public bool KeepGuids { get; set; }

    /// <summary>Client width of the main window when last closed in the normal state; 0 if never saved.</summary>
    public int WindowWidth { get; set; }

    /// <summary>Client height of the main window when last closed in the normal state; 0 if never saved.</summary>
    public int WindowHeight { get; set; }

    /// <summary>Whether the main window was maximised when last closed.</summary>
    public bool Maximized { get; set; }

    /// <summary>The settings in use by the running program (loaded once at first use).</summary>
    public static AppSettings Current { get; } = Load();

    /// <summary>Places the settings file may live, in order of preference: beside the exe, then the user's local app data.</summary>
    static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "AppTrace.settings.json");

        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppTrace", "settings.json");
    }

    /// <summary>Loads the first readable settings file; falls back to defaults rather than failing.</summary>
    /// <returns>The loaded or default settings.</returns>
    static AppSettings Load()
    {
        foreach (string path in Candidates())
        {
            try
            {
                if (File.Exists(path))
                {
                    var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));

                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch (Exception)
            {
                // Unreadable settings are not worth failing over; start with defaults.
            }
        }

        return new AppSettings();
    }

    /// <summary>The folder that capture folders are created in when nothing has been chosen yet.</summary>
    /// <returns>A <c>traces</c> folder next to the exe.</returns>
    public static string DefaultCaptureFolder()
    {
        return Path.Combine(AppContext.BaseDirectory, "traces");
    }

    /// <summary>Where capture folders go: the remembered choice, else a "traces" folder beside the exe.</summary>
    public string EffectiveCaptureFolder()
    {
        return CaptureFolder.Length > 0 ? CaptureFolder : DefaultCaptureFolder();
    }

    /// <summary>Where file/folder pickers should start: the last folder used, else the captures folder if it exists.</summary>
    /// <param name="preferred">A folder to try first, if any.</param>
    /// <returns>The first existing folder among <paramref name="preferred"/>, <see cref="OpenFolder"/> and the captures folder, or empty text (let Windows choose).</returns>
    public string StartFolder(string? preferred)
    {
        foreach (string? candidate in new[] { preferred, OpenFolder, EffectiveCaptureFolder() })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return "";
    }

    /// <summary>The folder part of a file or folder path, or "" if it can't be worked out.</summary>
    /// <param name="path">A file or folder path (may be blank or malformed).</param>
    /// <returns>The folder itself if <paramref name="path"/> is a folder, else its parent; empty text if that can't be worked out.</returns>
    public static string FolderOf(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "";
            }

            return Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? "");
        }
        catch (ArgumentException)
        {
            return "";
        }
    }

    /// <summary>The folder that holds a trace's run folder (so pickers open beside the other captures), or "".</summary>
    /// <param name="tracePathOrFolder">A trace file or run folder.</param>
    public static string CapturesFolderOf(string tracePathOrFolder)
    {
        try
        {
            string folder = FolderOf(TraceFile.ResolveTracePath(tracePathOrFolder));

            return folder.Length == 0 ? "" : (Path.GetDirectoryName(Path.GetFullPath(folder)) ?? folder);
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>Writes the settings as JSON to the first writable location; silently does nothing if none is (settings are a convenience).</summary>
    public void Save()
    {
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        foreach (string path in Candidates())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                File.WriteAllText(path, json);

                return;
            }
            catch (Exception)
            {
                // Read-only location (a locked-down USB stick, say): try the next one.
            }
        }
    }

}
