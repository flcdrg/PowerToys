// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Threading;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Telemetry;
using MouseWithoutBorders.Class;

// <summary>
//     Machine setup/switching implementation.
// </summary>
// <history>
//     2008 created by Truong Do (ductdo).
//     2009-... modified by Truong Do (TruongDo).
//     2023- Included in PowerToys.
// </history>
namespace MouseWithoutBorders.Core;

internal static class MachineStuff
{
    private static readonly Lock McMatrixLock = new();

    internal const byte MAX_MACHINE = 4;
    internal const long HEARTBEAT_TIMEOUT = 1500000; // 30 Mins

#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
    internal static ID desMachineID;
#pragma warning restore SA1307
#pragma warning disable SA1306 // Field should begin with a lower-case letter
    internal static string DesMachineName = string.Empty;
#pragma warning restore SA1306
#pragma warning disable SA1307 // Accessible fields should begin with upper-case letter
    internal static ID newDesMachineID;
    internal static ID newDesMachineIdEx;
    internal static ID dropMachineID;
    internal static long lastJump = Common.GetTick();
    internal static MyRectangle desktopBounds = new();
    internal static MyRectangle primaryScreenBounds = new();
#pragma warning restore SA1307
    private static MachinePool _machinePool;

    internal static MachinePool MachinePool
    {
        get
        {
            _machinePool ??= new MachinePool();
            return _machinePool;
        }
    }

    private static readonly MonitorLayoutNavigator _monitorLayoutNavigator = new MonitorLayoutNavigator();
    private static readonly DeviceLayoutNavigator _deviceLayoutNavigator = new DeviceLayoutNavigator();
    private static ILayoutNavigator _layoutNavigator = _deviceLayoutNavigator;

    internal static void RebuildMonitorLayout()
    {
        bool useLayout = Setting.Values.UseMonitorLayout;
        var layout = Setting.Values.MonitorLayout;
        int count = layout?.Count ?? 0;
        Logger.LogDebug($"RebuildMonitorLayout: UseMonitorLayout={useLayout}, MonitorLayout count={count}, IsMonitorLayoutEnabled={Setting.IsMonitorLayoutEnabled}");

        if (Setting.IsMonitorLayoutEnabled)
        {
            foreach (var m in layout)
            {
                Logger.LogDebug($"  Monitor: {m.MachineName}|{m.MonitorId} at ({m.X},{m.Y}) size {m.Width}x{m.Height}");
            }

            var patchedLayout = MonitorLayoutPatcher.Patch(layout, Common.MachineName, WinAPI.PhysicalMonitors, desktopBounds);
            _monitorLayoutNavigator.OnLayoutChanged(patchedLayout);
            _layoutNavigator = _monitorLayoutNavigator;
            Logger.LogDebug("RebuildMonitorLayout: adjacency rebuilt.");
        }
        else
        {
            Logger.LogDebug("RebuildMonitorLayout: monitor layout disabled, clearing adjacency");
            _monitorLayoutNavigator.OnLayoutChanged(null);
            _layoutNavigator = _deviceLayoutNavigator;
        }
    }

    /// <summary>
    /// Rebuilds the monitor adjacency snapshot from an externally supplied layout (e.g.
    /// received via the MonitorMetadata side-channel) WITHOUT touching
    /// <see cref="Setting.Values.MonitorLayout"/> or saving to disk.
    /// <para>
    /// Saving a peer's canvas coordinates to the local settings.json would cause layout
    /// thrashing: the two machines use different canvas origins, so each machine's stored
    /// layout conflicts with the other's, causing the adjacency to oscillate between them
    /// on every reconnect.
    /// </para>
    /// </summary>
    internal static void RebuildMonitorLayoutFromList(List<MonitorLayoutInfo> layout)
    {
        if (layout == null || layout.Count == 0)
        {
            return;
        }

        Logger.LogDebug($"RebuildMonitorLayoutFromList: {layout.Count} monitors");
        foreach (var m in layout)
        {
            Logger.LogDebug($"  Monitor: {m.MachineName}|{m.MonitorId} at ({m.X},{m.Y}) size {m.Width}x{m.Height}");
        }

        var patchedLayout = MonitorLayoutPatcher.Patch(layout, Common.MachineName, WinAPI.PhysicalMonitors, desktopBounds);
        _monitorLayoutNavigator.OnLayoutChanged(patchedLayout);
        Logger.LogDebug("RebuildMonitorLayoutFromList: adjacency rebuilt.");
    }

