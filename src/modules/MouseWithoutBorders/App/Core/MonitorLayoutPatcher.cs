// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

using Microsoft.PowerToys.Settings.UI.Library;

// <summary>
//     Pure-function helpers for patching monitor layout coordinates.
// </summary>
namespace MouseWithoutBorders.Core;

/// <summary>
/// Translates stored monitor layout coordinates into physical pixel coordinates
/// for the local machine. All methods are stateless pure functions; they receive
/// every input as a parameter and write to no shared state.
/// </summary>
internal static class MonitorLayoutPatcher
{
    /// <summary>
    /// Replaces local machine monitor coordinates in <paramref name="layout"/> with
    /// actual physical pixel bounds from <paramref name="physicalMonitors"/>,
    /// preserving the offset that positions the local monitor group within the
    /// combined multi-machine layout.
    /// This eliminates canvas round-trip rounding errors that create artificial
    /// gaps between local monitors.
    /// <para>
    /// When Windows has reassigned display device IDs since the layout was saved
    /// (e.g. \\.\DISPLAY3 and \\.\DISPLAY4 swapped), monitors are matched by
    /// resolution and left-to-right position instead of device name.  The
    /// <see cref="MonitorLayoutInfo.MonitorId"/> in each patched entry is set to
    /// the <em>physical</em> device name so that adjacency lookups keyed on the
    /// physical device name work correctly.
    /// </para>
    /// <para>
    /// <see cref="System.Windows.Forms.Screen.AllScreens"/> cannot be used here
    /// because it returns DPI-scaled coordinates in multi-DPI setups, which do not
    /// match the physical pixel coordinates used by cursor hooks.
    /// </para>
    /// </summary>
    internal static List<MonitorLayoutInfo> Patch(
        List<MonitorLayoutInfo> layout,
        string localName,
        List<WinAPI.PhysicalMonitorInfo> physicalMonitors,
        MyRectangle currentDesktopBounds)
    {
        if (layout == null || layout.Count == 0)
        {
            return layout;
        }

        if (string.IsNullOrEmpty(localName))
        {
            return layout;
        }

        if (physicalMonitors == null || physicalMonitors.Count == 0)
        {
            return layout;
        }

        // Collect local monitors from the configured layout.
        var localLayout = new List<MonitorLayoutInfo>();
        foreach (var m in layout)
        {
            if (string.Equals(m.MachineName, localName, StringComparison.OrdinalIgnoreCase))
            {
                localLayout.Add(m);
            }
        }

        if (localLayout.Count == 0)
        {
            return layout;
        }

        // Build a lookup from DeviceName to physical monitor info.
        var physByName = new Dictionary<string, WinAPI.PhysicalMonitorInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var pm in physicalMonitors)
        {
            if (!string.IsNullOrEmpty(pm.DeviceName))
            {
                physByName[pm.DeviceName] = pm;
            }
        }

        // Check if every local monitor's device name matches a physical monitor
        // with the SAME resolution.  If so, device names are stable.
        bool allNamesMatchSize = true;
        foreach (var m in localLayout)
        {
            if (!physByName.TryGetValue(m.MonitorId, out var phys)
                || phys.Width != m.Width || phys.Height != m.Height)
            {
                allNamesMatchSize = false;
                break;
            }
        }

        // configToPhys maps configured MonitorId -> (physical monitor, physical device name).
        Dictionary<string, (WinAPI.PhysicalMonitorInfo Phys, string PhysId)> configToPhys;

