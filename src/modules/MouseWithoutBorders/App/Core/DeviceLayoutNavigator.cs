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
    /// <see cref="ILayoutNavigator"/> implementation for the device matrix layout mode.
    /// Routes cursor transitions through the 4-slot machine matrix
    /// (<see cref="MachineStuff.LiveMachineMatrix"/>) without per-monitor awareness.
    /// </summary>
    internal sealed class DeviceLayoutNavigator : ILayoutNavigator
    {
        public void OnLayoutChanged(List<MonitorLayoutInfo>? layout)
        {
            // Device matrix navigation does not use the monitor layout data.
        }

        public EdgeTransition? CheckLocalMachineEdge(int x, int y, string currentMachine)
        {
            var db = MachineStuff.desktopBounds;
            MoveDirection? dir = null;
            if (x < db.Left + NavigationMath.SkipPixels)
            {
                dir = MoveDirection.Left;
            }
            else if (x >= db.Right - NavigationMath.SkipPixels)
            {
                dir = MoveDirection.Right;
            }
            else if (y < db.Top + NavigationMath.SkipPixels)
            {
                dir = MoveDirection.Up;
            }
            else if (y >= db.Bottom - NavigationMath.SkipPixels)
            {
                dir = MoveDirection.Down;
            }

            return dir.HasValue ? TryResolveLocalEdge(dir.Value, currentMachine, x, y, null, 0, 0) : null;
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
            string[] mc = MachineStuff.LiveMachineMatrix;
            if (mc == null)
            {
                return null;
            }

            // Up and Down are invalid in single-row matrix mode.
            if (Setting.Values.MatrixOneRow
                && (direction == MoveDirection.Up || direction == MoveDirection.Down))
            {
                return null;
            }

            ID newID = direction switch
            {
                MoveDirection.Right => ResolveMatrixRight(currentMachine, mc),
                MoveDirection.Left => ResolveMatrixLeft(currentMachine, mc),
                MoveDirection.Up => ResolveMatrixUp(currentMachine, mc),
                MoveDirection.Down => ResolveMatrixDown(currentMachine, mc),
                _ => ID.NONE,
            };

            if (newID == ID.NONE || newID == MachineStuff.desMachineID)
            {
                return null;
            }

            Logger.LogDebug($"Move {direction}");
            return new EdgeTransition(newID, ComputeMatrixLanding(direction, x, y, newID));
        }

        public EdgeTransition? TryResolveRemoteEdge(
            MoveDirection direction,
            string remoteMachine,
            int primaryX,
            int primaryY)
        {
            // Device matrix has no special handling for remote-machine edge detection.
            return null;
        }

        private static ID ResolveMatrixRight(string currentMachine, string[] mc)
        {
            bool oneRow = Setting.Values.MatrixOneRow;
            ID newID = ID.NONE;

            if (oneRow)
            {
                bool found = false;
                for (int i = 0; i < MachineStuff.MAX_MACHINE; i++)
                {
                    if (!currentMachine.Trim().Equals(mc[i], StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    for (int j = i; j < MachineStuff.MAX_MACHINE - 1; j++)
                    {
                        if (mc[j + 1] != null && mc[j + 1].Length > 0 && (newID = MachineStuff.IdFromName(mc[j + 1])) > 0)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found && Setting.Values.MatrixCircle)
                    {
                        for (int j = 0; j < i; j++)
                        {
                            if (mc[j] != null && mc[j].Length > 0 && (newID = MachineStuff.IdFromName(mc[j])) > 0)
                            {
                                break;
                            }
                        }
                    }

                    break;
                }
            }
            else
            {
                if (currentMachine.Trim().Equals(mc[0], StringComparison.OrdinalIgnoreCase) && mc[1] != null && mc[1].Length > 0)
                {
                    newID = MachineStuff.IdFromName(mc[1]);
                }
                else if (currentMachine.Trim().Equals(mc[2], StringComparison.OrdinalIgnoreCase) && mc[3] != null && mc[3].Length > 0)
                {
                    newID = MachineStuff.IdFromName(mc[3]);
                }
                else if (Setting.Values.MatrixCircle && currentMachine.Trim().Equals(mc[1], StringComparison.OrdinalIgnoreCase) && mc[0] != null && mc[0].Length > 0)
                {
                    newID = MachineStuff.IdFromName(mc[0]);
                }
                else if (Setting.Values.MatrixCircle && currentMachine.Trim().Equals(mc[3], StringComparison.OrdinalIgnoreCase) && mc[2] != null && mc[2].Length > 0)
                {
                    newID = MachineStuff.IdFromName(mc[2]);
                }
            }

            return newID > 0 ? newID : ID.NONE;
        }

        private static ID ResolveMatrixLeft(string currentMachine, string[] mc)
        {
            bool oneRow = Setting.Values.MatrixOneRow;
            ID newID = ID.NONE;

            if (oneRow)
            {
                bool found = false;
                for (int i = MachineStuff.MAX_MACHINE - 1; i >= 0; i--)
                {
                    if (!currentMachine.Trim().Equals(mc[i], StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    for (int j = i; j > 0; j--)
                    {
                        if (mc[j - 1] != null && mc[j - 1].Length > 0 && (newID = MachineStuff.IdFromName(mc[j - 1])) != ID.NONE)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found && Setting.Values.MatrixCircle)
                    {
                        for (int j = MachineStuff.MAX_MACHINE - 1; j > i; j--)
                        {
                            if (mc[j] != null && mc[j].Length > 0 && (newID = MachineStuff.IdFromName(mc[j])) != ID.NONE)
                            {
                                break;
                            }
                        }
                    }

                    break;
                }
            }
            else
            {
                if (currentMachine.Trim().Equals(mc[1], StringComparison.OrdinalIgnoreCase) && mc[0] != null && mc[0].Length > 0)
                {
                    newID = MachineStuff.IdFromName(mc[0]);
                }
                else if (currentMachine.Trim().Equals(mc[3], StringComparison.OrdinalIgnoreCase) && mc[2] != null && mc[2].Length > 0)
                {
                    newID = MachineStuff.IdFromName(mc[2]);
                }
                else if (Setting.Values.MatrixCircle && currentMachine.Trim().Equals(mc[0], StringComparison.OrdinalIgnoreCase) && mc[1] != null && mc[1].Length > 0)
                {
                    newID = MachineStuff.IdFromName(mc[1]);
                }
                else if (Setting.Values.MatrixCircle && currentMachine.Trim().Equals(mc[2], StringComparison.OrdinalIgnoreCase) && mc[3] != null && mc[3].Length > 0)
                {
                    newID = MachineStuff.IdFromName(mc[3]);
                }
            }

            return newID != ID.NONE ? newID : ID.NONE;
        }

        private static ID ResolveMatrixUp(string currentMachine, string[] mc)
        {
            ID newID = ID.NONE;
            if (currentMachine.Trim().Equals(mc[2], StringComparison.OrdinalIgnoreCase) && mc[0] != null && mc[0].Length > 0)
            {
                newID = MachineStuff.IdFromName(mc[0]);
            }
            else if (currentMachine.Trim().Equals(mc[3], StringComparison.OrdinalIgnoreCase) && mc[1] != null && mc[1].Length > 0)
            {
                newID = MachineStuff.IdFromName(mc[1]);
            }
            else if (Setting.Values.MatrixCircle && currentMachine.Trim().Equals(mc[0], StringComparison.OrdinalIgnoreCase) && mc[2] != null && mc[2].Length > 0)
            {
                newID = MachineStuff.IdFromName(mc[2]);
            }
            else if (Setting.Values.MatrixCircle && currentMachine.Trim().Equals(mc[1], StringComparison.OrdinalIgnoreCase) && mc[3] != null && mc[3].Length > 0)
            {
                newID = MachineStuff.IdFromName(mc[3]);
            }

            return newID != ID.NONE ? newID : ID.NONE;
        }

        private static ID ResolveMatrixDown(string currentMachine, string[] mc)
        {
            ID newID = ID.NONE;
            if (currentMachine.Trim().Equals(mc[0], StringComparison.OrdinalIgnoreCase) && mc[2] != null && mc[2].Length > 0)
            {
                newID = MachineStuff.IdFromName(mc[2]);
            }
            else if (currentMachine.Trim().Equals(mc[1], StringComparison.OrdinalIgnoreCase) && mc[3] != null && mc[3].Length > 0)
            {
                newID = MachineStuff.IdFromName(mc[3]);
            }
            else if (Setting.Values.MatrixCircle && currentMachine.Trim().Equals(mc[2], StringComparison.OrdinalIgnoreCase) && mc[0] != null && mc[0].Length > 0)
            {
                newID = MachineStuff.IdFromName(mc[0]);
            }
            else if (Setting.Values.MatrixCircle && currentMachine.Trim().Equals(mc[3], StringComparison.OrdinalIgnoreCase) && mc[1] != null && mc[1].Length > 0)
            {
                newID = MachineStuff.IdFromName(mc[1]);
            }

            return newID != ID.NONE ? newID : ID.NONE;
        }

        private static Point ComputeMatrixLanding(MoveDirection direction, int x, int y, ID newID)
        {
            bool moveRelatively = Setting.Values.MoveMouseRelatively;
            var db = MachineStuff.desktopBounds;
            var psb = MachineStuff.primaryScreenBounds;
            var desId = MachineStuff.desMachineID;

            switch (direction)
            {
                case MoveDirection.Right:
                    return moveRelatively
                        ? NavigationMath.ConvertToUniversalValue(new Point(db.Left + NavigationMath.JumpPixels, y), db)
                        : newID == Common.MachineID
                            ? NavigationMath.ConvertToUniversalValue(new Point(psb.Left + NavigationMath.JumpPixels, y), psb)
                            : desId == Common.MachineID
                                ? NavigationMath.ConvertToUniversalValue(new Point(db.Left + NavigationMath.JumpPixels, y), db)
                                : NavigationMath.ConvertToUniversalValue(new Point(psb.Left + NavigationMath.JumpPixels, y), psb);

                case MoveDirection.Left:
                    return moveRelatively
                        ? NavigationMath.ConvertToUniversalValue(new Point(db.Right - NavigationMath.JumpPixels, y), db)
                        : newID == Common.MachineID
                            ? NavigationMath.ConvertToUniversalValue(new Point(psb.Right - NavigationMath.JumpPixels, y), psb)
                            : desId == Common.MachineID
                                ? NavigationMath.ConvertToUniversalValue(new Point(db.Right - NavigationMath.JumpPixels, y), db)
                                : NavigationMath.ConvertToUniversalValue(new Point(psb.Right - NavigationMath.JumpPixels, y), psb);

                case MoveDirection.Up:
                    return moveRelatively
                        ? NavigationMath.ConvertToUniversalValue(new Point(x, db.Bottom - NavigationMath.JumpPixels), db)
                        : newID == Common.MachineID
                            ? NavigationMath.ConvertToUniversalValue(new Point(x, psb.Bottom - NavigationMath.JumpPixels), psb)
                            : desId == Common.MachineID
                                ? NavigationMath.ConvertToUniversalValue(new Point(x, db.Bottom - NavigationMath.JumpPixels), db)
                                : NavigationMath.ConvertToUniversalValue(new Point(x, psb.Bottom - NavigationMath.JumpPixels), psb);

                case MoveDirection.Down:
                    return moveRelatively
                        ? NavigationMath.ConvertToUniversalValue(new Point(x, db.Top + NavigationMath.JumpPixels), db)
                        : newID == Common.MachineID
                            ? NavigationMath.ConvertToUniversalValue(new Point(x, psb.Top + NavigationMath.JumpPixels), psb)
                            : desId == Common.MachineID
                                ? NavigationMath.ConvertToUniversalValue(new Point(x, db.Top + NavigationMath.JumpPixels), db)
                                : NavigationMath.ConvertToUniversalValue(new Point(x, psb.Top + NavigationMath.JumpPixels), psb);

                default:
                    return Point.Empty;
            }
        }
    }
}