    /// <summary>
    /// Updates <see cref="Setting.Values.MonitorLayout"/> from an authoritative full layout
    /// received from a connected peer so every machine renders the same shared canvas in
    /// Settings UI. Machines not present in <paramref name="receivedLayout"/> are preserved.
    /// </summary>
    internal static void UpdateRemoteMachineLayoutsInSettings(List<MonitorLayoutInfo> receivedLayout)
    {
        if (receivedLayout == null || receivedLayout.Count == 0)
        {
            return;
        }

        // Work on a copy so we don't mutate the backing list under another thread's lock.
        var stored = new List<MonitorLayoutInfo>(Setting.Values.MonitorLayout ?? new List<MonitorLayoutInfo>());
        if (stored.Count == 0)
        {
            // No local layout yet — accept the authoritative full layout directly.
            var initial = receivedLayout
                .Where(m => !string.IsNullOrWhiteSpace(m.MachineName))
                .Select(m => new MonitorLayoutInfo
                {
                    MachineName = m.MachineName,
                    MonitorId = m.MonitorId,
                    X = m.X,
                    Y = m.Y,
                    Width = m.Width,
                    Height = m.Height,
                    IsPrimary = m.IsPrimary,
                })
                .ToList();
            if (initial.Count > 0)
            {
                Setting.Values.MonitorLayout = initial;
                Logger.LogDebug($"UpdateRemoteMachineLayoutsInSettings: initialized with {initial.Count} monitors from authoritative layout.");
            }

            return;
        }

        var receivedMachineNames = new HashSet<string>(
            receivedLayout
                .Select(m => m.MachineName)
                .Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);

        if (receivedMachineNames.Count == 0)
        {
            return;
        }

        var merged = receivedLayout
            .Where(m => !string.IsNullOrWhiteSpace(m.MachineName))
            .Select(m => new MonitorLayoutInfo
            {
                MachineName = m.MachineName,
                MonitorId = m.MonitorId,
                X = m.X,
                Y = m.Y,
                Width = m.Width,
                Height = m.Height,
                IsPrimary = m.IsPrimary,
            })
            .ToList();

        merged.AddRange(stored.Where(m => !receivedMachineNames.Contains(m.MachineName ?? string.Empty)));

        if (!SettingMonitorLayoutsEquivalent(stored, merged))
        {
            Setting.Values.MonitorLayout = merged;
            Logger.LogDebug($"UpdateRemoteMachineLayoutsInSettings: updated {receivedMachineNames.Count} machine(s) from authoritative full layout.");
        }
    }

    private static bool SettingMonitorLayoutsEquivalent(
        List<MonitorLayoutInfo> left,
        List<MonitorLayoutInfo> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left == null || right == null || left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            var a = left[i];
            var b = right[i];

            if (!string.Equals(a.MachineName, b.MachineName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(a.MonitorId, b.MonitorId, StringComparison.Ordinal)
                || a.X != b.X
                || a.Y != b.Y
                || a.Width != b.Width
                || a.Height != b.Height
                || a.IsPrimary != b.IsPrimary)
            {
                return false;
            }
        }

        return true;
    }

    internal static List<MonitorLayoutInfo> PatchLocalMonitorCoordinatesForTesting(
        List<MonitorLayoutInfo> layout,
        string localMachineName,
        List<WinAPI.PhysicalMonitorInfo> physicalMonitors,
        MyRectangle currentDesktopBounds)
    {
        return MonitorLayoutPatcher.Patch(layout, localMachineName, physicalMonitors, currentDesktopBounds);
    }

