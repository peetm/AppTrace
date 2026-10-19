// AppTrace
// File:    MainForm.cs
// Purpose: The main window: menus, tabs for traces and comparisons, drag and drop, capture and About.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace AppTrace;

/// <summary>The application window. Each opened trace or comparison is a tab; captures are started from the Capture menu.</summary>
public sealed class MainForm : Form
{

    /// <summary>One tab per open trace or comparison.</summary>
    readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };

    /// <summary>Instructions shown while no tab is open.</summary>
    readonly Label _empty = new Label
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI", 11f),
        Text = "File > Open trace...  to look at a capture\r\nFile > Compare two traces...  to see what differs between two machines\r\nCapture > New capture...  to record a program"
    };

    /// <summary>Status bar text (progress and results).</summary>
    readonly ToolStripStatusLabel _status = new ToolStripStatusLabel("Ready");

    /// <summary>Settings of the most recent capture in this run, used to pre-fill the next one.</summary>
    CaptureSettings? _lastCapture;

    /// <summary>Builds the window, restores its saved size, and opens any traces given on the command line once it is shown.</summary>
    /// <param name="pathsToOpen">Trace files or run folders to open at startup (may be empty).</param>
    public MainForm(IEnumerable<string> pathsToOpen)
    {
        Text = "AppTrace" + (Elevation.IsElevated() ? " (Administrator)" : "");
        ClientSize = new Size(1280, 760);
        StartPosition = FormStartPosition.CenterScreen;

        RestoreWindow();

        FormClosing += (sender, e) => SaveWindow();

        AllowDrop = true;

        var menu = BuildMenu();

        var statusBar = new StatusStrip();

        statusBar.Items.Add(_status);

        // Fill first, then the edge-docked controls
        Controls.Add(_tabs);
        Controls.Add(_empty);
        Controls.Add(statusBar);
        Controls.Add(menu);

        MainMenuStrip = menu;

        _empty.BringToFront();

        _tabs.MouseDown += OnTabMouseDown;

        DragEnter += (sender, e) =>
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
        };

        DragDrop += async (sender, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped)
            {
                foreach (string path in dropped)
                {
                    await OpenTraceAsync(path);
                }
            }
        };

        var startup = pathsToOpen.ToList();

        Shown += async (sender, e) =>
        {
            foreach (string path in startup)
            {
                await OpenTraceAsync(path);
            }
        };

        UpdateEmptyState();
    }

    /// <summary>Creates the File, Capture and Help menus with their shortcuts.</summary>
    MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&File");

        file.DropDownItems.Add(Item("&Open trace...", Keys.Control | Keys.O, async () => await OnOpenTrace()));
        file.DropDownItems.Add(Item("Open trace &folder...", Keys.None, async () => await OnOpenFolder()));
        file.DropDownItems.Add(Item("&Compare two traces...", Keys.Control | Keys.M, async () => await OnCompare()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("Close &tab", Keys.Control | Keys.W, () => CloseCurrentTab()));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("E&xit", Keys.None, () => Close()));

        var capture = new ToolStripMenuItem("&Capture");

        capture.DropDownItems.Add(Item("&New capture...", Keys.Control | Keys.N, async () => await OnNewCapture()));
        capture.DropDownItems.Add(Item("Show captures &folder", Keys.None, () => ShowCapturesFolder()));

        var help = new ToolStripMenuItem("&Help");

        help.DropDownItems.Add(Item("&About AppTrace", Keys.None, () => ShowAbout()));

        menu.Items.Add(file);
        menu.Items.Add(capture);
        menu.Items.Add(help);

        return menu;
    }

    /// <summary>Creates a menu item.</summary>
    /// <param name="text">Caption (use &amp; for the access key).</param>
    /// <param name="shortcut">Keyboard shortcut, or <see cref="Keys.None"/>.</param>
    /// <param name="click">What to do when chosen.</param>
    static ToolStripMenuItem Item(string text, Keys shortcut, Action click)
    {
        var item = new ToolStripMenuItem(text);

        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
        }

        item.Click += (sender, e) => click();

        return item;
    }

    /// <summary>Shows the instructions when there are no tabs, otherwise the tab control.</summary>
    void UpdateEmptyState()
    {
        _empty.Visible = _tabs.TabPages.Count == 0;

        _tabs.Visible = _tabs.TabPages.Count > 0;
    }

    /// <summary>Adds a tab holding a view and selects it.</summary>
    /// <param name="title">Tab caption.</param>
    /// <param name="content">The control to show in the tab.</param>
    void AddTab(string title, Control content)
    {
        var page = new TabPage(title);

        page.Controls.Add(content);

        _tabs.TabPages.Add(page);

        _tabs.SelectedTab = page;

        UpdateEmptyState();
    }

    /// <summary>Closes the selected tab, if any.</summary>
    void CloseCurrentTab()
    {
        if (_tabs.SelectedTab != null)
        {
            CloseTab(_tabs.SelectedTab);
        }
    }

    /// <summary>Closes a tab and releases its view.</summary>
    /// <param name="page">The tab to close.</param>
    void CloseTab(TabPage page)
    {
        _tabs.TabPages.Remove(page);

        page.Dispose();

        UpdateEmptyState();
    }

    /// <summary>Closes the tab that is middle-clicked.</summary>
    void OnTabMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Middle)
        {
            return;
        }

        for (int i = 0; i < _tabs.TabPages.Count; i = i + 1)
        {
            if (_tabs.GetTabRect(i).Contains(e.Location))
            {
                CloseTab(_tabs.TabPages[i]);

                return;
            }
        }
    }

    /// <summary>File > Open trace: picks one or more trace files (starting in the remembered folder) and opens each.</summary>
    async Task OnOpenTrace()
    {
        using (var dialog = new OpenFileDialog
        {
            Title = "Open trace",
            Filter = "Trace (trace.jsonl)|trace.jsonl|JSON lines (*.jsonl)|*.jsonl|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = true,
            InitialDirectory = AppSettings.Current.StartFolder(null)
        })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            foreach (string path in dialog.FileNames)
            {
                await OpenTraceAsync(path);
            }
        }
    }

    /// <summary>File > Open trace folder: picks a capture folder and opens it.</summary>
    async Task OnOpenFolder()
    {
        using (var dialog = new FolderBrowserDialog { Description = "Choose a capture folder (the one containing trace.jsonl)", SelectedPath = AppSettings.Current.StartFolder(null) })
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                await OpenTraceAsync(dialog.SelectedPath);
            }
        }
    }

    /// <summary>Loads a trace on a background thread, remembers its folder, and shows it in a new tab.</summary>
    /// <param name="path">A trace file or run folder.</param>
    async Task OpenTraceAsync(string path)
    {
        Busy("Loading " + path + " ...");

        try
        {
            RememberFolder(path);

            var progress = new Progress<long>(lines => _status.Text = "Loading... " + lines.ToString("N0") + " lines");

            LoadedTrace trace = await Task.Run(() => TraceFile.Load(path, progress));

            AddTab(trace.DisplayName, new TraceView(trace));

            _status.Text = trace.Events.Count.ToString("N0") + " events loaded from " + trace.TracePath;
        }
        catch (Exception ex)
        {
            _status.Text = "Ready";

            Ui.ShowException(this, "Could not open " + path, ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>File > Compare: asks for two traces (offering the current tab's trace as A) and compares them.</summary>
    async Task OnCompare()
    {
        string? current = (_tabs.SelectedTab?.Controls.OfType<TraceView>().FirstOrDefault())?.Trace.TracePath;

        using (var dialog = new CompareDialog(current))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            await CompareAsync(dialog.PathA, dialog.PathB, dialog.KeepGuids);
        }
    }

    /// <summary>Loads both traces and compares them on background threads, then shows the result in a new tab.</summary>
    /// <param name="pathA">Trace A (file or folder).</param>
    /// <param name="pathB">Trace B (file or folder).</param>
    /// <param name="keepGuids">True to treat differing GUIDs as differences.</param>
    async Task CompareAsync(string pathA, string pathB, bool keepGuids)
    {
        Busy("Loading both traces...");

        try
        {
            var progress = new Progress<long>(lines => _status.Text = "Loading... " + lines.ToString("N0") + " lines");

            LoadedTrace a = await Task.Run(() => TraceFile.Load(pathA, progress));

            LoadedTrace b = await Task.Run(() => TraceFile.Load(pathB, progress));

            _status.Text = "Comparing...";

            CompareResult result = await Task.Run(() => Comparer.Compare(a, b, !keepGuids));

            string nameA = a.Meta.MachineName.Length > 0 ? a.Meta.MachineName : a.DisplayName;

            string nameB = b.Meta.MachineName.Length > 0 ? b.Meta.MachineName : b.DisplayName;

            AddTab("Compare: " + nameA + " vs " + nameB, new CompareView(result));

            _status.Text = result.Rows.Count.ToString("N0") + " differences found";
        }
        catch (Exception ex)
        {
            _status.Text = "Ready";

            Ui.ShowException(this, "Could not compare the traces.", ex);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>
    /// Capture > New capture: offers to restart as Administrator if needed, asks what to capture, runs it with a progress
    /// window, then opens the result. Failures show the full exception.
    /// </summary>
    async Task OnNewCapture()
    {
        if (!Elevation.IsElevated())
        {
            DialogResult answer = MessageBox.Show(this,
                "Capturing uses the Windows kernel trace, which needs Administrator rights.\r\n\r\nRestart AppTrace as Administrator now?",
                "AppTrace", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer == DialogResult.Yes && Elevation.RelaunchElevated())
            {
                Close();
            }

            return;
        }

        using (var dialog = new CaptureDialog(_lastCapture))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _lastCapture = dialog.Settings;
        }

        using (var progress = new CaptureProgressForm(_lastCapture))
        {
            DialogResult result = progress.ShowDialog(this);

            if (result != DialogResult.OK)
            {
                Ui.ShowException(this,
                    "The capture failed. If the message mentions the kernel logger, close other tools that use the 'NT Kernel Logger' session (Process Monitor, PerfView, xperf) and try again.",
                    progress.Error ?? new InvalidOperationException("Unknown error"));

                return;
            }

            if (progress.Warning.Length > 0)
            {
                MessageBox.Show(this, progress.Warning, "AppTrace", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            await OpenTraceAsync(progress.RunFolder);
        }
    }

    /// <summary>Remembers the folder that holds an opened trace's run folder, so file pickers start there next time.</summary>
    /// <param name="path">The trace file or run folder that was opened.</param>
    static void RememberFolder(string path)
    {
        string captures = AppSettings.CapturesFolderOf(path);

        if (captures.Length > 0 && captures != AppSettings.Current.OpenFolder)
        {
            AppSettings.Current.OpenFolder = captures;

            AppSettings.Current.Save();
        }
    }

    /// <summary>Applies the saved window size and maximised state (never larger than the screen).</summary>
    void RestoreWindow()
    {
        AppSettings saved = AppSettings.Current;

        if (saved.WindowWidth < 600 || saved.WindowHeight < 400)
        {
            return;
        }

        var bounds = new Rectangle(0, 0, saved.WindowWidth, saved.WindowHeight);

        Rectangle screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);

        // Never restore larger than the screen (e.g. settings carried over from a bigger display)
        ClientSize = new Size(Math.Min(bounds.Width, screen.Width), Math.Min(bounds.Height, screen.Height - 80));

        if (saved.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    /// <summary>Saves the window size and state, and flushes the settings to disk.</summary>
    void SaveWindow()
    {
        AppSettings saved = AppSettings.Current;

        saved.Maximized = WindowState == FormWindowState.Maximized;

        if (WindowState == FormWindowState.Normal)
        {
            saved.WindowWidth = ClientSize.Width;
            saved.WindowHeight = ClientSize.Height;
        }

        saved.Save();
    }

    /// <summary>Opens the captures folder in Explorer, creating it if necessary.</summary>
    void ShowCapturesFolder()
    {
        string folder = _lastCapture?.OutputRoot ?? AppSettings.Current.EffectiveCaptureFolder();

        Directory.CreateDirectory(folder);

        Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Shows a status message and the wait cursor.</summary>
    /// <param name="message">Status bar text.</param>
    void Busy(string message)
    {
        _status.Text = message;

        UseWaitCursor = true;
    }

    /// <summary>Shows the About box (what the program does, where settings live, and the author).</summary>
    void ShowAbout()
    {
        MessageBox.Show(this,
            "AppTrace records a program's registry, file, process and DLL activity (using Windows kernel event tracing) "
            + "and compares recordings from two machines.\r\n\r\n"
            + "Registry values are read back from the live registry just after each event, because the kernel event does not carry the data.\r\n"
            + "File contents are not recorded, only operations, offsets and sizes.\r\n"
            + "Capturing needs Administrator rights; viewing and comparing do not.\r\n\r\n"
            + "Tip: middle-click a tab (or Ctrl+W) to close it. You can also drag trace files onto the window.\r\n\r\n"
            + "Your choices (folders, last program, options, window size) are remembered in AppTrace.settings.json beside the program.\r\n\r\n"
            + "Written by Dr Peet Morris (pm@cslab.com), 2026.",
            "About AppTrace", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

}
