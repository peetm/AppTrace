// AppTrace
// File:    Program.cs
// Purpose: Application entry point.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Windows.Forms;

namespace AppTrace;

/// <summary>Holds the program's entry point.</summary>
public static class Program
{

    /// <summary>Starts the application and shows the main window.</summary>
    /// <param name="args">Optional traces (files or run folders) to open at startup.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        Application.EnableVisualStyles();

        Application.SetCompatibleTextRenderingDefault(false);

        // Per-monitor DPI awareness, so the windows stay sharp and correctly sized on high-resolution displays
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        Application.Run(new MainForm(args));
    }

}
