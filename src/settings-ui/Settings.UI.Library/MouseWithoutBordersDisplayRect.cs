// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Text.Json.Serialization;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    /// <summary>
    /// Represents a display rectangle with pixel coordinates.
    /// </summary>
    public class MouseWithoutBordersDisplayRect
    {
        public int Left { get; set; }

        public int Top { get; set; }

        public int Right { get; set; }

        public int Bottom { get; set; }

        [JsonIgnore]
        public int Width => Right - Left;

        [JsonIgnore]
        public int Height => Bottom - Top;

        public MouseWithoutBordersDisplayRect()
        {
        }

        public MouseWithoutBordersDisplayRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }
}
