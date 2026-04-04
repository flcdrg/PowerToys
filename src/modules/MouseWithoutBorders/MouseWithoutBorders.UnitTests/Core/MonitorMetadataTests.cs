// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class MonitorMetadataTests
{
    [TestMethod]
    public void GetLocalMonitors_ReturnsNonEmptyList()
    {
        List<MonitorLayoutInfo> monitors = Common.GetLocalMonitors("TESTPC");

        Assert.IsNotNull(monitors, "GetLocalMonitors should not return null.");
        Assert.IsTrue(monitors.Count > 0, "GetLocalMonitors should return at least one monitor.");
    }

    [TestMethod]
    public void GetLocalMonitors_FieldsAreValid()
    {
        const string machineName = "TESTPC";
        List<MonitorLayoutInfo> monitors = Common.GetLocalMonitors(machineName);

        Assert.IsTrue(monitors.Count > 0, "Expected at least one monitor.");

        foreach (MonitorLayoutInfo monitor in monitors)
        {
            Assert.AreEqual(machineName, monitor.MachineName, "MachineName should match the supplied parameter.");
            Assert.IsFalse(string.IsNullOrEmpty(monitor.MonitorId), "MonitorId should not be null or empty.");
            Assert.IsTrue(monitor.Width > 0, "Width should be positive.");
            Assert.IsTrue(monitor.Height > 0, "Height should be positive.");
        }
    }

    [TestMethod]
    public void GetLocalMonitors_ExactlyOnePrimaryMonitor()
    {
        List<MonitorLayoutInfo> monitors = Common.GetLocalMonitors("TESTPC");
        int primaryCount = 0;
        foreach (MonitorLayoutInfo m in monitors)
        {
            if (m.IsPrimary)
            {
                primaryCount++;
            }
        }

        Assert.AreEqual(1, primaryCount, "Exactly one monitor should be flagged as primary.");
    }

    [TestMethod]
    public void UpdateAndGetMonitorMetadata_RoundTrip()
    {
        MachinePool pool = new();
        const string machineName = "PC-ALPHA";

        List<MonitorLayoutInfo> monitors = new()
        {
            new MonitorLayoutInfo
            {
                MachineName = machineName,
                MonitorId = @"\\.\DISPLAY1",
                X = 0,
                Y = 0,
                Width = 1920,
                Height = 1080,
                IsPrimary = true,
            },
            new MonitorLayoutInfo
            {
                MachineName = machineName,
                MonitorId = @"\\.\DISPLAY2",
                X = 1920,
                Y = 0,
                Width = 2560,
                Height = 1440,
                IsPrimary = false,
            },
        };

        pool.UpdateMonitorMetadata(machineName, monitors);
        List<MonitorLayoutInfo>? retrieved = pool.GetMonitorMetadata(machineName);

        Assert.IsNotNull(retrieved, "GetMonitorMetadata should return the stored list.");
        Assert.AreEqual(2, retrieved.Count, "Expected 2 monitors to be stored.");
        Assert.AreEqual(@"\\.\DISPLAY1", retrieved[0].MonitorId);
        Assert.AreEqual(1920, retrieved[0].Width);
        Assert.IsTrue(retrieved[0].IsPrimary);
        Assert.AreEqual(@"\\.\DISPLAY2", retrieved[1].MonitorId);
        Assert.AreEqual(2560, retrieved[1].Width);
        Assert.IsFalse(retrieved[1].IsPrimary);
    }

    [TestMethod]
    public void UpdateMonitorMetadata_OverwritesPreviousData()
    {
        MachinePool pool = new();
        const string machineName = "PC-BETA";

        List<MonitorLayoutInfo> first = new()
        {
            new MonitorLayoutInfo { MachineName = machineName, MonitorId = @"\\.\DISPLAY1", Width = 1280, Height = 720, IsPrimary = true },
        };

        List<MonitorLayoutInfo> second = new()
        {
            new MonitorLayoutInfo { MachineName = machineName, MonitorId = @"\\.\DISPLAY1", Width = 3840, Height = 2160, IsPrimary = true },
            new MonitorLayoutInfo { MachineName = machineName, MonitorId = @"\\.\DISPLAY2", Width = 1920, Height = 1080, IsPrimary = false },
        };

        pool.UpdateMonitorMetadata(machineName, first);
        pool.UpdateMonitorMetadata(machineName, second);

        List<MonitorLayoutInfo>? retrieved = pool.GetMonitorMetadata(machineName);
        Assert.IsNotNull(retrieved);
        Assert.AreEqual(2, retrieved.Count, "Second update should replace the first.");
        Assert.AreEqual(3840, retrieved[0].Width);
    }

    [TestMethod]
    public void GetMonitorMetadata_ReturnsNullForUnknownMachine()
    {
        MachinePool pool = new();

        List<MonitorLayoutInfo>? result = pool.GetMonitorMetadata("UNKNOWN-MACHINE");

        Assert.IsNull(result, "GetMonitorMetadata should return null for a machine that was never stored.");
    }

    [TestMethod]
    public void GetMonitorMetadata_IsCaseInsensitive()
    {
        MachinePool pool = new();
        List<MonitorLayoutInfo> monitors = new()
        {
            new MonitorLayoutInfo { MachineName = "PC-GAMMA", MonitorId = @"\\.\DISPLAY1", Width = 1920, Height = 1080, IsPrimary = true },
        };

        pool.UpdateMonitorMetadata("PC-GAMMA", monitors);

        Assert.IsNotNull(pool.GetMonitorMetadata("pc-gamma"), "Lookup should be case-insensitive (lower).");
        Assert.IsNotNull(pool.GetMonitorMetadata("Pc-Gamma"), "Lookup should be case-insensitive (mixed).");
    }
}
