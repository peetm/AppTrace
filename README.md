# AppTrace

A Windows desktop app that records what a program does to the **registry**, **file system**, **process list** and
**DLL loads**, shows the recording, and compares recordings made on two machines.

It uses Event Tracing for Windows (the facility Process Monitor and PerfView use), so nothing is injected into
the program being watched.

## Build

Needs the .NET 8 SDK with the Windows desktop workload (it restores `Microsoft.Diagnostics.Tracing.TraceEvent` from NuGet).

    dotnet build
    dotnet run

or run `build.cmd`, which publishes a **single self-contained `AppTrace.exe`** to the `publish` folder inside this
project folder. Copy just that file to each machine; no .NET install is needed there.
(It isn't a fully static native binary: WinForms can't be compiled ahead of time to native code, so the .NET runtime
is bundled inside the exe, and TraceEvent's one required native DLL (`KernelTraceControl.dll`) is embedded in the exe and unpacked to your temp folder the first time you capture.
Expect a file of roughly 70-120 MB.)

All build output stays inside the project folder (`bin\`, `obj\`, `publish\`). If the folder is in OneDrive, those
folders will sync; you can exclude them from OneDrive or delete them after building.

## Using it

**File > Open trace...** (Ctrl+O) opens a `trace.jsonl` from a capture; **Open trace folder...** opens a capture
folder; you can also drag files onto the window. Each opens in its own tab (close with Ctrl+W or middle-click).
Filter by category, process, result (failures only) or free text; click a column header to sort; the pane below
shows the selected event in full; right-click to copy a path or value; **Export view to CSV** saves what you see.

**File > Compare two traces...** (Ctrl+M) asks for trace A and trace B and opens a comparison tab:

- Summary: counts of each kind of difference and the event counts per operation on each side.
- Processes, DLLs / images, Registry, Files: each difference with what A and B did, coloured by kind
  (only on A, only on B, behaved differently, registry value with different data, file with different bytes written).
- **Save report (Markdown)** and **Export differences to CSV** write the results out.

User name, profile folder, computer name, SIDs and GUIDs are normalised before comparing, so `C:\Users\alice` versus
`C:\Users\bob` isn't reported as a difference.

**Capture > New capture...** (Ctrl+N) picks the program and arguments, then records until the program and
everything it started have exited, or you press Stop. Capturing needs Administrator rights; if AppTrace isn't
elevated it offers to restart itself as Administrator (viewing and comparing never need it). Each capture is a folder
(default: a `traces` folder next to `AppTrace.exe`) containing:

| File | Contents |
|---|---|
| `trace.jsonl` | one JSON event per line: time, pid, process, category, operation, path, value name/type/data, result, details |
| `meta.json` | machine, OS, user, profile folder, SID (used to normalise paths when comparing) |
| `summary.txt` | event counts per operation |

## Remembered settings

AppTrace remembers your choices in `AppTrace.settings.json` beside `AppTrace.exe` (or, if that folder is read-only,
in `%LOCALAPPDATA%\AppTrace\settings.json`): the program, arguments, captures folder and capture options, the
folder the Open/Compare pickers start in, the last two traces compared, and the window size. Delete the file to reset.
Dialogs scale with the screen's DPI so text isn't clipped on high-resolution displays.

## Things to know

- **Registry values are read back, not captured.** The kernel registry events give the key, the value name and
  the result, but not the data. AppTrace reads the value from the live registry when it processes the event
  (a moment later), so a value changed again in between will show the newer data.
- **The program runs elevated.** Children inherit AppTrace's Administrator token, which can change behaviour
  (UAC virtualisation, writes to Program Files succeeding). Do the same on both machines for a fair comparison,
  or try "Start the program without Administrator rights", which is experimental (it launches via
  `runas /trustlevel:0x20000`); AppTrace warns if it didn't attach to the real program.
- **Only one "NT Kernel Logger" session can run.** Close Process Monitor, PerfView, xperf etc. first.
- **Some registry keys show as `<unresolved key 0x...>`** when the key was opened before the session started
  and the kernel's rundown didn't name it.
- **File contents aren't captured**, only operations, offsets and sizes. A rename's target name isn't provided
  by the kernel event.
- Very chatty programs produce very large traces (everything is held in memory when viewing). Untick
  "Log every file read and write" for a far smaller capture.
- If your user account differs from the elevated administrator account, `HKCU` read-back will look at the
  administrator's hive.

## Status

Not yet confirmed working on Windows after the single-file fix. If a capture fails, the error window shows the full exception (Copy button) and it is appended to `apptrace-error.log` beside the exe.  The code was compiled in a Linux environment: the capture engine against a stand-in for the
TraceEvent API, and the windows against .NET Framework-style WinForms reference assemblies. The comparison logic was
run on synthetic traces. The TraceEvent member names in `EtwMonitor*.cs` were written from knowledge of the library,
so if the first real build reports an error there, the fix will be a renamed handler or property.

## Code map

Every source file starts with a header (purpose and author) and every type and member has an XML documentation comment.

| File | What it holds |
|---|---|
| `Program.cs` | Entry point: DPI mode, then the main window |
| `MainForm.cs` | Main window: menus, tabs, drag and drop, starting captures, About |
| `CaptureDialog.cs`, `CaptureProgressForm.cs` | Choosing what to capture; the progress window with Stop |
| `CompareDialog.cs` | Choosing the two traces to compare |
| `TraceView.cs` | Tab that shows one trace (filters, sortable grid, detail pane, CSV export) |
| `CompareView.cs` | Comparison tab: summary plus one panel per category of difference |
| `Ui.cs` | Shared helpers: virtual grid, splitter placement, CSV, error dialog |
| `AppSettings.cs` | Remembered folders, options and window size (JSON beside the exe) |
| `CaptureSession.cs` | Runs a capture end to end; elevation helpers; capture settings |
| `EtwMonitor.cs` + `.Registry.cs`, `.Files.cs`, `.Process.cs` | The kernel ETW session and its registry, file and process/DLL handlers |
| `TraceWriter.cs` | Background writer of `trace.jsonl`; reads registry values back |
| `NtPaths.cs` | Kernel path to DOS path conversion; live registry read-back |
| `NativeSupport.cs` | Unpacks and loads the embedded `KernelTraceControl.dll` for the single-file exe |
| `EventRecord.cs`, `RunMeta.cs`, `TraceFile.cs` | The trace data model, machine facts and normaliser, trace loading |
| `Comparer.cs` | Compares two traces and writes the Markdown report |

## Author

Dr Peet Morris (pm@cslab.com)