    internal static MyRectangle PrimaryScreenBounds => MachineStuff.primaryScreenBounds;

#pragma warning disable SA1306 // Field should begin with a lower-case letter
    internal static MouseLocation SwitchLocation = new();
#pragma warning restore SA1306

    internal static ID NewDesMachineID
    {
        get => MachineStuff.newDesMachineID;
        set => MachineStuff.newDesMachineID = value;
    }

    internal static MyRectangle DesktopBounds => MachineStuff.desktopBounds;

#if OLD_VERSION
    static bool MoveToMyNeighbourIfNeeded(int x, int y)
    {
        if (Math.Abs(x) > 10) LastX = x;
        if (Math.Abs(y) > 10) LastY = y;
        if (GetTick() - lastJump < 500 || desMachineID == IP.ALL) return false;
        if (desMachineID == machineID)
        {
            if (x < desktopBounds.Left + skipPixels) return MoveLeft(x, y, x - desktopBounds.Left, 0);
        }
        else
        {
            if (x < primaryScreenBounds.Left + skipPixels)
            {
                if (MoveLeft(x, y, x - primaryScreenBounds.Left, 0))
                {
                    return true;
                }
                else
                {
                    if (desktopBounds.Left < primaryScreenBounds.Left)
                    {
                        RequestedX_Ex = primaryScreenBounds.Left;
                        RequestedY_Ex = y;
                        return true;
                    }
                }
            }
        }

        if (desMachineID == machineID)
        {
            if (x > desktopBounds.Right - skipPixels) return MoveRight(x, y, x - desktopBounds.Right, 0);
        }
        else
        {
            if (x > primaryScreenBounds.Right - skipPixels)
            {
                if (MoveRight(x, y, x - primaryScreenBounds.Right, 0))
                {
                    return true;
                }
                else
                {
                    if (desktopBounds.Right > primaryScreenBounds.Right)
                    {
                        RequestedX_Ex = primaryScreenBounds.Right;
                        RequestedY_Ex = y;
                        return true;
                    }
                }
            }
        }

        if (desMachineID == machineID)
        {
            if (y < desktopBounds.Top + skipPixels) return MoveUp(x, y, 0, y - desktopBounds.Top);
        }
        else
        {
            if (y < primaryScreenBounds.Top + skipPixels)
            {
                if (MoveUp(x, y, 0, y - primaryScreenBounds.Top))
                {
                    return true;
                }
                else
                {
                    if (desktopBounds.Top < primaryScreenBounds.Top)
                    {
                        RequestedX_Ex = x;
                        RequestedY_Ex = primaryScreenBounds.Top;
                        return true;
                    }
                }
            }
        }

        if (desMachineID == machineID)
        {
            if (y > desktopBounds.Bottom - skipPixels) return MoveDown(x, y, 0, y - desktopBounds.Bottom);
        }
        else
        {
            if (y > primaryScreenBounds.Bottom - skipPixels)
            {
                if (MoveDown(x, y, 0, y - primaryScreenBounds.Bottom))
                {
                    return true;
                }
                else
                {
                    if (desktopBounds.Bottom > primaryScreenBounds.Bottom)
                    {
                        RequestedX_Ex = x;
                        RequestedY_Ex = primaryScreenBounds.Bottom;
                        return true;
                    }
                }
            }
        }

        return false;
    }
#else

    /* Let's say we have 3 machines A, B, and C. A is the controller machine.
     * (x, y) is the current Mouse position in pixel.
     * If Setting.Values.MoveMouseRelatively then (x, y) can be from any machine having the value bounded by desktopBounds (can be negative)
     * Else (x, y) is from the controller machine which is bounded by ONLY primaryScreenBounds (>=0);
     *
     * The return point is from 0 to 65535 which is then mapped to the desktop of the new controlled machine by the SendInput method.
     *  Let's say user is switching from machine B to machine C:
     *      If Setting.Values.MoveMouseRelatively the this method is called by B and the return point is calculated by B and sent back to A, A will use it to move Mouse to the right position when switching to C.
     *      Else this method is called by A and the return point is calculated by A.
     * */

