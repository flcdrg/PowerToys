// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Windows.Forms;

using MouseWithoutBorders.Class;

// <summary>
//     Single-instance enforcement for the MouseWithoutBorders process.
// </summary>
namespace MouseWithoutBorders.Core;

/// <summary>
/// Guards against multiple instances of MouseWithoutBorders running on the
/// same desktop session.
/// </summary>
internal static class ProcessInstanceGuard
{
    /// <summary>
    /// Holds the named event used to detect a second instance.
    /// Must remain open (not garbage-collected) for the lifetime of the process.
    /// </summary>
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
    internal static EventWaitHandle oneInstanceCheck;
#pragma warning restore SA1307

    /// <summary>
    /// Returns <c>true</c> when another instance of the application is already
    /// running on this machine (detected via its window title).
    /// </summary>
    internal static bool CheckSecondInstance(bool sendMessage = false)
    {
        int h;

        if ((h = NativeMethods.FindWindow(null, Setting.Values.MyID)) > 0)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Creates a named system event scoped to the current desktop session.
    /// Terminates the process if the event already exists (a second instance).
    /// </summary>
    internal static void AssertOneInstancePerDesktopSession()
    {
        string eventName = $"Global\\{Application.ProductName}-{FrmAbout.AssemblyVersion}-{WinAPI.GetMyDesktop()}-{Common.CurrentProcess.SessionId}";
        oneInstanceCheck = new EventWaitHandle(false, EventResetMode.ManualReset, eventName, out bool created);

        if (!created)
        {
            Logger.TelemetryLogTrace($"Second instance found: {eventName}.", SeverityLevel.Warning, true);
            Common.CurrentProcess.KillProcess(true);
        }
    }
}
