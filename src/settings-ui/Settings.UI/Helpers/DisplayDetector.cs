// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.PowerToys.Settings.UI.Library;

namespace Microsoft.PowerToys.Settings.UI.Helpers
{
    /// <summary>
    /// Detects and enumerates the local machine's display configuration.
    /// </summary>
    public static class DisplayDetector
    {
        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

#pragma warning disable SA1307 // Win32 API
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
#pragma warning restore SA1307

#pragma warning disable SA1310 // Field names should not contain underscore
        private const uint MONITORINFOF_PRIMARY = 0x00000001;
#pragma warning restore SA1310 // Field names should not contain underscore

        /// <summary>
        /// Enumerates all displays on the current machine and returns their bounding rectangles.
        /// The primary display is placed first in the list.
        /// </summary>
        /// <returns>List of display rectangles, primary display first.</returns>
        public static List<MouseWithoutBordersDisplayRect> GetDisplays()
        {
            var primaryDisplays = new List<MouseWithoutBordersDisplayRect>();
            var otherDisplays = new List<MouseWithoutBordersDisplayRect>();
            List<string> errors = null;

            MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                try
                {
                    MONITORINFOEX mi = default;
                    mi.cbSize = Marshal.SizeOf<MONITORINFOEX>();

                    if (GetMonitorInfo(hMonitor, ref mi))
                    {
                        var rect = new MouseWithoutBordersDisplayRect(
                            mi.rcMonitor.Left,
                            mi.rcMonitor.Top,
                            mi.rcMonitor.Right,
                            mi.rcMonitor.Bottom);

                        if ((mi.dwFlags & MONITORINFOF_PRIMARY) != 0)
                        {
                            primaryDisplays.Add(rect);
                        }
                        else
                        {
                            otherDisplays.Add(rect);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors ??= new List<string>();
                    errors.Add(ex.Message);
                }

                return true; // continue enumeration
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

            // Sort secondary displays left-to-right, then top-to-bottom
            otherDisplays.Sort((a, b) =>
            {
                int cmp = a.Left.CompareTo(b.Left);
                return cmp != 0 ? cmp : a.Top.CompareTo(b.Top);
            });

            var result = new List<MouseWithoutBordersDisplayRect>(primaryDisplays.Count + otherDisplays.Count);
            result.AddRange(primaryDisplays);
            result.AddRange(otherDisplays);
            return result;
        }

        /// <summary>
        /// Returns true if the current machine has more than one display.
        /// </summary>
        public static bool HasMultipleDisplays()
        {
            int count = 0;
            MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
            {
                count++;
                return count < 2; // stop early once we know there are at least 2
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            return count > 1;
        }

        /// <summary>
        /// Builds a scaled set of display rectangles that fit within a given canvas size,
        /// maintaining relative proportions and positions.
        /// </summary>
        /// <param name="displays">The physical display rectangles.</param>
        /// <param name="canvasWidth">Target canvas width in pixels.</param>
        /// <param name="canvasHeight">Target canvas height in pixels.</param>
        /// <param name="padding">Padding to leave around the whole arrangement.</param>
        /// <returns>Scaled rectangles and the scale factor used.</returns>
        public static (List<MouseWithoutBordersDisplayRect> ScaledDisplays, double Scale) ScaleToCanvas(
            IList<MouseWithoutBordersDisplayRect> displays,
            double canvasWidth,
            double canvasHeight,
            double padding = 8.0)
        {
            if (displays == null || displays.Count == 0)
            {
                return (new List<MouseWithoutBordersDisplayRect>(), 1.0);
            }

            // Compute bounding box of all displays in physical coordinates
            int physLeft = int.MaxValue, physTop = int.MaxValue;
            int physRight = int.MinValue, physBottom = int.MinValue;

            foreach (var d in displays)
            {
                if (d.Left < physLeft)
                {
                    physLeft = d.Left;
                }

                if (d.Top < physTop)
                {
                    physTop = d.Top;
                }

                if (d.Right > physRight)
                {
                    physRight = d.Right;
                }

                if (d.Bottom > physBottom)
                {
                    physBottom = d.Bottom;
                }
            }

            double physWidth = physRight - physLeft;
            double physHeight = physBottom - physTop;

            if (physWidth <= 0 || physHeight <= 0)
            {
                return (new List<MouseWithoutBordersDisplayRect>(), 1.0);
            }

            double availableWidth = canvasWidth - (padding * 2);
            double availableHeight = canvasHeight - (padding * 2);

            double scaleX = availableWidth / physWidth;
            double scaleY = availableHeight / physHeight;
            double scale = Math.Min(scaleX, scaleY);

            // Compute offset to center the arrangement in the canvas
            double scaledTotalWidth = physWidth * scale;
            double scaledTotalHeight = physHeight * scale;
            double offsetX = padding + ((availableWidth - scaledTotalWidth) / 2.0);
            double offsetY = padding + ((availableHeight - scaledTotalHeight) / 2.0);

            var scaled = new List<MouseWithoutBordersDisplayRect>(displays.Count);
            foreach (var d in displays)
            {
                int left = (int)Math.Round(offsetX + ((d.Left - physLeft) * scale));
                int top = (int)Math.Round(offsetY + ((d.Top - physTop) * scale));
                int right = (int)Math.Round(offsetX + ((d.Right - physLeft) * scale));
                int bottom = (int)Math.Round(offsetY + ((d.Bottom - physTop) * scale));
                scaled.Add(new MouseWithoutBordersDisplayRect(left, top, right, bottom));
            }

            return (scaled, scale);
        }
    }
}
