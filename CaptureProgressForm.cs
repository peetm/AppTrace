// AppTrace
// File:    CaptureProgressForm.cs
// Purpose: Modal window shown while a capture runs; has a Stop button.
// Author:  Dr Peet Morris (pm@cslab.com)
// Created: October 2026

using System.Drawing;
using System.Windows.Forms;

namespace AppTrace;

/// <summary>Runs a capture and shows its progress. Closes itself when the capture ends.</summary>
public sealed class CaptureProgressForm : Form
{

    /// <summary>What is being captured.</summary>
    readonly CaptureSettings _settings;

    /// <summary>The running capture.</summary>
    readonly CaptureSession _session = new CaptureSession();

    /// <summary>Signalled when the user presses Stop or tries to close the window.</summary>
    readonly CancellationTokenSource _cancel = new CancellationTokenSource();

    /// <summary>Shows the capture's current phase.</summary>
    readonly Label _state = new Label { Left = 16, Top = 16, Width = 440, Height = 20 };

    /// <summary>Shows event and process counts.</summary>
    readonly Label _counts = new Label { Left = 16, Top = 44, Width = 440, Height = 20 };

    /// <summary>Explains when the capture will stop.</summary>
    readonly Label _hint = new Label { Left = 16, Top = 72, Width = 440, Height = 40 };

    /// <summary>Ends the capture early.</summary>
    readonly Button _stop = new Button { Text = "Stop capture", Left = 346, Top = 120, Width = 110 };

    /// <summary>Refreshes the labels four times a second.</summary>
    readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 250 };

    /// <summary>True while the capture is in progress; closing the window is then treated as Stop.</summary>
    bool _running;

    /// <summary>Creates the window; the capture starts when it is shown.</summary>
    /// <param name="settings">What to capture.</param>
    public CaptureProgressForm(CaptureSettings settings)
    {
        _settings = settings;

        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Capturing " + Path.GetFileName(settings.Exe);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(476, 160);

        _hint.Text = "Capturing stops when the program and everything it started have exited, or when you press Stop.";

        Controls.Add(_state);
        Controls.Add(_counts);
        Controls.Add(_hint);
        Controls.Add(_stop);

        _stop.Click += (sender, e) => RequestStop();

        _timer.Tick += (sender, e) =>
        {
            _state.Text = _session.State;

            _counts.Text = _session.EventCount.ToString("N0") + " events captured, " + _session.ProcessCount + " process(es) running";
        };

        FormClosing += (sender, e) =>
        {
            if (_running)
            {
                RequestStop();

                e.Cancel = true;
            }
        };
    }

    /// <summary>Folder of the finished capture (valid when the dialog result is OK).</summary>
    public string RunFolder { get; private set; } = "";

    /// <summary>Warning from the capture (dropped events, program not seen, ...); empty if none.</summary>
    public string Warning
    {
        get
        {
            return _session.Warning;
        }
    }

    /// <summary>The failure, if the dialog result is Abort.</summary>
    public Exception? Error { get; private set; }

    /// <summary>Asks the capture to finish early and disables the Stop button.</summary>
    void RequestStop()
    {
        _stop.Enabled = false;

        _state.Text = "Stopping...";

        _cancel.Cancel();
    }

    /// <summary>Starts the capture when the window appears and closes the window when it ends (OK on success, Abort on failure).</summary>
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        _running = true;

        _timer.Start();

        try
        {
            RunFolder = await _session.RunAsync(_settings, _cancel.Token);

            _running = false;

            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            Error = ex;

            _running = false;

            DialogResult = DialogResult.Abort;
        }
        finally
        {
            _timer.Stop();
        }
    }

}
