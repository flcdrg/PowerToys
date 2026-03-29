// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.PowerToys.Settings.UI.Library
{
    /// <summary>
    /// Describes the placement of a remote machine relative to a specific display edge.
    /// </summary>
    public class MouseWithoutBordersDisplayLayoutDevicePosition
    {
        public string MachineName { get; set; }

        public MouseWithoutBordersDisplayEdge Edge { get; set; }

        /// <summary>
        /// Gets or sets the zero-based index of the display this machine is adjacent to.
        /// </summary>
        public int DisplayIndex { get; set; }

        public MouseWithoutBordersDisplayLayoutDevicePosition()
        {
            MachineName = string.Empty;
        }

        public MouseWithoutBordersDisplayLayoutDevicePosition(string machineName, MouseWithoutBordersDisplayEdge edge, int displayIndex)
        {
            MachineName = machineName ?? string.Empty;
            Edge = edge;
            DisplayIndex = displayIndex;
        }
    }
}
