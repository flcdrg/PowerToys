// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System.Collections.Generic;
using System.Drawing;
using Microsoft.PowerToys.Settings.UI.Library;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.Core
{
    internal record struct EdgeTransition(ID TargetMachineId, Point LandingPosition);

    /// <summary>
    /// Abstracts the navigation strategy used to resolve cross-monitor and cross-machine
    /// cursor transitions. Two implementations exist: <see cref="MonitorLayoutNavigator"/>
    /// (per-monitor adjacency graph) and <see cref="DeviceLayoutNavigator"/> (machine matrix).
    /// <see cref="MachineStuff"/> holds a reference to the active implementation and switches
    /// it whenever <c>UseMonitorLayout</c> changes.
    /// </summary>
    internal interface ILayoutNavigator
    {
        /// <summary>Rebuilds internal state from an updated layout. Pass <c>null</c> to clear.</summary>
        void OnLayoutChanged(List<MonitorLayoutInfo>? layout);

        /// <summary>
        /// Detects whether the cursor at (<paramref name="x"/>, <paramref name="y"/>) has
        /// reached a monitor edge on the local machine and, if so, resolves the transition.
        /// </summary>
        EdgeTransition? CheckLocalMachineEdge(int x, int y, string currentMachine);

        /// <summary>
        /// Resolves a directional edge transition for the local machine given the physical
        /// cursor position and the monitor that contains it. Returns <c>null</c> when this
        /// navigator cannot handle the transition (no neighbour, not resolved, etc.).
        /// </summary>
        EdgeTransition? TryResolveLocalEdge(
            MoveDirection direction,
            string currentMachine,
            int x,
            int y,
            string? localMonitorId,
            int physMonitorLeft,
            int physMonitorTop);

        /// <summary>
        /// Resolves a directional edge transition for a cursor on a remote machine where
        /// (<paramref name="primaryX"/>, <paramref name="primaryY"/>) are in the controller's
        /// <c>primaryScreenBounds</c> coordinate space. Returns <c>null</c> when this
        /// navigator cannot handle the transition.
        /// </summary>
        EdgeTransition? TryResolveRemoteEdge(
            MoveDirection direction,
            string remoteMachine,
            int primaryX,
            int primaryY);
    }
}