        if (allNamesMatchSize)
        {
            // Device names are stable; use direct name lookup.
            configToPhys = new Dictionary<string, (WinAPI.PhysicalMonitorInfo, string)>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in localLayout)
            {
                configToPhys[m.MonitorId] = (physByName[m.MonitorId], m.MonitorId);
            }
        }
        else
        {
            // Device names may have changed; match by size + left-to-right position.
            configToPhys = MatchMonitorsByPositionalOrder(localLayout, physicalMonitors);
            if (configToPhys == null)
            {
                Logger.LogDebug("PatchLocalMonitorCoordinates: size+order matching failed, using configured layout as-is.");
                return layout;
            }

            Logger.LogDebug("PatchLocalMonitorCoordinates: device names changed, using size+order matching.");
        }

        // Find the anchor: first local monitor with a match.
        MonitorLayoutInfo anchorLayout = null;
        WinAPI.PhysicalMonitorInfo anchorPhys = default;
        bool anchorFound = false;
        foreach (var m in localLayout)
        {
            if (configToPhys.TryGetValue(m.MonitorId, out var pair))
            {
                anchorLayout = m;
                anchorPhys = pair.Phys;
                anchorFound = true;
                break;
            }
        }

        if (!anchorFound)
        {
            return layout;
        }

        // Compute the offset between layout space and physical desktop space for
        // the anchor monitor.  All local monitors get shifted by this offset.
        int offsetX = anchorLayout.X - (anchorPhys.Left - currentDesktopBounds.Left);
        int offsetY = anchorLayout.Y - (anchorPhys.Top - currentDesktopBounds.Top);

        var result = new List<MonitorLayoutInfo>(layout.Count);
        foreach (var m in layout)
        {
            if (string.Equals(m.MachineName, localName, StringComparison.OrdinalIgnoreCase)
                && configToPhys.TryGetValue(m.MonitorId, out var pair))
            {
                var phys = pair.Phys;
                var patched = new MonitorLayoutInfo
                {
                    MachineName = m.MachineName,
                    MonitorId = pair.PhysId,
                    X = (phys.Left - currentDesktopBounds.Left) + offsetX,
                    Y = (phys.Top - currentDesktopBounds.Top) + offsetY,
                    Width = phys.Width,
                    Height = phys.Height,
                    IsPrimary = m.IsPrimary,
                };
                Logger.LogDebug($"  Patched: {patched.MachineName}|{patched.MonitorId} (was {m.MonitorId}) ({m.X},{m.Y})->({patched.X},{patched.Y}) size {patched.Width}x{patched.Height}");
                result.Add(patched);
            }
            else
            {
                result.Add(m);
            }
        }

        // After patching local monitors, adjust remote monitors so that edge
        // adjacency with local monitors is preserved.  Without this step, a
        // physical display-configuration change that shifts a local monitor
        // (e.g. DISPLAY4 Y 612→699) would leave remote monitors at stale
        // positions, breaking cross-machine adjacency.
        AdjustRemoteMonitorsForEdgeAlignment(result, layout, localName, configToPhys);

        return result;
    }

    /// <summary>
    /// For every remote monitor that was edge-adjacent to a local monitor in
    /// the stored layout, shifts its position by the same delta that the local
    /// monitor's edge moved during patching.  This keeps cross-machine
    /// adjacency intact when local monitors shift due to physical display
    /// configuration changes.
    /// </summary>
    private static void AdjustRemoteMonitorsForEdgeAlignment(
        List<MonitorLayoutInfo> result,
        List<MonitorLayoutInfo> storedLayout,
        string localName,
        Dictionary<string, (WinAPI.PhysicalMonitorInfo Phys, string PhysId)> configToPhys)
    {
        // Use the same tolerance as MonitorLayoutManager so we catch every
        // pair that was considered adjacent before patching.
        const int Tolerance = 64;

        // Build (stored, patched) pairs for each local monitor.
        var localPairs = new List<(MonitorLayoutInfo Stored, MonitorLayoutInfo Patched)>();
        foreach (var stored in storedLayout)
        {
            if (!string.Equals(stored.MachineName, localName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!configToPhys.TryGetValue(stored.MonitorId, out var pair))
            {
                continue;
            }

            foreach (var patched in result)
            {
                if (string.Equals(patched.MachineName, localName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(patched.MonitorId, pair.PhysId, StringComparison.OrdinalIgnoreCase))
                {
                    localPairs.Add((stored, patched));
                    break;
                }
            }
        }

        if (localPairs.Count == 0)
        {
            return;
        }

        for (int i = 0; i < result.Count; i++)
        {
            var remote = result[i];
            if (string.Equals(remote.MachineName, localName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int deltaX = 0;
            int deltaY = 0;
            bool adjustedX = false;
            bool adjustedY = false;

            foreach (var (stored, patched) in localPairs)
            {
                // Remote below local: remote.top ≈ stored.bottom
                if (!adjustedY
                    && Math.Abs(remote.Y - (stored.Y + stored.Height)) <= Tolerance
                    && HasXOverlap(remote, stored, Tolerance))
                {
                    deltaY = (patched.Y + patched.Height) - (stored.Y + stored.Height);
                    adjustedY = true;
                }

                // Remote above local: remote.bottom ≈ stored.top
                if (!adjustedY
                    && Math.Abs((remote.Y + remote.Height) - stored.Y) <= Tolerance
                    && HasXOverlap(remote, stored, Tolerance))
                {
                    deltaY = patched.Y - stored.Y;
                    adjustedY = true;
                }

                // Remote right of local: remote.left ≈ stored.right
                if (!adjustedX
                    && Math.Abs(remote.X - (stored.X + stored.Width)) <= Tolerance
                    && HasYOverlap(remote, stored, Tolerance))
                {
                    deltaX = (patched.X + patched.Width) - (stored.X + stored.Width);
                    adjustedX = true;
                }

                // Remote left of local: remote.right ≈ stored.left
                if (!adjustedX
                    && Math.Abs((remote.X + remote.Width) - stored.X) <= Tolerance
                    && HasYOverlap(remote, stored, Tolerance))
                {
                    deltaX = patched.X - stored.X;
                    adjustedX = true;
                }

                if (adjustedX && adjustedY)
                {
                    break;
                }
            }

            if (deltaX != 0 || deltaY != 0)
            {
                result[i] = new MonitorLayoutInfo
                {
                    MachineName = remote.MachineName,
                    MonitorId = remote.MonitorId,
                    X = remote.X + deltaX,
                    Y = remote.Y + deltaY,
                    Width = remote.Width,
                    Height = remote.Height,
                    IsPrimary = remote.IsPrimary,
                };
                Logger.LogDebug($"  Adjusted remote: {remote.MachineName}|{remote.MonitorId} ({remote.X},{remote.Y})->({remote.X + deltaX},{remote.Y + deltaY}) delta=({deltaX},{deltaY})");
            }
        }
    }

    private static bool HasXOverlap(MonitorLayoutInfo a, MonitorLayoutInfo b, int tolerance)
    {
        return a.X < (b.X + b.Width + tolerance) && (a.X + a.Width + tolerance) > b.X;
    }

    private static bool HasYOverlap(MonitorLayoutInfo a, MonitorLayoutInfo b, int tolerance)
    {
        return a.Y < (b.Y + b.Height + tolerance) && (a.Y + a.Height + tolerance) > b.Y;
    }

    /// <summary>
    /// Matches configured local monitors to physical monitors by resolution and
    /// left-to-right position within each resolution group.
    /// Returns <c>null</c> when any resolution group has a count mismatch between
    /// configured and physical monitors (cannot match reliably).
    /// </summary>
    internal static Dictionary<string, (WinAPI.PhysicalMonitorInfo Phys, string PhysId)> MatchMonitorsByPositionalOrder(
        List<MonitorLayoutInfo> localLayout,
        List<WinAPI.PhysicalMonitorInfo> physicalMonitors)
    {
        // Single-monitor shortcut: when the stored layout has exactly one local
        // monitor and the machine has exactly one physical monitor, match them
        // unconditionally regardless of resolution.  This handles the common
        // scenario where a remote machine pushed a layout that contains a
        // synthetic placeholder MonitorId (e.g. "DELLA-0") with dimensions
        // that may not match the actual screen, yet both sides agree there is
        // only one monitor.  Without this shortcut the resolution check below
        // would fail and the physical device name would never replace the
        // placeholder, making all adjacency look-ups miss and forcing a fall-
        // back to the legacy machine-matrix navigation.
        if (localLayout.Count == 1 && physicalMonitors.Count == 1)
        {
            var pm = physicalMonitors[0];
            Logger.LogDebug($"PatchLocalMonitorCoordinates: single-monitor shortcut — mapping {localLayout[0].MonitorId} -> {pm.DeviceName}.");
            return new Dictionary<string, (WinAPI.PhysicalMonitorInfo, string)>(StringComparer.OrdinalIgnoreCase)
            {
                [localLayout[0].MonitorId] = (pm, pm.DeviceName),
            };
        }

        // Group configured local monitors by resolution.
        var configBySize = new Dictionary<(int W, int H), List<MonitorLayoutInfo>>();
        foreach (var m in localLayout)
        {
            var key = (m.Width, m.Height);
            if (!configBySize.TryGetValue(key, out var list))
            {
                configBySize[key] = list = new List<MonitorLayoutInfo>();
            }

            list.Add(m);
        }

        // Group physical monitors by resolution.
        var physBySize = new Dictionary<(int W, int H), List<WinAPI.PhysicalMonitorInfo>>();
        foreach (var pm in physicalMonitors)
        {
            var key = (pm.Width, pm.Height);
            if (!physBySize.TryGetValue(key, out var list))
            {
                physBySize[key] = list = new List<WinAPI.PhysicalMonitorInfo>();
            }

            list.Add(pm);
        }

        var result = new Dictionary<string, (WinAPI.PhysicalMonitorInfo, string)>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in configBySize)
        {
            var sizeKey = kvp.Key;
            var configList = kvp.Value;

            if (!physBySize.TryGetValue(sizeKey, out var physList) || physList.Count != configList.Count)
            {
                Logger.LogDebug($"PatchLocalMonitorCoordinates: size {sizeKey.W}x{sizeKey.H} count mismatch (config={configList.Count}, phys={physList?.Count ?? 0}).");
                return null;
            }

            // Sort configured by X (left-to-right in canvas).
            configList.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

            // Sort physical by Left (left-to-right in Windows virtual screen).
            physList.Sort((a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Top.CompareTo(b.Top));

            for (int i = 0; i < configList.Count; i++)
            {
                result[configList[i].MonitorId] = (physList[i], physList[i].DeviceName);
            }
        }

        return result;
    }
}
