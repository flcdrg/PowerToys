// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Windows.Forms;

using Microsoft.PowerToys.Telemetry;
using MouseWithoutBorders.Class;

// <summary>
//     Lifecycle management for the legacy setup / machine-matrix UI forms.
// </summary>
namespace MouseWithoutBorders.Core;

/// <summary>
/// Opens, closes, and shows the legacy Mouse without Borders setup form and
/// machine-matrix window.  All form-lifetime state is encapsulated here so
/// that no other class needs to hold a reference to the form objects.
/// </summary>
internal static class SetupFormManager
{
    private static SettingsForm _settings;

    internal static SettingsForm Settings
    {
        get => _settings;
        set => _settings = value;
    }

    internal static void ShowSetupForm(bool reopenSockets = false)
    {
        Logger.LogDebug("========== BEGIN THE SETUP EXPERIENCE ==========", true);
        Setting.Values.MyKey = Encryption.MyKey = Encryption.CreateRandomKey();
        Encryption.GeneratedKey = true;

        if (Process.GetCurrentProcess().SessionId != NativeMethods.WTSGetActiveConsoleSessionId())
        {
            Logger.Log("Not physical console session.");
            _ = MessageBox.Show(
                "Please run the program in the physical console session.\r\nThe program does not work in a remote desktop or virtual machine session.",
                Application.ProductName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Stop);
            return;
        }

        if (_settings == null)
        {
            _settings = new SettingsForm();
            _settings.Show();
        }
        else
        {
            _settings.Close();
            Common.MMSleep(0.3);
            _settings = new SettingsForm();
            _settings.Show();
        }

        if (reopenSockets)
        {
            Common.ReopenSockets(true);
        }
    }

    internal static void CloseSetupForm()
    {
        if (_settings != null)
        {
            _settings.Close();
            _settings = null;
        }
    }

    internal static void ShowMachineMatrix()
    {
        if (!Setting.Values.ShowOriginalUI)
        {
            return;
        }

        if (Process.GetCurrentProcess().SessionId != NativeMethods.WTSGetActiveConsoleSessionId())
        {
            Common.ShowToolTip(Application.ProductName + " cannot be used in a remote desktop or virtual machine session.", 5000);
        }

#if NEW_SETTINGS_FORM
        Common.ShowSetupForm();
#else
        if (Setting.Values.FirstRun && !Common.AtLeastOneSocketConnected())
        {
            SetupFormManager.ShowSetupForm();
        }
        else
        {
            PowerToysTelemetry.Log.WriteEvent(new MouseWithoutBorders.Telemetry.MouseWithoutBordersOldUIOpenedEvent());

            if (Common.MatrixForm == null)
            {
                Common.MatrixForm = new FrmMatrix();
                Common.MatrixForm.Show();

                if (Common.MainForm != null)
                {
                    Common.MainForm.NotifyIcon.Visible = false;
                    Common.MainForm.NotifyIcon.Visible = Setting.Values.ShowOriginalUI;
                }
            }
            else
            {
                Common.MatrixForm.WindowState = FormWindowState.Normal;
                Common.MatrixForm.Activate();
            }
        }
#endif
    }
}