    internal static Point MoveToMyNeighbourIfNeeded(int x, int y, ID desMachineID)
    {
        newDesMachineIdEx = desMachineID;

        if (Math.Abs(x) > 10)
        {
            Common.LastX = x;
        }

        if (Math.Abs(y) > 10)
        {
            Common.LastY = y;
        }

        if ((Common.GetTick() - lastJump < 100) || desMachineID == ID.ALL)
        {
            return Point.Empty;
        }

        if (Setting.Values.BlockMouseAtCorners)
        {
            lock (WinAPI.SensitivePoints)
            {
                foreach (Point p in WinAPI.SensitivePoints)
                {
                    if (Math.Abs(p.X - x) < 100 && Math.Abs(p.Y - y) < 100)
                    {
                        return Point.Empty;
                    }
                }
            }
        }

        /* If Mouse is moving in the controller machine and this method is called by the controller machine.
         * Or if Mouse is moving in the controlled machine and this method is called by the controlled machine and Setting.Values.MoveMouseRelative.
         * */
        if (desMachineID == Common.MachineID)
        {
            string currentMachine = NameFromID(desMachineID);
            if (currentMachine != null)
            {
                var transition = _layoutNavigator.CheckLocalMachineEdge(x, y, currentMachine);
                if (transition.HasValue)
                {
                    newDesMachineIdEx = transition.Value.TargetMachineId;
                    return transition.Value.LandingPosition;
                }
            }
        }

        /* If Mouse is moving in the controlled machine and this method is called by the controller machine and !Setting.Values.MoveMouseRelative.
         * Mouse location is scaled from the primary screen bound of the controller machine regardless of how many monitors the controlled machine may have.
         * */
        else
        {
            MoveDirection? dir = null;
            if (x < primaryScreenBounds.Left + NavigationMath.SkipPixels)
            {
                dir = MoveDirection.Left;
            }
            else if (x >= primaryScreenBounds.Right - NavigationMath.SkipPixels)
            {
                dir = MoveDirection.Right;
            }
            else if (y < primaryScreenBounds.Top + NavigationMath.SkipPixels)
            {
                dir = MoveDirection.Up;
            }
            else if (y >= primaryScreenBounds.Bottom - NavigationMath.SkipPixels)
            {
                dir = MoveDirection.Down;
            }

            if (dir.HasValue)
            {
                // Let the active navigator try first (monitor layout performs a canvas-space
                // lookup; device matrix returns null and we fall through to the matrix path).
                string remoteName = NameFromID(desMachineID);
                var remoteTransition = _layoutNavigator.TryResolveRemoteEdge(dir.Value, remoteName, x, y);
                if (remoteTransition.HasValue)
                {
                    newDesMachineIdEx = remoteTransition.Value.TargetMachineId;
                    return remoteTransition.Value.LandingPosition;
                }

                // Matrix fallback (also used when monitor layout did not resolve).
                return dir.Value switch
                {
                    MoveDirection.Left => MoveLeft(x, y),
                    MoveDirection.Right => MoveRight(x, y),
                    MoveDirection.Up => MoveUp(x, y),
                    MoveDirection.Down => MoveDown(x, y),
                    _ => Point.Empty,
                };
            }
        }

        return Point.Empty;
    }

#endif

    private static Point MoveRight(int x, int y, string localMonitorId = null, int physMonitorLeft = 0, int physMonitorTop = 0)
    {
        string currentMachine = NameFromID(desMachineID);
        if (currentMachine == null)
        {
            return Point.Empty;
        }

        var t = _layoutNavigator.TryResolveLocalEdge(MoveDirection.Right, currentMachine, x, y, localMonitorId, physMonitorLeft, physMonitorTop);
        if (!t.HasValue)
        {
            return Point.Empty;
        }

        newDesMachineIdEx = t.Value.TargetMachineId;
        return t.Value.LandingPosition;
    }

