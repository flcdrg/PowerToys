// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CommonLibTest
{
    [TestClass]
    public class DisplayLayoutConfigurationTests
    {
        // ---- DisplayRect ----

        [TestMethod]
        public void DisplayRect_WidthAndHeight_ComputedCorrectly()
        {
            var r = new MouseWithoutBordersDisplayRect(10, 20, 110, 70);
            Assert.AreEqual(100, r.Width);
            Assert.AreEqual(50, r.Height);
        }

        [TestMethod]
        public void DisplayRect_DefaultConstructor_ZeroValues()
        {
            var r = new MouseWithoutBordersDisplayRect();
            Assert.AreEqual(0, r.Left);
            Assert.AreEqual(0, r.Top);
            Assert.AreEqual(0, r.Right);
            Assert.AreEqual(0, r.Bottom);
        }

        [TestMethod]
        public void DisplayRect_SerializationRoundtrip()
        {
            var r = new MouseWithoutBordersDisplayRect(5, 10, 1925, 1090);
            string json = JsonSerializer.Serialize(r);
            var r2 = JsonSerializer.Deserialize<MouseWithoutBordersDisplayRect>(json);
            Assert.IsNotNull(r2);
            Assert.AreEqual(r.Left, r2.Left);
            Assert.AreEqual(r.Top, r2.Top);
            Assert.AreEqual(r.Right, r2.Right);
            Assert.AreEqual(r.Bottom, r2.Bottom);
        }

        [TestMethod]
        public void DisplayRect_WidthHeight_NotSerializedAsFields()
        {
            // Width and Height are [JsonIgnore]; they must not appear in the JSON.
            var r = new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080);
            string json = JsonSerializer.Serialize(r);
            Assert.IsFalse(json.Contains("\"Width\""), "Width should not appear in JSON");
            Assert.IsFalse(json.Contains("\"Height\""), "Height should not appear in JSON");
        }

        // ---- DisplayLayoutConfiguration.ToMachineMatrix ----

        [TestMethod]
        public void ToMachineMatrix_Empty_ReturnsFourEmptySlots()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration();
            var matrix = cfg.ToMachineMatrix("LOCAL");
            Assert.AreEqual(4, matrix.Count);
            Assert.IsTrue(matrix.All(s => string.IsNullOrEmpty(s)));
        }

        [TestMethod]
        public void ToMachineMatrix_OneRightMachine_PlacedAfterLocal()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("REMOTE1", MouseWithoutBordersDisplayEdge.Right, 0),
                },
            };

            var matrix = cfg.ToMachineMatrix("LOCAL");
            Assert.AreEqual(4, matrix.Count);

            // LOCAL first (no left-edge machines), then REMOTE1 on right
            Assert.AreEqual("LOCAL", matrix[0]);
            Assert.AreEqual("REMOTE1", matrix[1]);
        }

        [TestMethod]
        public void ToMachineMatrix_OneLeftMachine_PlacedBeforeLocal()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("LEFTPC", MouseWithoutBordersDisplayEdge.Left, 0),
                },
            };

            var matrix = cfg.ToMachineMatrix("LOCAL");
            Assert.AreEqual("LEFTPC", matrix[0]);
            Assert.AreEqual("LOCAL", matrix[1]);
        }

        [TestMethod]
        public void ToMachineMatrix_LeftAndRightMachines_CorrectOrder()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("LEFTPC", MouseWithoutBordersDisplayEdge.Left, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("RIGHTPC", MouseWithoutBordersDisplayEdge.Right, 0),
                },
            };

            var matrix = cfg.ToMachineMatrix("LOCAL");
            Assert.AreEqual("LEFTPC", matrix[0]);
            Assert.AreEqual("LOCAL", matrix[1]);
            Assert.AreEqual("RIGHTPC", matrix[2]);
        }

        [TestMethod]
        public void ToMachineMatrix_TopMachine_PlacedInOtherSlot()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("TOPPC", MouseWithoutBordersDisplayEdge.Top, 0),
                },
            };

            var matrix = cfg.ToMachineMatrix("LOCAL");
            Assert.AreEqual(4, matrix.Count);
            Assert.IsTrue(matrix.Contains("TOPPC"), "TOPPC should appear in the matrix");
        }

        [TestMethod]
        public void ToMachineMatrix_MaxMachines_FillsFourSlots()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect>
                {
                    new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080),
                    new MouseWithoutBordersDisplayRect(1920, 0, 3840, 1080),
                },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("A", MouseWithoutBordersDisplayEdge.Left, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("B", MouseWithoutBordersDisplayEdge.Right, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("C", MouseWithoutBordersDisplayEdge.Right, 1),
                },
            };

            var matrix = cfg.ToMachineMatrix("LOCAL");
            Assert.AreEqual(4, matrix.Count);
            // First 4 of [A, LOCAL, B, C] — all fit
            Assert.IsTrue(matrix.Contains("A"));
            Assert.IsTrue(matrix.Contains("LOCAL"));
            Assert.IsTrue(matrix.Contains("B"));
            Assert.IsTrue(matrix.Contains("C"));
        }

        [TestMethod]
        public void ToMachineMatrix_NullLocalMachineName_DoesNotCrash()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("REMOTE", MouseWithoutBordersDisplayEdge.Right, 0),
                },
            };

            var matrix = cfg.ToMachineMatrix(null);
            Assert.AreEqual(4, matrix.Count);
        }

        [TestMethod]
        public void ToMachineMatrix_NullPositions_DoesNotCrash()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                DevicePositions = null,
            };

            var matrix = cfg.ToMachineMatrix("LOCAL");
            Assert.AreEqual(4, matrix.Count);
        }

        // ---- Sanitize ----

        [TestMethod]
        public void Sanitize_RemovesOutOfRangeDisplayIndex()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("GOOD", MouseWithoutBordersDisplayEdge.Right, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("BAD", MouseWithoutBordersDisplayEdge.Right, 5), // index 5 doesn't exist
                },
            };

            cfg.Sanitize();
            Assert.AreEqual(1, cfg.DevicePositions.Count);
            Assert.AreEqual("GOOD", cfg.DevicePositions[0].MachineName);
        }

        [TestMethod]
        public void Sanitize_RemovesEmptyMachineNames()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition(string.Empty, MouseWithoutBordersDisplayEdge.Right, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("   ", MouseWithoutBordersDisplayEdge.Left, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("GOOD", MouseWithoutBordersDisplayEdge.Bottom, 0),
                },
            };

            cfg.Sanitize();
            Assert.AreEqual(1, cfg.DevicePositions.Count);
            Assert.AreEqual("GOOD", cfg.DevicePositions[0].MachineName);
        }

        [TestMethod]
        public void Sanitize_RemovesDuplicateMachineNames()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect>
                {
                    new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080),
                    new MouseWithoutBordersDisplayRect(1920, 0, 3840, 1080),
                },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("PC1", MouseWithoutBordersDisplayEdge.Right, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("PC1", MouseWithoutBordersDisplayEdge.Left, 1), // duplicate
                },
            };

            cfg.Sanitize();
            Assert.AreEqual(1, cfg.DevicePositions.Count);
        }

        // ---- FindFirstAvailableEdge ----

        [TestMethod]
        public void FindFirstAvailableEdge_EmptyConfig_ReturnsRightEdgeOfLastDisplay()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
            };

            var result = cfg.FindFirstAvailableEdge();
            Assert.IsNotNull(result);
            Assert.AreEqual(MouseWithoutBordersDisplayEdge.Right, result.Value.Edge);
        }

        [TestMethod]
        public void FindFirstAvailableEdge_AllEdgesOccupied_ReturnsNull()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("A", MouseWithoutBordersDisplayEdge.Right, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("B", MouseWithoutBordersDisplayEdge.Left, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("C", MouseWithoutBordersDisplayEdge.Top, 0),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("D", MouseWithoutBordersDisplayEdge.Bottom, 0),
                },
            };

            var result = cfg.FindFirstAvailableEdge();
            Assert.IsNull(result);
        }

        [TestMethod]
        public void FindFirstAvailableEdge_NoDisplays_ReturnsNull()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration();
            var result = cfg.FindFirstAvailableEdge();
            Assert.IsNull(result);
        }

        // ---- Full serialization roundtrip ----

        [TestMethod]
        public void DisplayLayoutConfiguration_FullSerializationRoundtrip()
        {
            var cfg = new MouseWithoutBordersDisplayLayoutConfiguration
            {
                Displays = new List<MouseWithoutBordersDisplayRect>
                {
                    new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080),
                    new MouseWithoutBordersDisplayRect(1920, 0, 3840, 1080),
                },
                DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                {
                    new MouseWithoutBordersDisplayLayoutDevicePosition("REMOTE1", MouseWithoutBordersDisplayEdge.Right, 1),
                    new MouseWithoutBordersDisplayLayoutDevicePosition("REMOTE2", MouseWithoutBordersDisplayEdge.Left, 0),
                },
            };

            string json = JsonSerializer.Serialize(cfg);
            var cfg2 = JsonSerializer.Deserialize<MouseWithoutBordersDisplayLayoutConfiguration>(json);

            Assert.IsNotNull(cfg2);
            Assert.AreEqual(2, cfg2.Displays.Count);
            Assert.AreEqual(2, cfg2.DevicePositions.Count);
            Assert.AreEqual("REMOTE1", cfg2.DevicePositions[0].MachineName);
            Assert.AreEqual(MouseWithoutBordersDisplayEdge.Right, cfg2.DevicePositions[0].Edge);
            Assert.AreEqual(1, cfg2.DevicePositions[0].DisplayIndex);
            Assert.AreEqual("REMOTE2", cfg2.DevicePositions[1].MachineName);
        }

        // ---- MouseWithoutBordersProperties integration ----

        [TestMethod]
        public void MouseWithoutBordersProperties_DisplayLayout_NullByDefault()
        {
            var props = new MouseWithoutBordersProperties();
            Assert.IsNull(props.DisplayLayout, "DisplayLayout should default to null for backward compatibility");
        }

        [TestMethod]
        public void MouseWithoutBordersProperties_DisplayLayout_NotIncludedInJsonWhenNull()
        {
            var props = new MouseWithoutBordersProperties();
            string json = JsonSerializer.Serialize(props);
            Assert.IsFalse(json.Contains("DisplayLayout"), "DisplayLayout should not appear in JSON when null");
        }

        [TestMethod]
        public void MouseWithoutBordersProperties_DisplayLayout_CanRoundtrip()
        {
            var props = new MouseWithoutBordersProperties
            {
                DisplayLayout = new MouseWithoutBordersDisplayLayoutConfiguration
                {
                    Displays = new List<MouseWithoutBordersDisplayRect> { new MouseWithoutBordersDisplayRect(0, 0, 1920, 1080) },
                    DevicePositions = new List<MouseWithoutBordersDisplayLayoutDevicePosition>
                    {
                        new MouseWithoutBordersDisplayLayoutDevicePosition("PC2", MouseWithoutBordersDisplayEdge.Right, 0),
                    },
                },
            };

            string json = JsonSerializer.Serialize(props);
            var props2 = JsonSerializer.Deserialize<MouseWithoutBordersProperties>(json);

            Assert.IsNotNull(props2?.DisplayLayout);
            Assert.AreEqual(1, props2.DisplayLayout.Displays.Count);
            Assert.AreEqual(1, props2.DisplayLayout.DevicePositions.Count);
            Assert.AreEqual("PC2", props2.DisplayLayout.DevicePositions[0].MachineName);
        }
    }
}
