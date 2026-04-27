// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.PowerToys.Settings.UI.Library;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.Core
{
    /// <summary>
    /// <see cref="ILayoutNavigator"/> implementation for the per-monitor layout mode.
    /// Uses a <see cref="MonitorLayoutManager"/> adjacency graph to resolve transitions
    /// between physical monitors and across machines.
    /// </summary>
    internal sealed class MonitorLayoutNavigator : ILayoutNavigator
    {
        internal readonly MonitorLayoutManager Manager = new MonitorLayoutManager();

        public void OnLayoutChanged(List<MonitorLayoutInfo>? layout)
        {
            Manager.RebuildAdjacency(layout);
        }

        public EdgeTransition? CheckLocalMachineEdge(int x, int y, string currentMachine)
        {
            var physMonitors = WinAPI.PhysicalMonitors;
            bool foundScreen = false;

            foreach (var pm in physMonitors)
            {
                if (x < pm.Left || x >= pm.Right || y < pm.Top || y >= pm.Bottom)
                {
                    continue;
                }

                foundScreen = true;
                string monitorId = pm.DeviceName;

                MoveDirection? dir = null;
                if (x < pm.Left + NavigationMath.SkipPixels)
                {
                    dir = MoveDirection.Left;
                }
                else if (x >= pm.Right - NavigationMath.SkipPixels)
                {
                    dir = MoveDirection.Right;
                }
                else if (y < pm.Top + NavigationMath.SkipPixels)
                {
                    dir = MoveDirection.Up;
                }
                else if (y >= pm.Bottom - NavigationMath.SkipPixels)
                {
                    dir = MoveDirection.Down;
                }

                if (dir.HasValue)
                {
                    Logger.LogDebug($"MonitorLayout edge: {dir.Value} on screen {monitorId} cursor=({x},{y})");
                    return TryResolveLocalEdge(dir.Value, currentMachine, x, y, monitorId, pm.Left, pm.Top);
                }

                break; // Cursor is inside this monitor but not at any edge.
            }

            if (!foundScreen)
            {
                return HandleGapCursor(x, y, currentMachine, physMonitors);
            }

            return null;
        }

        public EdgeTransition? TryResolveLocalEdge(
            MoveDirection direction,
            string currentMachine,
            int x,
            int y,
            string? localMonitorId,
            int physMonitorLeft,
            int physMonitorTop)
        {
            MonitorTransitionResult layoutResult;
            if (localMonitorId != null)
            {
                layoutResult = Manager.ResolveEdgeTransitionByMonitorId(
                    currentMachine, localMonitorId, direction, x, y, physMonitorLeft, physMonitorTop);
            }
            else
            {
                int normX = MachineStuff.desMachineID == Common.MachineID ? x - MachineStuff.desktopBounds.Left : x;
                int normY = MachineStuff.desMachineID == Common.MachineID ? y - MachineStuff.desktopBounds.Top : y;
                layoutResult = Manager.ResolveEdgeTransition(currentMachine, normX, normY, direction);
            }

            if (!layoutResult.IsResolved)
            {
                Logger.LogDebug($"Move {direction} (monitor layout): not resolved for {currentMachine} phys=({x},{y})");
                return null;
            }

            ID resolvedId = MachineStuff.IdFromName(layoutResult.TargetMachine);
            if (resolvedId == ID.NONE)
            {
                Logger.LogDebug($"Move {direction} (monitor layout): resolved target='{layoutResult.TargetMachine}' but IdFromName returned NONE — machine not connected?");
                return null;
            }

            Logger.LogDebug($"Move {direction} (monitor layout): {currentMachine} -> {layoutResult.TargetMachine}:{layoutResult.TargetMonitorId}");

            Point landing;
            if (resolvedId == Common.MachineID)
            {
                landing = ComputeSameMachineLanding(direction, x, y, layoutResult);
            }
            else
            {
                var srcPhys = localMonitorId != null ? NavigationMath.FindPhysicalMonitor(localMonitorId) : null;
                landing = ComputeRemoteMachineLanding(direction, layoutResult, x, y, srcPhys);
            }

            return new EdgeTransition(resolvedId, landing);
        }

        public EdgeTransition? TryResolveRemoteEdge(
            MoveDirection direction,
            string remoteMachine,
            int primaryX,
            int primaryY)
        {
            Logger.LogDebug($"ResolveRemoteMachineEdge: {remoteMachine} {direction} primary=({primaryX},{primaryY}) " +
                $"primaryBounds=({MachineStuff.primaryScreenBounds.Left},{MachineStuff.primaryScreenBounds.Top}," +
                $"{MachineStuff.primaryScreenBounds.Right},{MachineStuff.primaryScreenBounds.Bottom})");

            var remoteBounds = Manager.GetMachineBounds(remoteMachine);
            if (remoteBounds == null)
            {
                Logger.LogDebug($"ResolveRemoteMachineEdge: no patched snapshot entry for '{remoteMachine}', falling back.");
                return null;
            }

            var psb = MachineStuff.primaryScreenBounds;
            int pw = Math.Max(1, psb.Right - psb.Left);
            int ph = Math.Max(1, psb.Bottom - psb.Top);
            int cx = remoteBounds.Left + (((primaryX - psb.Left) * (remoteBounds.Right - remoteBounds.Left)) / pw);
            int cy = remoteBounds.Top + (((primaryY - psb.Top) * (remoteBounds.Bottom - remoteBounds.Top)) / ph);
            cx = Math.Clamp(cx, remoteBounds.Left, remoteBounds.Right - 1);
            cy = Math.Clamp(cy, remoteBounds.Top, remoteBounds.Bottom - 1);
            Logger.LogDebug($"  remoteBounds=({remoteBounds.Left},{remoteBounds.Top})-({remoteBounds.Right},{remoteBounds.Bottom}) canvas cursor=({cx},{cy})");

            var result = Manager.ResolveEdgeTransition(remoteMachine, cx, cy, direction);
            if (!result.IsResolved)
            {
                Logger.LogDebug($"ResolveRemoteMachineEdge: NotResolved for '{remoteMachine}' canvas ({cx},{cy}) dir={direction}.");
                return null;
            }

            ID resolvedId = MachineStuff.IdFromName(result.TargetMachine);
            if (resolvedId == ID.NONE)
            {
                Logger.LogDebug($"ResolveRemoteMachineEdge: IdFromName('{result.TargetMachine}') returned NONE — machine not connected?");
                return null;
            }

            Logger.LogDebug($"Move {direction} (remote → monitor layout): {remoteMachine} → {result.TargetMachine}:{result.TargetMonitorId}");

            Point landing;
            if (resolvedId == Common.MachineID)
            {
                landing = ComputeLocalMachineLandingFromRemote(direction, result, cx, cy, primaryX, primaryY);
            }
            else
            {
                // Remote to remote: keep current cursor position in universal coords.
                landing = NavigationMath.ConvertToUniversalValue(new Point(primaryX, primaryY), MachineStuff.desktopBounds);
                Logger.LogDebug($"  remote→remote: primary=({primaryX},{primaryY}) universal=({landing.X},{landing.Y})");
            }

            return new EdgeTransition(resolvedId, landing);
        }

        private EdgeTransition? HandleGapCursor(int x, int y, string currentMachine, List<WinAPI.PhysicalMonitorInfo> physMonitors)
        {
            WinAPI.PhysicalMonitorInfo bestPm = default;
            int bestDist = int.MaxValue;
            bool bestFound = false;

            foreach (var pm in physMonitors)
            {
                int cx = Math.Clamp(x, pm.Left, pm.Right - 1);
                int cy = Math.Clamp(y, pm.Top, pm.Bottom - 1);
                int dist = ((x - cx) * (x - cx)) + ((y - cy) * (y - cy));
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPm = pm;
                    bestFound = true;
                }
            }

            if (!bestFound)
            {
                return null;
            }

            string? monitorId = bestPm.DeviceName;
            MoveDirection? gapDir = null;
            if (x < bestPm.Left)
            {
                gapDir = MoveDirection.Left;
            }
            else if (x >= bestPm.Right)
            {
                gapDir = MoveDirection.Right;
            }
            else if (y < bestPm.Top)
            {
                gapDir = MoveDirection.Up;
            }
            else if (y >= bestPm.Bottom)
            {
                gapDir = MoveDirection.Down;
            }

            if (!gapDir.HasValue)
            {
                return null;
            }

            Logger.LogDebug($"MonitorLayout gap→{gapDir.Value} on {monitorId} cursor=({x},{y})");
            var resolved = TryResolveLocalEdge(gapDir.Value, currentMachine, x, y, monitorId, bestPm.Left, bestPm.Top);
            return resolved ?? SnapBackToMonitor(bestPm, x, y);
        }

        private static EdgeTransition SnapBackToMonitor(WinAPI.PhysicalMonitorInfo pm, int x, int y)
        {
            int snapX = Math.Clamp(x, pm.Left + NavigationMath.SkipPixels, pm.Right - NavigationMath.SkipPixels - 1);
            int snapY = Math.Clamp(y, pm.Top + NavigationMath.SkipPixels, pm.Bottom - NavigationMath.SkipPixels - 1);
            Logger.LogDebug($"MonitorLayout gap snap-back: ({x},{y}) → ({snapX},{snapY})");
            return new EdgeTransition(
                Common.MachineID,
                NavigationMath.ConvertToUniversalValue(new Point(snapX, snapY), MachineStuff.desktopBounds));
        }

        private static Point ComputeSameMachineLanding(MoveDirection direction, int x, int y, MonitorTransitionResult result)
        {
            var targetPhys = NavigationMath.FindPhysicalMonitor(result.TargetMonitorId);
            var db = MachineStuff.desktopBounds;
            Point pt;
            switch (direction)
            {
                case MoveDirection.Right:
                {
                    int targetX = (targetPhys?.Left ?? db.Left) + NavigationMath.JumpPixels;
                    int clampedY = targetPhys.HasValue
                        ? NavigationMath.ClampToSafeMonitorCoord(y, targetPhys.Value.Top, targetPhys.Value.Bottom) : y;
                    pt = NavigationMath.ConvertToUniversalValue(new Point(targetX, clampedY), db);
                    Logger.LogDebug($"  Right same-machine landing: targetX={targetX} clampedY={clampedY} universal=({pt.X},{pt.Y})");
                    return pt;
                }

                case MoveDirection.Left:
                {
                    int targetX = (targetPhys?.Right ?? db.Right) - NavigationMath.JumpPixels;
                    int clampedY = targetPhys.HasValue
                        ? NavigationMath.ClampToSafeMonitorCoord(y, targetPhys.Value.Top, targetPhys.Value.Bottom) : y;
                    pt = NavigationMath.ConvertToUniversalValue(new Point(targetX, clampedY), db);
                    Logger.LogDebug($"  Left same-machine landing: targetX={targetX} clampedY={clampedY} universal=({pt.X},{pt.Y})");
                    return pt;
                }

                case MoveDirection.Up:
                {
                    int targetY = (targetPhys?.Bottom ?? db.Bottom) - NavigationMath.JumpPixels;
                    int clampedX = targetPhys.HasValue
                        ? NavigationMath.ClampToSafeMonitorCoord(x, targetPhys.Value.Left, targetPhys.Value.Right) : x;
                    pt = NavigationMath.ConvertToUniversalValue(new Point(clampedX, targetY), db);
                    Logger.LogDebug($"  Up same-machine landing: clampedX={clampedX} targetY={targetY} universal=({pt.X},{pt.Y})");
                    return pt;
                }

                case MoveDirection.Down:
                {
                    int targetY = (targetPhys?.Top ?? db.Top) + NavigationMath.JumpPixels;
                    int clampedX = targetPhys.HasValue
                        ? NavigationMath.ClampToSafeMonitorCoord(x, targetPhys.Value.Left, targetPhys.Value.Right) : x;
                    pt = NavigationMath.ConvertToUniversalValue(new Point(clampedX, targetY), db);
                    Logger.LogDebug($"  Down same-machine landing: clampedX={clampedX} targetY={targetY} universal=({pt.X},{pt.Y})");
                    return pt;
                }

                default:
                    return Point.Empty;
            }
        }

        private Point ComputeRemoteMachineLanding(
            MoveDirection direction,
            MonitorTransitionResult result,
            int physX,
            int physY,
            WinAPI.PhysicalMonitorInfo? srcPhys)
        {
            var targetBounds = Manager.GetMachineBounds(result.TargetMachine);
            int targetMachineWidth = targetBounds != null ? Math.Max(1, targetBounds.Right - targetBounds.Left) : 1;
            int targetMachineHeight = targetBounds != null ? Math.Max(1, targetBounds.Bottom - targetBounds.Top) : 1;

            Point pt;
            switch (direction)
            {
                case MoveDirection.Right:
                {
                    double relY = ProportionalPosition(physY, srcPhys?.Top, srcPhys?.Bottom, result.CanvasCursorY, result.SrcCanvasY, result.SrcCanvasHeight);
                    int landingX = MapToTargetMonitorUniversal(NavigationMath.JumpPixels, result.TargetCanvasX, result.TargetCanvasWidth, targetBounds?.Left ?? 0, targetMachineWidth, isNearFar: false);
                    int landingY = MapToTargetMonitorUniversal(relY, result.TargetCanvasY, result.TargetCanvasHeight, targetBounds?.Top ?? 0, targetMachineHeight);
                    pt = new Point(landingX, landingY);
                    Logger.LogDebug($"  Right remote landing: relY={relY:F3} universal=({pt.X},{pt.Y})");
                    return pt;
                }

                case MoveDirection.Left:
                {
                    double relY = ProportionalPosition(physY, srcPhys?.Top, srcPhys?.Bottom, result.CanvasCursorY, result.SrcCanvasY, result.SrcCanvasHeight);
                    int landingX = MapToTargetMonitorUniversal(NavigationMath.JumpPixels, result.TargetCanvasX, result.TargetCanvasWidth, targetBounds?.Left ?? 0, targetMachineWidth, isNearFar: true);
                    int landingY = MapToTargetMonitorUniversal(relY, result.TargetCanvasY, result.TargetCanvasHeight, targetBounds?.Top ?? 0, targetMachineHeight);
                    pt = new Point(landingX, landingY);
                    Logger.LogDebug($"  Left remote landing: relY={relY:F3} universal=({pt.X},{pt.Y})");
                    return pt;
                }

                case MoveDirection.Up:
                {
                    double relX = ProportionalPosition(physX, srcPhys?.Left, srcPhys?.Right, result.CanvasCursorX, result.SrcCanvasX, result.SrcCanvasWidth);
                    int landingX = MapToTargetMonitorUniversal(relX, result.TargetCanvasX, result.TargetCanvasWidth, targetBounds?.Left ?? 0, targetMachineWidth);
                    int landingY = MapToTargetMonitorUniversal(NavigationMath.JumpPixels, result.TargetCanvasY, result.TargetCanvasHeight, targetBounds?.Top ?? 0, targetMachineHeight, isNearFar: true);
                    pt = new Point(landingX, landingY);
                    Logger.LogDebug($"  Up remote landing: relX={relX:F3} universal=({pt.X},{pt.Y})");
                    return pt;
                }

                case MoveDirection.Down:
                {
                    double relX = ProportionalPosition(physX, srcPhys?.Left, srcPhys?.Right, result.CanvasCursorX, result.SrcCanvasX, result.SrcCanvasWidth);
                    int landingX = MapToTargetMonitorUniversal(relX, result.TargetCanvasX, result.TargetCanvasWidth, targetBounds?.Left ?? 0, targetMachineWidth);
                    int landingY = MapToTargetMonitorUniversal(NavigationMath.JumpPixels, result.TargetCanvasY, result.TargetCanvasHeight, targetBounds?.Top ?? 0, targetMachineHeight, isNearFar: false);
                    pt = new Point(landingX, landingY);
                    Logger.LogDebug($"  Down remote landing: relX={relX:F3} universal=({pt.X},{pt.Y})");
                    return pt;
                }

                default:
                    return Point.Empty;
            }
        }

        /// <summary>
        /// Maps a proportional position (0.0–1.0) within a target monitor to the 0–65535
        /// universal coordinate space of the target machine's full virtual desktop.
        /// This ensures cursor at fraction <paramref name="rel"/> of the source monitor
        /// lands at the same fraction of the target monitor rather than the target machine's
        /// entire virtual desktop.
        /// </summary>
        /// <param name="rel">Fractional position within the target monitor (0.0–1.0).</param>
        /// <param name="monitorOrigin">Canvas X or Y of the target monitor.</param>
        /// <param name="monitorSize">Canvas width or height of the target monitor.</param>
        /// <param name="machineOrigin">Canvas X or Y of the target machine's bounding box.</param>
        /// <param name="machineSize">Total canvas width or height of the target machine.</param>
        private static int MapToTargetMonitorUniversal(double rel, int monitorOrigin, int monitorSize, int machineOrigin, int machineSize)
        {
            if (monitorSize <= 0 || machineSize <= 0)
            {
                return Math.Clamp((int)(rel * 65535), 0, 65535);
            }

            double monitorStartFrac = (double)(monitorOrigin - machineOrigin) / machineSize;
            double landingFrac = monitorStartFrac + (rel * monitorSize / machineSize);
            return Math.Clamp((int)(landingFrac * 65535), 0, 65535);
        }

        /// <summary>
        /// Overload for fixed pixel offsets from the near or far edge of the target monitor
        /// (e.g. <see cref="NavigationMath.JumpPixels"/> inside the entry edge).
        /// </summary>
        /// <param name="jumpPixels">Pixel offset from the entry edge.</param>
        /// <param name="isNearFar"><c>false</c> = near edge (e.g. entering from the left/top);
        /// <c>true</c> = far edge (entering from the right/bottom).</param>
        private static int MapToTargetMonitorUniversal(int jumpPixels, int monitorOrigin, int monitorSize, int machineOrigin, int machineSize, bool isNearFar = false)
        {
            double rel = isNearFar
                ? (double)(monitorSize - jumpPixels) / monitorSize
                : (double)jumpPixels / monitorSize;
            return MapToTargetMonitorUniversal(rel, monitorOrigin, monitorSize, machineOrigin, machineSize);
        }

        /// <summary>
        /// Returns the cursor's proportional position (0.0–1.0) along a monitor edge.
        /// Uses physical pixel coordinates when available; falls back to canvas coordinates.
        /// </summary>
        internal static double ProportionalPosition(
            int physCoord,
            int? physMin,
            int? physMax,
            int canvasCoord,
            int canvasMin,
            int canvasSize)
        {
            if (physMin.HasValue && physMax.HasValue && physMax.Value > physMin.Value)
            {
                return Math.Clamp((double)(physCoord - physMin.Value) / (physMax.Value - physMin.Value), 0.0, 1.0);
            }

            if (canvasSize > 0)
            {
                return Math.Clamp((double)(canvasCoord - canvasMin) / canvasSize, 0.0, 1.0);
            }

            return 0.5;
        }

        private static Point ComputeLocalMachineLandingFromRemote(
            MoveDirection direction,
            MonitorTransitionResult result,
            int cx,
            int cy,
            int primaryX,
            int primaryY)
        {
            var targetPhys = NavigationMath.FindPhysicalMonitor(result.TargetMonitorId);
            Logger.LogDebug($"  local landing: targetMonitor={result.TargetMonitorId} physFound={targetPhys.HasValue} " +
                $"canvasTarget=({result.TargetCanvasX},{result.TargetCanvasY},{result.TargetCanvasWidth}x{result.TargetCanvasHeight})");

            int targetX, targetY;
            var db = MachineStuff.desktopBounds;

            switch (direction)
            {
                case MoveDirection.Up:
                    targetY = (targetPhys?.Bottom ?? db.Bottom) - NavigationMath.JumpPixels;
                    targetX = ComputeProportionalPhysX(cx, result.SrcCanvasX, result.SrcCanvasWidth, targetPhys, db, primaryX);
                    break;
                case MoveDirection.Down:
                    targetY = (targetPhys?.Top ?? db.Top) + NavigationMath.JumpPixels;
                    targetX = ComputeProportionalPhysX(cx, result.SrcCanvasX, result.SrcCanvasWidth, targetPhys, db, primaryX);
                    break;
                case MoveDirection.Left:
                    targetX = (targetPhys?.Right ?? db.Right) - NavigationMath.JumpPixels;
                    targetY = ComputeProportionalPhysY(cy, result.SrcCanvasY, result.SrcCanvasHeight, targetPhys, db, primaryY);
                    break;
                case MoveDirection.Right:
                    targetX = (targetPhys?.Left ?? db.Left) + NavigationMath.JumpPixels;
                    targetY = ComputeProportionalPhysY(cy, result.SrcCanvasY, result.SrcCanvasHeight, targetPhys, db, primaryY);
                    break;
                default:
                    return Point.Empty;
            }

            var universal = NavigationMath.ConvertToUniversalValue(new Point(targetX, targetY), db);
            Logger.LogDebug($"  landing phys=({targetX},{targetY}) universal=({universal.X},{universal.Y})");
            return universal;
        }

        private static int ComputeProportionalPhysX(
            int canvasCursorX,
            int srcCanvasX,
            int srcCanvasWidth,
            WinAPI.PhysicalMonitorInfo? targetPhys,
            MyRectangle db,
            int fallback)
        {
            if (!targetPhys.HasValue || srcCanvasWidth <= 0)
            {
                return fallback;
            }

            double relX = Math.Clamp((double)(canvasCursorX - srcCanvasX) / srcCanvasWidth, 0.0, 1.0);
            int physWidth = targetPhys.Value.Right - targetPhys.Value.Left;
            return NavigationMath.ClampToSafeMonitorCoord(
                targetPhys.Value.Left + (int)(relX * physWidth),
                targetPhys.Value.Left,
                targetPhys.Value.Right);
        }

        private static int ComputeProportionalPhysY(
            int canvasCursorY,
            int srcCanvasY,
            int srcCanvasHeight,
            WinAPI.PhysicalMonitorInfo? targetPhys,
            MyRectangle db,
            int fallback)
        {
            if (!targetPhys.HasValue || srcCanvasHeight <= 0)
            {
                return fallback;
            }

            double relY = Math.Clamp((double)(canvasCursorY - srcCanvasY) / srcCanvasHeight, 0.0, 1.0);
            int physHeight = targetPhys.Value.Bottom - targetPhys.Value.Top;
            return NavigationMath.ClampToSafeMonitorCoord(
                targetPhys.Value.Top + (int)(relY * physHeight),
                targetPhys.Value.Top,
                targetPhys.Value.Bottom);
        }
    }
}