    private static Point MoveLeft(int x, int y, string localMonitorId = null, int physMonitorLeft = 0, int physMonitorTop = 0)
    {
        string currentMachine = NameFromID(desMachineID);
        if (currentMachine == null)
        {
            return Point.Empty;
        }

        var t = _layoutNavigator.TryResolveLocalEdge(MoveDirection.Left, currentMachine, x, y, localMonitorId, physMonitorLeft, physMonitorTop);
        if (!t.HasValue)
        {
            return Point.Empty;
        }

        newDesMachineIdEx = t.Value.TargetMachineId;
        return t.Value.LandingPosition;
    }

    private static Point MoveUp(int x, int y, string localMonitorId = null, int physMonitorLeft = 0, int physMonitorTop = 0)
    {
        string currentMachine = NameFromID(desMachineID);
        if (currentMachine == null)
        {
            return Point.Empty;
        }

        var t = _layoutNavigator.TryResolveLocalEdge(MoveDirection.Up, currentMachine, x, y, localMonitorId, physMonitorLeft, physMonitorTop);
        if (!t.HasValue)
        {
            return Point.Empty;
        }

        newDesMachineIdEx = t.Value.TargetMachineId;
        return t.Value.LandingPosition;
    }

    private static Point MoveDown(int x, int y, string localMonitorId = null, int physMonitorLeft = 0, int physMonitorTop = 0)
    {
        string currentMachine = NameFromID(desMachineID);
        if (currentMachine == null)
        {
            return Point.Empty;
        }

        var t = _layoutNavigator.TryResolveLocalEdge(MoveDirection.Down, currentMachine, x, y, localMonitorId, physMonitorLeft, physMonitorTop);
        if (!t.HasValue)
        {
            return Point.Empty;
        }

        newDesMachineIdEx = t.Value.TargetMachineId;
        return t.Value.LandingPosition;
    }

    internal static bool RemoveDeadMachines(ID ip)
    {
        bool rv = false;

        // Here we are removing a dead machine by IP.
        foreach (MachineInf inf in MachineStuff.MachinePool.ListAllMachines())
        {
            if (inf.Id == ip)
            {
                if (MachinePool.SetMachineDisconnected(inf.Name))
                {
                    rv = true;
                }

                Logger.LogDebug("<><><><><>>><><><<><><><><><><><><><><>><><><><><><><><><><><" + inf.Name);
            }
        }

        return rv;
    }

    internal static void RemoveDeadMachines()
    {
        // list of live/dead machines is now automatically up-to-date
        // if it changed we need to update the UI.
        // for now assume it changed.
        // Common.MachinePool.ResetIPAddressesForDeadMachines();
        // DoSomethingInUIThread(UpdateMenu);
        MachineStuff.UpdateMachinePoolStringSetting();

        // Make sure MachinePool still holds this machine.
        if (MachineStuff.MachinePool.LearnMachine(Common.MachineName))
        {
            _ = MachineStuff.MachinePool.TryUpdateMachineID(Common.MachineName, Common.MachineID, false);
        }
    }

