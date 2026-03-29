// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    /// <summary>
    /// Full layout configuration describing local displays and where remote machines are positioned.
    /// </summary>
    public class MouseWithoutBordersDisplayLayoutConfiguration
    {
        /// <summary>
        /// Gets or sets the rectangles for each local display, ordered by display index.
        /// </summary>
        public List<MouseWithoutBordersDisplayRect> Displays { get; set; } = new List<MouseWithoutBordersDisplayRect>();

        /// <summary>
        /// Gets or sets the positions of remote machines relative to local display edges.
        /// </summary>
        public List<MouseWithoutBordersDisplayLayoutDevicePosition> DevicePositions { get; set; } = new List<MouseWithoutBordersDisplayLayoutDevicePosition>();

        /// <summary>
        /// Converts the display layout to a flat machine matrix compatible with the existing MWB runtime.
        /// The matrix is a 4-element array where position encodes left-to-right ordering.
        /// </summary>
        /// <param name="localMachineName">The name of this (local) machine.</param>
        /// <returns>A 4-element string array for MachineMatrixString.</returns>
        public List<string> ToMachineMatrix(string localMachineName)
        {
            const int MaxMachine = 4;
            var result = new List<string>(new string[MaxMachine]);
            for (int i = 0; i < MaxMachine; i++)
            {
                result[i] = string.Empty;
            }

            if (DevicePositions == null || DevicePositions.Count == 0)
            {
                return result;
            }

            // Collect unique machine names (excluding local) in spatial order:
            // Left-edge machines first, then local machine, then right-edge machines.
            // Top/bottom are interleaved by display position.
            var leftMachines = new List<string>();
            var rightMachines = new List<string>();
            var otherMachines = new List<string>();

            foreach (var pos in DevicePositions)
            {
                if (string.IsNullOrEmpty(pos.MachineName))
                {
                    continue;
                }

                switch (pos.Edge)
                {
                    case MouseWithoutBordersDisplayEdge.Left:
                        if (!leftMachines.Contains(pos.MachineName, StringComparer.OrdinalIgnoreCase))
                        {
                            leftMachines.Add(pos.MachineName);
                        }

                        break;
                    case MouseWithoutBordersDisplayEdge.Right:
                        if (!rightMachines.Contains(pos.MachineName, StringComparer.OrdinalIgnoreCase))
                        {
                            rightMachines.Add(pos.MachineName);
                        }

                        break;
                    default:
                        if (!otherMachines.Contains(pos.MachineName, StringComparer.OrdinalIgnoreCase))
                        {
                            otherMachines.Add(pos.MachineName);
                        }

                        break;
                }
            }

            // Build ordered list: [left machines] [local] [right machines] [top/bottom]
            var ordered = new List<string>();
            ordered.AddRange(leftMachines);

            if (!string.IsNullOrEmpty(localMachineName))
            {
                ordered.Add(localMachineName);
            }

            ordered.AddRange(rightMachines);
            ordered.AddRange(otherMachines);

            // Fill into the 4-slot matrix
            for (int i = 0; i < Math.Min(ordered.Count, MaxMachine); i++)
            {
                result[i] = ordered[i];
            }

            return result;
        }

        /// <summary>
        /// Validates the configuration, removing invalid positions.
        /// </summary>
        public void Sanitize()
        {
            int displayCount = Displays?.Count ?? 0;

            DevicePositions?.RemoveAll(p =>
                string.IsNullOrWhiteSpace(p.MachineName) ||
                p.DisplayIndex < 0 ||
                p.DisplayIndex >= displayCount);

            // Remove duplicates (keep first occurrence)
            if (DevicePositions != null)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                DevicePositions.RemoveAll(p => !seen.Add(p.MachineName));
            }
        }

        /// <summary>
        /// Returns an outer edge that is not yet occupied, or null if all edges are taken.
        /// Prefers right edges, then left, then bottom, then top.
        /// </summary>
        public (MouseWithoutBordersDisplayEdge Edge, int DisplayIndex)? FindFirstAvailableEdge()
        {
            if (Displays == null || Displays.Count == 0)
            {
                return null;
            }

            var occupied = new HashSet<(MouseWithoutBordersDisplayEdge, int)>();
            if (DevicePositions != null)
            {
                foreach (var pos in DevicePositions)
                {
                    occupied.Add((pos.Edge, pos.DisplayIndex));
                }
            }

            // Try outer edges only (rightmost display right edge, leftmost display left edge, etc.)
            var edgePriority = new[]
            {
                (MouseWithoutBordersDisplayEdge.Right, Displays.Count - 1),
                (MouseWithoutBordersDisplayEdge.Left, 0),
                (MouseWithoutBordersDisplayEdge.Bottom, 0),
                (MouseWithoutBordersDisplayEdge.Top, 0),
            };

            foreach (var (edge, displayIndex) in edgePriority)
            {
                if (!occupied.Contains((edge, displayIndex)))
                {
                    return (edge, displayIndex);
                }
            }

            // Fall back to any unoccupied edge on any display
            foreach (var edge in new[] { MouseWithoutBordersDisplayEdge.Right, MouseWithoutBordersDisplayEdge.Left, MouseWithoutBordersDisplayEdge.Bottom, MouseWithoutBordersDisplayEdge.Top })
            {
                for (int i = 0; i < Displays.Count; i++)
                {
                    if (!occupied.Contains((edge, i)))
                    {
                        return (edge, i);
                    }
                }
            }

            return null;
        }
    }
}
