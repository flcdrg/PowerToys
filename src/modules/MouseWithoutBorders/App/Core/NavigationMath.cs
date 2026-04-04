// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;

// <summary>
//     Pure-function helpers for navigation coordinate conversion.
// </summary>
namespace MouseWithoutBorders.Core;

/// <summary>
/// Stateless helpers for the monitor-edge navigation coordinate system.
/// All members are pure functions or constants with no side effects.
/// </summary>
internal static class NavigationMath
{
    /// <summary>
    /// Number of pixels from a monitor edge at which the edge-trigger fires.
    /// </summary>
    internal const int SkipPixels = 1;

    /// <summary>
    /// Number of pixels inside a monitor edge at which the cursor lands after
    /// a cross-machine or cross-monitor transition.
    /// </summary>
    internal const int JumpPixels = 2;

    /// <summary>
    /// Converts a physical pixel point to a universal 0–65535 coordinate
    /// relative to <paramref name="r"/>.
    /// </summary>
    internal static Point ConvertToUniversalValue(Point p, MyRectangle r)
    {
        if (!p.IsEmpty)
        {
            p.X = (p.X - r.Left) * 65535 / (r.Right - r.Left);
            p.Y = (p.Y - r.Top) * 65535 / (r.Bottom - r.Top);
        }

        return p;
    }

    /// <summary>
    /// Clamps <paramref name="coord"/> to the safe interior of a monitor axis
    /// so the result never coincides with an edge-trigger pixel.
    /// <para>
    /// Edge detection fires when a coordinate is within <see cref="SkipPixels"/>
    /// of a monitor boundary.  If a same-machine transition were to place the
    /// cursor exactly at that threshold pixel the hook would immediately fire
    /// another edge event on the new monitor, causing a visible stutter or
    /// locking the cursor at the edge.  This helper keeps the result at least
    /// one pixel inside the trigger zone on each side.
    /// </para>
    /// Safe range: [<paramref name="minBound"/> + <see cref="SkipPixels"/>,
    ///              <paramref name="maxBound"/> - <see cref="SkipPixels"/> - 1]
    /// </summary>
    internal static int ClampToSafeMonitorCoord(int coord, int minBound, int maxBound)
        => Math.Clamp(coord, minBound + SkipPixels, maxBound - SkipPixels - 1);

    /// <summary>
    /// Returns the <see cref="WinAPI.PhysicalMonitorInfo"/> for the physical
    /// monitor whose <see cref="WinAPI.PhysicalMonitorInfo.DeviceName"/> matches
    /// <paramref name="deviceName"/>, or <c>null</c> when not found.
    /// </summary>
    internal static WinAPI.PhysicalMonitorInfo? FindPhysicalMonitor(string deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            return null;
        }

        var monitors = WinAPI.PhysicalMonitors;
        if (monitors == null)
        {
            return null;
        }

        foreach (var pm in monitors)
        {
            if (string.Equals(pm.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            {
                return pm;
            }
        }

        return null;
    }
}