    internal static string AddToMachinePool(DATA package)
    {
        // Log("********** AddToMachinePool called: " + package.src.ToString(CultureInfo.InvariantCulture));

        // There should be no duplicates in machine pool.
        string name = package.MachineName;

        // a few things happening here:
        // 1) find a matching machine (by name)
        // 2) update its ID and time
        // 3) logging
        // 4) updating some variables - desMachineID/newDesMachineID
        // 5) return the matched name (trimmed) - only in the event of a match
        if (MachineStuff.MachinePool.TryFindMachineByName(name, out MachineInf machineInfo))
        {
            _ = MachineStuff.MachinePool.TryUpdateMachineID(machineInfo.Name, machineInfo.Id, true);

            _ = MachineStuff.MachinePool.TryUpdateMachineID(machineInfo.Name, package.Src, true);

            if (machineInfo.Name.Equals(DesMachineName, StringComparison.OrdinalIgnoreCase))
            {
                Logger.LogDebug("AddToMachinePool: Des ID updated: " + Common.DesMachineID.ToString() + "/" + package.Src.ToString());
                newDesMachineID = desMachineID = package.Src;
            }

            return machineInfo.Name;
        }
        else
        {
            if (MachineStuff.MachinePool.LearnMachine(name))
            {
                _ = MachineStuff.MachinePool.TryUpdateMachineID(name, package.Src, true);
            }
            else
            {
                Logger.LogDebug("AddToMachinePool: could not add a new machine: " + name);
                return "The 5th machine";
            }
        }

        // if (machineCount != saved)
        {
            // DoSomethingInUIThread(UpdateMenu);
            MachineStuff.UpdateMachinePoolStringSetting();
        }

        // NOTE(yuyoyuppe): automatically active "bidirectional" control between the machines.
        string[] st = new string[MachineStuff.MAX_MACHINE];
        Array.Fill(st, string.Empty);
        var machines = MachineStuff.MachinePool.ListAllMachines();
        for (int i = 0; i < machines.Count; ++i)
        {
            if (machines[i].Id != ID.NONE && machines[i].Id != ID.ALL)
            {
                st[i] = machines[i].Name;
            }
        }

        MachineStuff.MachineMatrix = st;
        Common.ReopenSockets(true);
        MachineStuff.SendMachineMatrix();

        Logger.LogDebug("Machine added: " + name + "/" + package.Src.ToString());
        UpdateClientSockets("AddToMachinePool");
        return name;
    }

    internal static void UpdateClientSockets(string logHeader)
    {
        Logger.LogDebug("UpdateClientSockets: " + logHeader);
        Common.Sk?.UpdateTCPClients();
    }

    private static string[] mcMatrix;

    internal static string[] MachineMatrix
    {
        get
        {
            lock (McMatrixLock)
            {
                if (mcMatrix == null)
                {
                    string s = Setting.Values.MachineMatrixString;

                    if (!string.IsNullOrEmpty(s))
                    {
                        mcMatrix = s.Split(new char[] { ',' });

                        if (mcMatrix == null || mcMatrix.Length != MAX_MACHINE)
                        {
                            mcMatrix = new string[MAX_MACHINE] { string.Empty, string.Empty, string.Empty, string.Empty };
                        }
                    }
                    else
                    {
                        mcMatrix = new string[MAX_MACHINE] { string.Empty, string.Empty, string.Empty, string.Empty };
                    }
                }

                return mcMatrix;
            }
        }

        set
        {
            lock (McMatrixLock)
            {
                if (value == null)
                {
                    mcMatrix = null; // Force read from registry next time.
                    return;
                }
                else
                {
                    Setting.Values.MachineMatrixString = string.Join(",", mcMatrix = value);
                }
            }

            Common.DoSomethingInUIThread(() =>
            {
                Common.MainForm.ChangeIcon(-1);
                Common.MainForm.UpdateNotifyIcon();
            });
        }
    }

    internal static string[] LiveMachineMatrix
    {
        get
        {
            bool twoRow = Setting.IsMonitorLayoutEnabled || !Setting.Values.MatrixOneRow;
            string[] connectedMachines = twoRow ? MachineMatrix : MachineMatrix.Select(m => Common.IsConnectedTo(IdFromName(m)) ? m : string.Empty).ToArray();
            Logger.LogDebug($"Matrix: {string.Join(",", MachineMatrix)}, Connected: {string.Join(",", connectedMachines)}");

            return connectedMachines;
        }
    }

    internal static void UpdateMachinePoolStringSetting()
    {
        Setting.Values.MachinePoolString = MachineStuff.MachinePool.SerializedAsString();
    }

    internal static void SendMachineMatrix()
    {
        if (MachineMatrix == null)
        {
            return;
        }

        DATA package = new();

        for (int i = 0; i < MachineMatrix.Length; i++)
        {
            package.MachineName = MachineMatrix[i];

            package.Type = PackageType.Matrix
                | (Setting.Values.MatrixCircle ? PackageType.MatrixSwapFlag : 0)
                | (Setting.Values.MatrixOneRow ? 0 : PackageType.MatrixTwoRowFlag);

            package.Src = (ID)(i + 1);
            package.Des = ID.ALL;

            Common.SkSend(package, null, false);

            Logger.LogDebug($"matrixIncludedMachine sent: [{i + 1}]:[{MachineMatrix[i]}]");
        }
    }

