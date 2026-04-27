// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;

using Microsoft.PowerToys.Settings.UI.Library;
using MouseWithoutBorders.Core;

// Disable the warning to preserve original code
#pragma warning disable CA1716
namespace MouseWithoutBorders.Class
#pragma warning restore CA1716
{
    internal enum MoveDirection
    {
        Left,
        Right,
        Up,
        Down,
    }

    internal struct AdjacentMonitors
    {
        public MonitorLayoutInfo? Left;
        public MonitorLayoutInfo? Right;
        public MonitorLayoutInfo? Up;
        public MonitorLayoutInfo? Down;

        // Gap (in pixels) for each direction; closest neighbor wins when multiple candidates exist.
        internal int LeftGap = int.MaxValue;
        internal int RightGap = int.MaxValue;
        internal int UpGap = int.MaxValue;
        internal int DownGap = int.MaxValue;

        // Perpendicular overlap for each direction; used as a tiebreaker when gaps are equal
        // so that a wide shared edge beats a corner-pixel touch.
        internal int LeftOverlap = 0;
        internal int RightOverlap = 0;
        internal int UpOverlap = 0;
        internal int DownOverlap = 0;

        public AdjacentMonitors()
        {
        }
    }

    internal sealed class MonitorTransitionResult
    {
        public string TargetMachine { get; init; } = string.Empty;

        public string TargetMonitorId { get; init; } = string.Empty;

        public bool IsResolved { get; init; }

        /// <summary>Gets Canvas X of the target monitor (from the patched snapshot).</summary>
        public int TargetCanvasX { get; init; }

        /// <summary>Gets Canvas Y of the target monitor (from the patched snapshot).</summary>
        public int TargetCanvasY { get; init; }

        /// <summary>Gets Canvas width of the target monitor (from the patched snapshot).</summary>
        public int TargetCanvasWidth { get; init; }

        /// <summary>Gets Canvas height of the target monitor (from the patched snapshot).</summary>
        public int TargetCanvasHeight { get; init; }

        /// <summary>Gets Canvas X of the source monitor (from the patched snapshot).</summary>
        public int SrcCanvasX { get; init; }

        /// <summary>Gets Canvas Y of the source monitor (from the patched snapshot).</summary>
        public int SrcCanvasY { get; init; }

        /// <summary>Gets Canvas width of the source monitor (from the patched snapshot).</summary>
        public int SrcCanvasWidth { get; init; }

        /// <summary>Gets Canvas height of the source monitor (from the patched snapshot).</summary>
        public int SrcCanvasHeight { get; init; }

        /// <summary>Gets the canvas X of the cursor at the moment the transition was resolved.</summary>
        public int CanvasCursorX { get; init; }

        /// <summary>Gets the canvas Y of the cursor at the moment the transition was resolved.</summary>
        public int CanvasCursorY { get; init; }

        internal static readonly MonitorTransitionResult NotResolved = new MonitorTransitionResult { IsResolved = false };
    }

    internal sealed class MonitorLayoutManager
    {
        private const int AdjacencyTolerance = 128;

        /// <summary>
        /// Small overlaps (in pixels) caused by canvas rounding should be
        /// treated as "touching" rather than "fully overlapping". Only skip
        /// a pair when BOTH dimensions overlap by more than this threshold.
        /// </summary>
        private const int OverlapThreshold = 64;

        private readonly object _writeLock = new object();

        private volatile AdjacencySnapshot? _snapshot;

        private sealed class AdjacencySnapshot
        {
            internal readonly List<MonitorLayoutInfo> Monitors;
            internal readonly Dictionary<string, AdjacentMonitors> Adjacency;

            internal AdjacencySnapshot(List<MonitorLayoutInfo> monitors, Dictionary<string, AdjacentMonitors> adjacency)
            {
                Monitors = monitors;
                Adjacency = adjacency;
            }
        }

        internal void RebuildAdjacency(List<MonitorLayoutInfo>? layout)
        {
            if (layout == null || layout.Count == 0)
            {
                lock (_writeLock)
                {
                    _snapshot = null;
                }

                return;
            }

            var monitors = new List<MonitorLayoutInfo>(layout);
            var adjacency = new Dictionary<string, AdjacentMonitors>(monitors.Count, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < monitors.Count; i++)
            {
                adjacency[MonitorKey(monitors[i])] = new AdjacentMonitors();
            }

            for (int i = 0; i < monitors.Count; i++)
            {
                for (int j = i + 1; j < monitors.Count; j++)
                {
                    ComputePairAdjacency(adjacency, monitors[i], monitors[j]);
                }
            }

            var newSnapshot = new AdjacencySnapshot(monitors, adjacency);

            // Log computed adjacency for diagnostics.
            foreach (var kvp in adjacency)
            {
                var adj = kvp.Value;
                MouseWithoutBorders.Core.Logger.LogDebug(
                    $"Adjacency [{kvp.Key}]: L={FormatTarget(adj.Left)} R={FormatTarget(adj.Right)} U={FormatTarget(adj.Up)} D={FormatTarget(adj.Down)}");
            }

            lock (_writeLock)
            {
                _snapshot = newSnapshot;
            }
        }

        internal MonitorTransitionResult ResolveEdgeTransition(
            string currentMachine,
            int cursorX,
            int cursorY,
            MoveDirection direction)
        {
            var snapshot = _snapshot;
            if (snapshot == null)
            {
                MouseWithoutBorders.Core.Logger.LogDebug($"ResolveEdgeTransition({currentMachine}, ({cursorX},{cursorY}), {direction}): no snapshot");
                return MonitorTransitionResult.NotResolved;
            }

            MouseWithoutBorders.Core.Logger.LogDebug($"ResolveEdgeTransition({currentMachine}, canvas=({cursorX},{cursorY}), {direction})");

            MonitorLayoutInfo? current = null;
            var monitors = snapshot.Monitors;
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                if (string.Equals(m.MachineName, currentMachine, StringComparison.OrdinalIgnoreCase)
                    && cursorX >= m.X && cursorX < m.X + m.Width
                    && cursorY >= m.Y && cursorY < m.Y + m.Height)
                {
                    current = m;
                    break;
                }
            }

            if (current == null)
            {
                // Log which monitors exist for this machine so we can see if the canvas cursor missed them all.
                var machineMonitors = new System.Text.StringBuilder();
                foreach (var m in monitors)
                {
                    if (string.Equals(m.MachineName, currentMachine, StringComparison.OrdinalIgnoreCase))
                    {
                        machineMonitors.Append(System.Globalization.CultureInfo.InvariantCulture, $" [{m.MonitorId} ({m.X},{m.Y})-({m.X + m.Width},{m.Y + m.Height})]");
                    }
                }

                MouseWithoutBorders.Core.Logger.LogDebug($"  -> no {currentMachine} monitor contains canvas ({cursorX},{cursorY}); monitors:{(machineMonitors.Length > 0 ? machineMonitors.ToString() : " (none)")}");
                return MonitorTransitionResult.NotResolved;
            }

            string currentKey = MonitorKey(current);
            if (!snapshot.Adjacency.TryGetValue(currentKey, out var adjacent))
            {
                MouseWithoutBorders.Core.Logger.LogDebug($"  -> key '{currentKey}' not in adjacency dict ({snapshot.Adjacency.Count} entries)");
                return MonitorTransitionResult.NotResolved;
            }

            MonitorLayoutInfo? target = direction switch
            {
                MoveDirection.Left => adjacent.Left,
                MoveDirection.Right => adjacent.Right,
                MoveDirection.Up => adjacent.Up,
                MoveDirection.Down => adjacent.Down,
                _ => null,
            };

            if (target == null)
            {
                MouseWithoutBorders.Core.Logger.LogDebug($"  -> adjacency[{currentKey}] has no {direction} neighbor");
                return MonitorTransitionResult.NotResolved;
            }

            MouseWithoutBorders.Core.Logger.LogDebug($"  -> resolved {direction}: {currentKey} -> {target.MachineName}|{target.MonitorId} canvas=({target.X},{target.Y},{target.Width}x{target.Height})");
            return new MonitorTransitionResult
            {
                TargetMachine = target.MachineName,
                TargetMonitorId = target.MonitorId,
                IsResolved = true,
                TargetCanvasX = target.X,
                TargetCanvasY = target.Y,
                TargetCanvasWidth = target.Width,
                TargetCanvasHeight = target.Height,
                SrcCanvasX = current.X,
                SrcCanvasY = current.Y,
                SrcCanvasWidth = current.Width,
                SrcCanvasHeight = current.Height,
                CanvasCursorX = cursorX,
                CanvasCursorY = cursorY,
            };
        }

        internal MonitorTransitionResult ResolveEdgeTransitionByMonitorId(
            string currentMachine,
            string monitorId,
            MoveDirection direction,
            int cursorX = int.MinValue,
            int cursorY = int.MinValue,
            int physMonitorLeft = 0,
            int physMonitorTop = 0)
        {
            var snapshot = _snapshot;
            if (snapshot == null)
            {
                MouseWithoutBorders.Core.Logger.LogDebug($"ResolveEdgeTransitionByMonitorId({currentMachine}|{monitorId}, {direction}): no snapshot");
                return MonitorTransitionResult.NotResolved;
            }

            string key = string.Concat(currentMachine, "|", monitorId);
            MouseWithoutBorders.Core.Logger.LogDebug($"ResolveEdgeTransitionByMonitorId: key='{key}' dir={direction} phys=({cursorX},{cursorY}) physOrigin=({physMonitorLeft},{physMonitorTop})");

            if (!snapshot.Adjacency.TryGetValue(key, out var adjacent))
            {
                // Log a sample of adjacency keys so we can spot case or separator mismatches.
                var sample = new System.Text.StringBuilder();
                int count = 0;
                foreach (var k in snapshot.Adjacency.Keys)
                {
                    sample.Append(System.Globalization.CultureInfo.InvariantCulture, $" '{k}'");
                    if (++count >= 6)
                    {
                        sample.Append(" ...");
                        break;
                    }
                }

                MouseWithoutBorders.Core.Logger.LogDebug($"  -> key not in adjacency ({snapshot.Adjacency.Count} entries):{sample}");
                return MonitorTransitionResult.NotResolved;
            }

            MonitorLayoutInfo? target = direction switch
            {
                MoveDirection.Left => adjacent.Left,
                MoveDirection.Right => adjacent.Right,
                MoveDirection.Up => adjacent.Up,
                MoveDirection.Down => adjacent.Down,
                _ => null,
            };

            if (target == null)
            {
                MouseWithoutBorders.Core.Logger.LogDebug($"  -> adjacency[{key}] has no {direction} neighbor (L={FormatTarget(adjacent.Left)} R={FormatTarget(adjacent.Right)} U={FormatTarget(adjacent.Up)} D={FormatTarget(adjacent.Down)})");
                return MonitorTransitionResult.NotResolved;
            }

            // Find the source monitor to convert the cursor from physical space to
            // canvas space.  The canvas and physical coordinate systems have the same
            // scale (1 canvas pixel == 1 physical pixel), so:
            //   canvas_cursor = src.CanvasOrigin + (cursor - physMonitorOrigin)
            // After that, reject the transition when the cursor is outside the shared
            // perpendicular strip — e.g. a Down transition from a wide monitor to a
            // narrow one should only fire when the cursor is above the narrow overlap.
            MonitorLayoutInfo? src = null;
            var monitors = snapshot.Monitors;
            for (int i = 0; i < monitors.Count; i++)
            {
                if (string.Equals(MonitorKey(monitors[i]), key, StringComparison.OrdinalIgnoreCase))
                {
                    src = monitors[i];
                    break;
                }
            }

            // Default to the target center; overwritten below when physical cursor coords are available.
            int canvasCursorX = target.X + (target.Width / 2);
            int canvasCursorY = target.Y + (target.Height / 2);

            if (src != null && cursorX != int.MinValue && cursorY != int.MinValue)
            {
                canvasCursorX = src.X + (cursorX - physMonitorLeft);
                canvasCursorY = src.Y + (cursorY - physMonitorTop);

                // Only apply the strip check for same-machine transitions. For
                // cross-machine targets the stored dimensions may reflect settings values
                // rather than the physical pixel size (DPI scaling on the remote machine
                // can make them diverge), so the check would reject valid transitions.
                bool sameMachine = string.Equals(src.MachineName, target.MachineName, StringComparison.OrdinalIgnoreCase);

                if (sameMachine && (direction == MoveDirection.Down || direction == MoveDirection.Up))
                {
                    // Vertical movement: cursor X must lie inside the shared horizontal strip.
                    int sharedXMin = Math.Max(src.X, target.X);
                    int sharedXMax = Math.Min(src.X + src.Width, target.X + target.Width);
                    if (sharedXMax > sharedXMin && (canvasCursorX < sharedXMin || canvasCursorX >= sharedXMax))
                    {
                        MouseWithoutBorders.Core.Logger.LogDebug($"  -> strip FAIL ({direction}): canvasX={canvasCursorX} not in [{sharedXMin},{sharedXMax}) src=({src.X},{src.X + src.Width}) target=({target.X},{target.X + target.Width})");
                        return MonitorTransitionResult.NotResolved;
                    }
                }
                else if (sameMachine)
                {
                    // Horizontal movement: cursor Y must lie inside the shared vertical strip.
                    int sharedYMin = Math.Max(src.Y, target.Y);
                    int sharedYMax = Math.Min(src.Y + src.Height, target.Y + target.Height);
                    if (sharedYMax > sharedYMin && (canvasCursorY < sharedYMin || canvasCursorY >= sharedYMax))
                    {
                        MouseWithoutBorders.Core.Logger.LogDebug($"  -> strip FAIL ({direction}): canvasY={canvasCursorY} not in [{sharedYMin},{sharedYMax}) src=({src.Y},{src.Y + src.Height}) target=({target.Y},{target.Y + target.Height})");
                        return MonitorTransitionResult.NotResolved;
                    }
                }

                MouseWithoutBorders.Core.Logger.LogDebug($"  -> canvas cursor=({canvasCursorX},{canvasCursorY}) src=({src.X},{src.Y},{src.Width}x{src.Height}) strip OK");
            }

            MouseWithoutBorders.Core.Logger.LogDebug($"  -> resolved {direction}: {key} -> {target.MachineName}|{target.MonitorId} canvas=({target.X},{target.Y},{target.Width}x{target.Height}) canvasCursor=({canvasCursorX},{canvasCursorY})");
            return new MonitorTransitionResult
            {
                TargetMachine = target.MachineName,
                TargetMonitorId = target.MonitorId,
                IsResolved = true,
                TargetCanvasX = target.X,
                TargetCanvasY = target.Y,
                TargetCanvasWidth = target.Width,
                TargetCanvasHeight = target.Height,
                SrcCanvasX = src?.X ?? 0,
                SrcCanvasY = src?.Y ?? 0,
                SrcCanvasWidth = src?.Width ?? 0,
                SrcCanvasHeight = src?.Height ?? 0,
                CanvasCursorX = canvasCursorX,
                CanvasCursorY = canvasCursorY,
            };
        }

        /// <summary>
        /// Returns the bounding box of all monitors belonging to <paramref name="machineName"/>
        /// from the current patched snapshot, or <c>null</c> when the snapshot is empty or the
        /// machine has no entries.
        /// </summary>
        internal MyRectangle? GetMachineBounds(string machineName)
        {
            var snapshot = _snapshot;
            if (snapshot == null)
            {
                return null;
            }

            int rcLeft = int.MaxValue, rcTop = int.MaxValue, rcRight = int.MinValue, rcBottom = int.MinValue;
            bool found = false;
            var monitors = snapshot.Monitors;
            for (int i = 0; i < monitors.Count; i++)
            {
                var m = monitors[i];
                if (!string.Equals(m.MachineName, machineName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rcLeft = Math.Min(rcLeft, m.X);
                rcTop = Math.Min(rcTop, m.Y);
                rcRight = Math.Max(rcRight, m.X + m.Width);
                rcBottom = Math.Max(rcBottom, m.Y + m.Height);
                found = true;
            }

            return found ? new MyRectangle { Left = rcLeft, Top = rcTop, Right = rcRight, Bottom = rcBottom } : null;
        }

        private static string MonitorKey(MonitorLayoutInfo m)
            => string.Concat(m.MachineName, "|", m.MonitorId);

        private static string FormatTarget(MonitorLayoutInfo? m)
            => m == null ? "(none)" : $"{m.MachineName}|{m.MonitorId}";

        private static void ComputePairAdjacency(
            Dictionary<string, AdjacentMonitors> adjacency,
            MonitorLayoutInfo a,
            MonitorLayoutInfo b)
        {
            // Compute the actual pixel overlap in each dimension.
            int xOverlap = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
            int yOverlap = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));

            // Skip only when BOTH dimensions overlap significantly. Small
            // overlaps (canvas rounding, DPI scaling) should still allow
            // adjacency detection.
            if (xOverlap > OverlapThreshold && yOverlap > OverlapThreshold)
            {
                return;
            }

            if (string.Equals(a.MachineName, b.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                // Same machine: monitors are always part of a group. Use the settings
                // data (MachineName) as the authority — no proximity tolerance needed.
                ComputeSameMachinePairAdjacency(adjacency, a, b);
                return;
            }

            // Cross-machine: only link when monitors share an edge strip within tolerance.
            bool sharedVertical = a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
            bool sharedHorizontal = a.X < b.X + b.Width && b.X < a.X + a.Width;

            // Perpendicular overlap used as a tiebreaker when multiple candidates share the same gap.
            int hOverlap = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
            int vOverlap = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));

            int gap;

            gap = Math.Abs((a.X + a.Width) - b.X);
            if (sharedVertical && gap <= AdjacencyTolerance)
            {
                SetAdjacent(adjacency, a, b, MoveDirection.Right, gap, vOverlap);
                SetAdjacent(adjacency, b, a, MoveDirection.Left, gap, vOverlap);
                return;
            }

            gap = Math.Abs((b.X + b.Width) - a.X);
            if (sharedVertical && gap <= AdjacencyTolerance)
            {
                SetAdjacent(adjacency, b, a, MoveDirection.Right, gap, vOverlap);
                SetAdjacent(adjacency, a, b, MoveDirection.Left, gap, vOverlap);
                return;
            }

            gap = Math.Abs((a.Y + a.Height) - b.Y);
            if (sharedHorizontal && gap <= AdjacencyTolerance)
            {
                SetAdjacent(adjacency, a, b, MoveDirection.Down, gap, hOverlap);
                SetAdjacent(adjacency, b, a, MoveDirection.Up, gap, hOverlap);
                return;
            }

            gap = Math.Abs((b.Y + b.Height) - a.Y);
            if (sharedHorizontal && gap <= AdjacencyTolerance)
            {
                SetAdjacent(adjacency, b, a, MoveDirection.Down, gap, hOverlap);
                SetAdjacent(adjacency, a, b, MoveDirection.Up, gap, hOverlap);
            }
        }

        /// <summary>
        /// Links two monitors on the same machine by finding the direction with the
        /// smallest non-negative edge-to-edge gap. For diagonal arrangements (no shared
        /// horizontal or vertical strip), the closest edge pair determines the direction.
        /// The closest-neighbor-wins logic in <see cref="SetAdjacent"/> ensures the
        /// nearest monitor wins when multiple candidates exist in the same direction.
        /// </summary>
        private static void ComputeSameMachinePairAdjacency(
            Dictionary<string, AdjacentMonitors> adjacency,
            MonitorLayoutInfo a,
            MonitorLayoutInfo b)
        {
            // Signed gap in each possible direction (positive = gap exists, negative = overlap).
            int aRightGap = b.X - (a.X + a.Width);    // b is to the right of a
            int bRightGap = a.X - (b.X + b.Width);    // a is to the right of b
            int aDownGap = b.Y - (a.Y + a.Height);    // b is below a
            int bDownGap = a.Y - (b.Y + b.Height);    // a is below b (b is above a)

            // Require perpendicular overlap for each direction, matching the cross-machine
            // logic. Without this, monitors that merely touch at a corner (zero overlap in
            // the perpendicular axis) would be linked, which produces wrong transitions —
            // e.g. a wide top monitor getting linked Down to a narrow bottom monitor that
            // shares only a pixel at the corner, beating out a cross-machine neighbor that
            // has substantial horizontal overlap.
            int xOverlap = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
            int yOverlap = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));

            // Pick the direction with the smallest non-negative gap where the perpendicular
            // strip is non-empty (at least 1 pixel of shared edge).
            int minGap = int.MaxValue;
            int bestDir = -1;

            if (aRightGap >= 0 && aRightGap < minGap && yOverlap > 0)
            {
                minGap = aRightGap;
                bestDir = 0;
            }

            if (bRightGap >= 0 && bRightGap < minGap && yOverlap > 0)
            {
                minGap = bRightGap;
                bestDir = 1;
            }

            if (aDownGap >= 0 && aDownGap < minGap && xOverlap > 0)
            {
                minGap = aDownGap;
                bestDir = 2;
            }

            if (bDownGap >= 0 && bDownGap < minGap && xOverlap > 0)
            {
                minGap = bDownGap;
                bestDir = 3;
            }

            switch (bestDir)
            {
                case 0: // b is to the right of a
                    SetAdjacent(adjacency, a, b, MoveDirection.Right, minGap, yOverlap);
                    SetAdjacent(adjacency, b, a, MoveDirection.Left, minGap, yOverlap);
                    break;
                case 1: // a is to the right of b
                    SetAdjacent(adjacency, b, a, MoveDirection.Right, minGap, yOverlap);
                    SetAdjacent(adjacency, a, b, MoveDirection.Left, minGap, yOverlap);
                    break;
                case 2: // b is below a
                    SetAdjacent(adjacency, a, b, MoveDirection.Down, minGap, xOverlap);
                    SetAdjacent(adjacency, b, a, MoveDirection.Up, minGap, xOverlap);
                    break;
                case 3: // a is below b
                    SetAdjacent(adjacency, b, a, MoveDirection.Down, minGap, xOverlap);
                    SetAdjacent(adjacency, a, b, MoveDirection.Up, minGap, xOverlap);
                    break;
                default:
                    // All gaps are negative: monitors overlap in all directions; skip.
                    break;
            }
        }

        private static void SetAdjacent(
            Dictionary<string, AdjacentMonitors> adjacency,
            MonitorLayoutInfo from,
            MonitorLayoutInfo to,
            MoveDirection direction,
            int gap,
            int overlap = 0)
        {
            string key = MonitorKey(from);
            var entry = adjacency[key];
            switch (direction)
            {
                case MoveDirection.Left:
                    if (gap < entry.LeftGap || (gap == entry.LeftGap && overlap > entry.LeftOverlap))
                    {
                        entry.Left = to;
                        entry.LeftGap = gap;
                        entry.LeftOverlap = overlap;
                    }

                    break;
                case MoveDirection.Right:
                    if (gap < entry.RightGap || (gap == entry.RightGap && overlap > entry.RightOverlap))
                    {
                        entry.Right = to;
                        entry.RightGap = gap;
                        entry.RightOverlap = overlap;
                    }

                    break;
                case MoveDirection.Up:
                    if (gap < entry.UpGap || (gap == entry.UpGap && overlap > entry.UpOverlap))
                    {
                        entry.Up = to;
                        entry.UpGap = gap;
                        entry.UpOverlap = overlap;
                    }

                    break;
                case MoveDirection.Down:
                    if (gap < entry.DownGap || (gap == entry.DownGap && overlap > entry.DownOverlap))
                    {
                        entry.Down = to;
                        entry.DownGap = gap;
                        entry.DownOverlap = overlap;
                    }

                    break;
            }

            adjacency[key] = entry;
        }
    }
}