    internal static void UpdateMachineMatrix(DATA package)
    {
        uint i = (uint)package.Src;
        string matrixIncludedMachine = package.MachineName;

        if (i is > 0 and <= MAX_MACHINE)
        {
            Logger.LogDebug($"matrixIncludedMachine: [{i}]:[{matrixIncludedMachine}]");

            MachineMatrix[i - 1] = matrixIncludedMachine;

            if (i == MAX_MACHINE)
            {
                Setting.Values.MatrixCircle = (package.Type & PackageType.MatrixSwapFlag) == PackageType.MatrixSwapFlag;

                // When monitor layout routing is active, MatrixOneRow is irrelevant. Ignore the
                // synced value so the user's stored preference is not silently overwritten.
                if (!Setting.IsMonitorLayoutEnabled)
                {
                    Setting.Values.MatrixOneRow = !((package.Type & PackageType.MatrixTwoRowFlag) == PackageType.MatrixTwoRowFlag);
                }

                MachineMatrix = MachineMatrix; // Save

                InitAndCleanup.ReopenSocketDueToReadError = true;

                UpdateClientSockets("UpdateMachineMatrix");

                Setting.Values.Changed = true;
            }
        }
        else
        {
            Logger.LogDebug("Invalid machine Matrix package!");
        }
    }

    internal static void SwitchToMachine(string name)
    {
        ID id = MachineStuff.MachinePool.ResolveID(name);

        if (id != ID.NONE)
        {
            // Ask current machine to hide the Mouse cursor
            if (desMachineID != Common.MachineID)
            {
                Common.SendPackage(desMachineID, PackageType.HideMouse);
            }

            NewDesMachineID = Common.DesMachineID = id;
            SwitchLocation.X = Event.XY_BY_PIXEL + primaryScreenBounds.Left + ((primaryScreenBounds.Right - primaryScreenBounds.Left) / 2);
            SwitchLocation.Y = Event.XY_BY_PIXEL + primaryScreenBounds.Top + ((primaryScreenBounds.Bottom - primaryScreenBounds.Top) / 2);
            SwitchLocation.ResetCount();
            Common.UpdateMultipleModeIconAndMenu();
            Common.HideMouseCursor(false);
            _ = Common.EvSwitch.Set();
        }
    }

    internal static void SwitchToMultipleMode(bool multipleMode, bool centerScreen)
    {
        if (multipleMode)
        {
            PowerToysTelemetry.Log.WriteEvent(new MouseWithoutBorders.Telemetry.MouseWithoutBordersMultipleModeEvent());
            NewDesMachineID = Common.DesMachineID = ID.ALL;
        }
        else
        {
            NewDesMachineID = Common.DesMachineID = Common.MachineID;
        }

        if (centerScreen)
        {
            Common.MoveMouseToCenter();
        }

        InitAndCleanup.ReleaseAllKeys();

        Common.UpdateMultipleModeIconAndMenu();
    }

    internal static ID IdFromName(string name)
    {
        return MachineStuff.MachinePool.ResolveID(name);
    }

    internal static string NameFromID(ID id)
    {
        foreach (MachineInf inf in MachineStuff.MachinePool.TryFindMachineByID(id))
        {
            if (!string.IsNullOrEmpty(inf.Name))
            {
                return inf.Name;
            }
        }

        return null;
    }

    internal static bool InMachineMatrix(string name)
    {
        if (MachineMatrix == null || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (string st in MachineMatrix)
        {
            if (!string.IsNullOrWhiteSpace(st) && st.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static void ClearComputerMatrix()
    {
        MachineStuff.MachineMatrix = new string[MachineStuff.MAX_MACHINE] { Common.MachineName.Trim(), string.Empty, string.Empty, string.Empty };
        MachineStuff.MachinePool.Initialize(new string[] { Common.MachineName });
        MachineStuff.UpdateMachinePoolStringSetting();
    }
}
