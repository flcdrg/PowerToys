// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Linq;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Class;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class MachineStuffSwitchingTests
{
    private static MonitorLayoutInfo Mon(string machine, string id, int x, int y, int w, int h)
        => new MonitorLayoutInfo
        {
            MachineName = machine,
            MonitorId = id,
            X = x,
            Y = y,
            Width = w,
            Height = h,
        };

    private static WinAPI.PhysicalMonitorInfo Phys(string id, int left, int top, int right, int bottom)
        => new()
        {
            DeviceName = id,
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom,
        };

    private static MyRectangle Desktop(int left, int top, int right, int bottom)
        => new()
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom,
        };

    [TestMethod]
    public void MatchMonitorsByPositionalOrder_MatchesByResolutionAndLeftToRight()
    {
        var local = new List<MonitorLayoutInfo>
        {
            Mon("LOCAL", "CFG_A", 0, 0, 2560, 1440),
            Mon("LOCAL", "CFG_B", 2560, 0, 1920, 1080),
            Mon("LOCAL", "CFG_C", 4480, 0, 1920, 1080),
        };

        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("PHYS_MAIN", -2560, 0, 0, 1440),
            Phys("PHYS_LEFT_1080", 0, 0, 1920, 1080),
            Phys("PHYS_RIGHT_1080", 1920, 0, 3840, 1080),
        };

        var map = MonitorLayoutPatcher.MatchMonitorsByPositionalOrder(local, physical);

        Assert.IsNotNull(map);
        Assert.AreEqual("PHYS_MAIN", map["CFG_A"].PhysId);
        Assert.AreEqual("PHYS_LEFT_1080", map["CFG_B"].PhysId);
        Assert.AreEqual("PHYS_RIGHT_1080", map["CFG_C"].PhysId);
    }

    [TestMethod]
    public void MatchMonitorsByPositionalOrder_WhenResolutionCountsMismatch_ReturnsNull()
    {
        var local = new List<MonitorLayoutInfo>
        {
            Mon("LOCAL", "CFG_A", 0, 0, 1920, 1080),
            Mon("LOCAL", "CFG_B", 1920, 0, 1920, 1080),
        };

        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("PHYS_ONLY_ONE", 0, 0, 1920, 1080),
        };

        var map = MonitorLayoutPatcher.MatchMonitorsByPositionalOrder(local, physical);

        Assert.IsNull(map);
    }

    [TestMethod]
    public void PatchLocalMonitorCoordinatesForTesting_WhenDeviceNamesStable_PatchesOnlyLocalMonitors()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("LOCAL", "\\\\.\\DISPLAY1", 100, 200, 1920, 1080),
            Mon("LOCAL", "\\\\.\\DISPLAY2", 2021, 200, 1920, 1080),
            Mon("REMOTE", "REMOTE1", 5000, 1000, 2560, 1440),
        };

        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("\\\\.\\DISPLAY1", 0, 0, 1920, 1080),
            Phys("\\\\.\\DISPLAY2", 1920, 0, 3840, 1080),
        };

        var patched = MachineStuff.PatchLocalMonitorCoordinatesForTesting(
            layout,
            "LOCAL",
            physical,
            Desktop(0, 0, 3840, 1080));

        var local1 = patched.Single(m => m.MachineName == "LOCAL" && m.MonitorId == "\\\\.\\DISPLAY1");
        var local2 = patched.Single(m => m.MachineName == "LOCAL" && m.MonitorId == "\\\\.\\DISPLAY2");
        var remote = patched.Single(m => m.MachineName == "REMOTE");

        Assert.AreEqual(100, local1.X);
        Assert.AreEqual(200, local1.Y);
        Assert.AreEqual(2020, local2.X);
        Assert.AreEqual(200, local2.Y);

        Assert.AreEqual("REMOTE1", remote.MonitorId);
        Assert.AreEqual(5000, remote.X);
        Assert.AreEqual(1000, remote.Y);
    }

    [TestMethod]
    public void PatchLocalMonitorCoordinatesForTesting_WhenDeviceNamesChanged_UsesSizeAndOrderMatchingAndRewritesIds()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("LOCAL", "CONFIG_LEFT", 10, 20, 1920, 1080),
            Mon("LOCAL", "CONFIG_RIGHT", 1931, 20, 1920, 1080),
        };

        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("\\\\.\\DISPLAY7", 0, 0, 1920, 1080),
            Phys("\\\\.\\DISPLAY9", 1920, 0, 3840, 1080),
        };

        var patched = MachineStuff.PatchLocalMonitorCoordinatesForTesting(
            layout,
            "LOCAL",
            physical,
            Desktop(0, 0, 3840, 1080));

        var left = patched.Single(m => m.X == 10);
        var right = patched.Single(m => m.X == 1930);

        Assert.AreEqual("\\\\.\\DISPLAY7", left.MonitorId);
        Assert.AreEqual("\\\\.\\DISPLAY9", right.MonitorId);
    }

    [TestMethod]
    public void PatchLocalMonitorCoordinatesForTesting_WhenSizeOrderMatchFails_ReturnsOriginalLayout()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("LOCAL", "CONFIG_LEFT", 0, 0, 1920, 1080),
            Mon("LOCAL", "CONFIG_RIGHT", 1920, 0, 1920, 1080),
        };

        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("\\\\.\\DISPLAY1", 0, 0, 1920, 1080),
            Phys("\\\\.\\DISPLAY2", 1920, 0, 4480, 1440),
        };

        var patched = MachineStuff.PatchLocalMonitorCoordinatesForTesting(
            layout,
            "LOCAL",
            physical,
            Desktop(0, 0, 4480, 1440));

        Assert.AreSame(layout, patched);
    }

    [TestMethod]
    public void PatchLocalMonitorCoordinatesForTesting_DavidSetup_CorrectlyRemapsSwappedDisplayIds()
    {
        // Saved config has DISPLAY4=3840x2160 and DISPLAY3=2560x1440, but
        // the current physical monitors have DISPLAY3=3840x2160 (primary)
        // and DISPLAY4=2560x1440.  Size+order matching should remap the
        // config names to the correct physical devices.
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("AU001-G2MDZ64", "AU001-G2MDZ64-0", 0, 0, 1920, 1080),
            Mon("Delphinium", "\\\\.\\DISPLAY4", 1902, 9, 3840, 2160),   // config: 3840x2160
            Mon("Delphinium", "\\\\.\\DISPLAY1", 2356, 2169, 3456, 2160),
            Mon("Delphinium", "\\\\.\\DISPLAY2", 5809, 0, 3840, 2160),   // config: 3840x2160
            Mon("Delphinium", "\\\\.\\DISPLAY3", 9600, 606, 2560, 1440), // config: 2560x1440
            Mon("Della", "Della-0", 3231, 4329, 1920, 1080),
        };

        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("\\\\.\\DISPLAY1", -3404, 2169, 52, 4329),    // 3456x2160
            Phys("\\\\.\\DISPLAY2", -3840, 9, 0, 2169),         // 3840x2160 (leftmost)
            Phys("\\\\.\\DISPLAY3", 0, 0, 3840, 2160),          // 3840x2160 (primary, rightmost)
            Phys("\\\\.\\DISPLAY4", 3840, 612, 6400, 2052),     // 2560x1440
        };

        // Desktop spans -3840 to 6400 in x and 0 to 4329 in y.
        var desktopBounds = Desktop(-3840, 0, 6400, 4329);

        var patched = MachineStuff.PatchLocalMonitorCoordinatesForTesting(
            layout, "Delphinium", physical, desktopBounds);

        // Remote machines must be unchanged.
        var au001 = patched.Single(m => m.MachineName == "AU001-G2MDZ64");
        Assert.AreEqual(0, au001.X);
        Assert.AreEqual(0, au001.Y);
        Assert.AreEqual("AU001-G2MDZ64-0", au001.MonitorId);

        var della = patched.Single(m => m.MachineName == "Della");
        Assert.AreEqual(3231, della.X);
        Assert.AreEqual(4329, della.Y);

        // config DISPLAY4 (3840x2160, leftmost in canvas) → physical DISPLAY2
        // (3840x2160, leftmost physical). It is the anchor so its canvas
        // position is preserved exactly.
        var d2 = patched.Single(m => m.MonitorId == "\\\\.\\DISPLAY2" && m.MachineName == "Delphinium");
        Assert.AreEqual(1902, d2.X, "Anchor monitor must keep its configured canvas X");
        Assert.AreEqual(9, d2.Y, "Anchor monitor must keep its configured canvas Y");
        Assert.AreEqual(3840, d2.Width);
        Assert.AreEqual(2160, d2.Height);

        // config DISPLAY1 (3456x2160, unique size) → physical DISPLAY1.
        // Offset: physical (436, 2169) + offsetX=1902 = (2338, 2169).
        var d1 = patched.Single(m => m.MonitorId == "\\\\.\\DISPLAY1" && m.MachineName == "Delphinium");
        Assert.AreEqual(2338, d1.X);
        Assert.AreEqual(2169, d1.Y);
        Assert.AreEqual(3456, d1.Width);
        Assert.AreEqual(2160, d1.Height);

        // config DISPLAY2 (3840x2160, rightmost in canvas) → physical DISPLAY3
        // (3840x2160, rightmost physical). Physical (0,0) + offset (1902,0) = (5742, 0) → wait,
        // offsetX=1902, physical.Left=0, desktopBounds.Left=-3840,
        // x = (0-(-3840)) + 1902... Hmm wait, let me recalculate:
        // offsetX = anchorLayout.X - (anchorPhys.Left - desktopBounds.Left)
        //         = 1902 - (-3840 - (-3840)) = 1902 - 0 = 1902
        // patched.X = (phys.Left - desktopBounds.Left) + offsetX
        // For DISPLAY3: (0 - (-3840)) + 1902 = 3840 + 1902 = 5742
        var d3 = patched.Single(m => m.MonitorId == "\\\\.\\DISPLAY3" && m.MachineName == "Delphinium");
        Assert.AreEqual(5742, d3.X);
        Assert.AreEqual(0, d3.Y);
        Assert.AreEqual(3840, d3.Width);
        Assert.AreEqual(2160, d3.Height);

        // config DISPLAY3 (2560x1440) → physical DISPLAY4 (2560x1440).
        // x = (3840 - (-3840)) + 1902 = 7680 + 1902 = 9582
        // y = (612 - 0) + 0 = 612
        var d4 = patched.Single(m => m.MonitorId == "\\\\.\\DISPLAY4" && m.MachineName == "Delphinium");
        Assert.AreEqual(9582, d4.X);
        Assert.AreEqual(612, d4.Y);
        Assert.AreEqual(2560, d4.Width);
        Assert.AreEqual(1440, d4.Height);
    }

    [TestMethod]
    public void PatchLocalMonitorCoordinatesForTesting_EmptyLayout_ReturnsOriginal()
    {
        var layout = new List<MonitorLayoutInfo>();
        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("\\\\.\\DISPLAY1", 0, 0, 1920, 1080),
        };

        var result = MachineStuff.PatchLocalMonitorCoordinatesForTesting(
            layout, "LOCAL", physical, Desktop(0, 0, 1920, 1080));

        Assert.AreSame(layout, result);
    }

    [TestMethod]
    public void PatchLocalMonitorCoordinatesForTesting_EmptyPhysicalMonitors_ReturnsOriginal()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("LOCAL", "\\\\.\\DISPLAY1", 0, 0, 1920, 1080),
        };

        var result = MachineStuff.PatchLocalMonitorCoordinatesForTesting(
            layout, "LOCAL", new List<WinAPI.PhysicalMonitorInfo>(), Desktop(0, 0, 1920, 1080));

        Assert.AreSame(layout, result);
    }

    [TestMethod]
    public void PatchLocalMonitorCoordinatesForTesting_NoLocalMonitorsInLayout_ReturnsOriginal()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("REMOTE1", "R1", 0, 0, 1920, 1080),
            Mon("REMOTE2", "R2", 1920, 0, 1920, 1080),
        };
        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("\\\\.\\DISPLAY1", 0, 0, 1920, 1080),
        };

        var result = MachineStuff.PatchLocalMonitorCoordinatesForTesting(
            layout, "LOCAL", physical, Desktop(0, 0, 1920, 1080));

        Assert.AreSame(layout, result);
    }

    [TestMethod]
    public void PatchLocalMonitorCoordinatesForTesting_PreservesIsPrimary()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            new MonitorLayoutInfo
            {
                MachineName = "LOCAL",
                MonitorId = "\\\\.\\DISPLAY1",
                X = 0, Y = 0, Width = 1920, Height = 1080,
                IsPrimary = true,
            },
            new MonitorLayoutInfo
            {
                MachineName = "LOCAL",
                MonitorId = "\\\\.\\DISPLAY2",
                X = 1921, Y = 0, Width = 1920, Height = 1080,
                IsPrimary = false,
            },
        };

        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("\\\\.\\DISPLAY1", 0, 0, 1920, 1080),
            Phys("\\\\.\\DISPLAY2", 1920, 0, 3840, 1080),
        };

        var patched = MachineStuff.PatchLocalMonitorCoordinatesForTesting(
            layout, "LOCAL", physical, Desktop(0, 0, 3840, 1080));

        Assert.IsTrue(patched.Single(m => m.MonitorId == "\\\\.\\DISPLAY1").IsPrimary);
        Assert.IsFalse(patched.Single(m => m.MonitorId == "\\\\.\\DISPLAY2").IsPrimary);
    }

    [TestMethod]
    public void MatchMonitorsByPositionalOrder_DavidSwapCase_MapsConfigNamesToPhysicalNames()
    {
        // The layout saved DISPLAY4 as 3840x2160 and DISPLAY3 as 2560x1440, but
        // Windows has since swapped the device IDs. MatchMonitorsByPositionalOrder
        // must re-match by canvas left-to-right order within each resolution group.
        var local = new List<MonitorLayoutInfo>
        {
            Mon("Delphinium", "\\\\.\\DISPLAY4", 1902, 9, 3840, 2160),    // canvas-left 3840x2160
            Mon("Delphinium", "\\\\.\\DISPLAY1", 2356, 2169, 3456, 2160), // only 3456x2160
            Mon("Delphinium", "\\\\.\\DISPLAY2", 5809, 0, 3840, 2160),    // canvas-right 3840x2160
            Mon("Delphinium", "\\\\.\\DISPLAY3", 9600, 606, 2560, 1440),  // only 2560x1440
        };

        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("\\\\.\\DISPLAY2", -3840, 9, 0, 2169),    // physical-left 3840x2160
            Phys("\\\\.\\DISPLAY3", 0, 0, 3840, 2160),     // physical-right 3840x2160
            Phys("\\\\.\\DISPLAY1", -3404, 2169, 52, 4329), // unique 3456x2160
            Phys("\\\\.\\DISPLAY4", 3840, 612, 6400, 2052), // unique 2560x1440
        };

        var map = MonitorLayoutPatcher.MatchMonitorsByPositionalOrder(local, physical);

        Assert.IsNotNull(map);
        Assert.AreEqual("\\\\.\\DISPLAY2", map["\\\\.\\DISPLAY4"].PhysId, "canvas-left 3840x2160 maps to physical-left 3840x2160");
        Assert.AreEqual("\\\\.\\DISPLAY3", map["\\\\.\\DISPLAY2"].PhysId, "canvas-right 3840x2160 maps to physical-right 3840x2160");
        Assert.AreEqual("\\\\.\\DISPLAY1", map["\\\\.\\DISPLAY1"].PhysId, "unique 3456x2160 maps to itself");
        Assert.AreEqual("\\\\.\\DISPLAY4", map["\\\\.\\DISPLAY3"].PhysId, "unique 2560x1440 maps to physical DISPLAY4");
    }

    [TestMethod]
    public void MatchMonitorsByPositionalOrder_SingleMonitor_ReturnsSingleEntry()
    {
        var local = new List<MonitorLayoutInfo>
        {
            Mon("LOCAL", "CFG_ONLY", 0, 0, 1920, 1080),
        };
        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("PHYS_ONLY", 0, 0, 1920, 1080),
        };

        var map = MonitorLayoutPatcher.MatchMonitorsByPositionalOrder(local, physical);

        Assert.IsNotNull(map);
        Assert.AreEqual(1, map.Count);
        Assert.AreEqual("PHYS_ONLY", map["CFG_ONLY"].PhysId);
    }

    [TestMethod]
    public void MatchMonitorsByPositionalOrder_Empty_ReturnsEmptyMap()
    {
        var map = MonitorLayoutPatcher.MatchMonitorsByPositionalOrder(
            new List<MonitorLayoutInfo>(),
            new List<WinAPI.PhysicalMonitorInfo>());

        Assert.IsNotNull(map);
        Assert.AreEqual(0, map.Count);
    }

    [TestMethod]
    public void ClampToSafeMonitorCoord_CoordInsideSafeRange_Unchanged()
    {
        // x=100 is well inside [-3403, 50] for DISPLAY1 (Left=-3404, Right=52)
        int result = NavigationMath.ClampToSafeMonitorCoord(100, -100, 200);
        Assert.AreEqual(100, result);
    }

    [TestMethod]
    public void ClampToSafeMonitorCoord_Display3ToDisplay1_CursorAtX967_ClampedTo50()
    {
        // Regression: DISPLAY3 cursor x=967 moving DOWN into DISPLAY1
        // (Left=-3404, Right=52). Before this fix the clamp produced 51,
        // the right-edge trigger pixel (Right-SKIP_PIXELS = 52-1 = 51),
        // immediately re-firing MoveRight and trapping the cursor.
        // After the fix the result must be <= Right-SKIP_PIXELS-1 = 50.
        int result = NavigationMath.ClampToSafeMonitorCoord(967, -3404, 52);
        Assert.AreEqual(50, result, "x must land at most at Right-SKIP_PIXELS-1 so it cannot trigger the right edge");
    }

    [TestMethod]
    public void ClampToSafeMonitorCoord_ResultNeverEqualsRightEdgeTrigger()
    {
        // Right edge fires when x >= maxBound - SKIP_PIXELS.
        // SKIP_PIXELS = 1, so threshold is maxBound - 1.
        // The clamped result must be < maxBound - 1, i.e. <= maxBound - 2.
        const int skipPixels = 1;
        int maxBound = 100;
        int result = NavigationMath.ClampToSafeMonitorCoord(999, 0, maxBound);
        Assert.IsTrue(result < maxBound - skipPixels, $"result {result} must be < right-trigger threshold {maxBound - skipPixels}");
    }

    [TestMethod]
    public void ClampToSafeMonitorCoord_ResultNeverEqualsLeftEdgeTrigger()
    {
        // Left edge fires when x < minBound + SKIP_PIXELS.
        // SKIP_PIXELS = 1, so threshold is minBound + 1.
        // The clamped result must be >= minBound + 1.
        const int skipPixels = 1;
        int minBound = 0;
        int result = NavigationMath.ClampToSafeMonitorCoord(-999, minBound, 200);
        Assert.IsTrue(result >= minBound + skipPixels, $"result {result} must be >= left-trigger threshold {minBound + skipPixels}");
    }

    [TestMethod]
    public void ClampToSafeMonitorCoord_CoordBelowMinBound_ClampedToMinBoundPlusSkip()
    {
        int result = NavigationMath.ClampToSafeMonitorCoord(-5000, -3404, 52);
        Assert.AreEqual(-3403, result); // minBound + SKIP_PIXELS = -3404 + 1
    }

    [TestMethod]
    public void ClampToSafeMonitorCoord_CoordAtExactRightTrigger_ClampedOnePixelInside()
    {
        // Coord = maxBound - SKIP_PIXELS = 51 (the trigger threshold itself)
        // must be clamped to 50.
        int result = NavigationMath.ClampToSafeMonitorCoord(51, -3404, 52);
        Assert.AreEqual(50, result);
    }

    [TestMethod]
    public void ClampToSafeMonitorCoord_StandardFullHdMonitor_NominalCursorIsUnchanged()
    {
        // Cursor at x=960 well inside a 1920-wide monitor starting at 0.
        int result = NavigationMath.ClampToSafeMonitorCoord(960, 0, 1920);
        Assert.AreEqual(960, result);
    }

    [TestMethod]
    public void MatchMonitorsByPositionalOrder_ThreeResolutionGroups_AllGroupsMatchCorrectly()
    {
        var local = new List<MonitorLayoutInfo>
        {
            Mon("LOCAL", "CFG_4K_L", 0, 0, 3840, 2160),
            Mon("LOCAL", "CFG_4K_R", 3840, 0, 3840, 2160),
            Mon("LOCAL", "CFG_1440_ONLY", 7680, 0, 2560, 1440),
            Mon("LOCAL", "CFG_1080_L", 0, 2160, 1920, 1080),
            Mon("LOCAL", "CFG_1080_R", 1920, 2160, 1920, 1080),
        };
        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys("PHYS_4K_A", -3840, 0, 0, 2160),
            Phys("PHYS_4K_B", 0, 0, 3840, 2160),
            Phys("PHYS_1440", 3840, 0, 6400, 1440),
            Phys("PHYS_1080_X", -1920, 2160, 0, 3240),
            Phys("PHYS_1080_Y", 0, 2160, 1920, 3240),
        };

        var map = MonitorLayoutPatcher.MatchMonitorsByPositionalOrder(local, physical);

        Assert.IsNotNull(map);
        Assert.AreEqual("PHYS_4K_A", map["CFG_4K_L"].PhysId);
        Assert.AreEqual("PHYS_4K_B", map["CFG_4K_R"].PhysId);
        Assert.AreEqual("PHYS_1440", map["CFG_1440_ONLY"].PhysId);
        Assert.AreEqual("PHYS_1080_X", map["CFG_1080_L"].PhysId);
        Assert.AreEqual("PHYS_1080_Y", map["CFG_1080_R"].PhysId);
    }

    /// <summary>
    /// Reproduces the Delphinium→DELLA navigation bug where DISPLAY4 patching
    /// shifts it from Y=612 to Y=699 (physical monitor moved), breaking the
    /// edge alignment with remote DELLA at Y=2052.
    /// After patching, DELLA.Y must shift by the same delta (+87) so that
    /// DISPLAY4.bottom == DELLA.top.
    /// </summary>
    [TestMethod]
    public void Patch_RemoteMonitorAdjustedWhenLocalMonitorShifts()
    {
        // Stored layout: DISPLAY4 bottom (612+1440=2052) aligns with DELLA top (2052).
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("Delphinium", @"\\.\DISPLAY2", 0, 9, 3840, 2160),
            Mon("Delphinium", @"\\.\DISPLAY1", 436, 2169, 3456, 2160),
            Mon("Delphinium", @"\\.\DISPLAY3", 3840, 0, 3840, 2160),
            Mon("Delphinium", @"\\.\DISPLAY4", 7680, 612, 2560, 1440),
            Mon("DELLA", "DELLA-0", 7680, 2052, 1920, 1080),
        };

        // Physical monitors on Delphinium: DISPLAY4 shifted to Y=699.
        var physical = new List<WinAPI.PhysicalMonitorInfo>
        {
            Phys(@"\\.\DISPLAY2", -3840, 9, 0, 2169),
            Phys(@"\\.\DISPLAY1", -3473, 2169, -17, 4329),
            Phys(@"\\.\DISPLAY3", 0, 0, 3840, 2160),
            Phys(@"\\.\DISPLAY4", 3840, 699, 6400, 2139),
        };

        var desktop = Desktop(-3840, 0, 6400, 4329);

        var patched = MachineStuff.PatchLocalMonitorCoordinatesForTesting(layout, "Delphinium", physical, desktop);

        // DISPLAY4 should now be at Y=699 (physical).
        var display4 = patched.First(m => m.MonitorId == @"\\.\DISPLAY4");
        Assert.AreEqual(699, display4.Y, "DISPLAY4 Y should be patched to physical Y");
        Assert.AreEqual(2139, display4.Y + display4.Height, "DISPLAY4 bottom");

        // DELLA (remote) must shift by the same delta so its top aligns with DISPLAY4 bottom.
        var della = patched.First(m => m.MachineName == "DELLA");
        Assert.AreEqual(2139, della.Y, "DELLA.Y must shift from 2052 to 2139 to match DISPLAY4 bottom");
    }

    [TestMethod]
    public void UpdateRemoteMachineLayoutsInSettings_EmptyStoredLayout_AcceptsFullLayout()
    {
        var originalSettings = Setting.Values;
        string originalMachineName = Common.MachineName;

        try
        {
            Common.MachineName = "DELLA";
            Setting.Values = new MouseWithoutBorders.Class.Settings
            {
                MonitorLayout = new List<MonitorLayoutInfo>(),
            };

            var receivedLayout = new List<MonitorLayoutInfo>
            {
                Mon("Delphinium", @"\\.\DISPLAY3", 0, 0, 3840, 2160),
                Mon("Delphinium", @"\\.\DISPLAY4", 3840, 701, 2560, 1440),
                Mon("DELLA", "DELLA-0", 0, 2160, 3840, 2160),
            };

            MachineStuff.UpdateRemoteMachineLayoutsInSettings(receivedLayout);

            var updated = Setting.Values.MonitorLayout;
            Assert.IsNotNull(updated, "Layout should not be null after accepting authoritative layout");
            Assert.AreEqual(3, updated.Count, "All 3 monitors from received layout should be saved");
            CollectionAssert.AreEqual(
                receivedLayout.Select(m => $"{m.MachineName}|{m.MonitorId}|{m.X}|{m.Y}|{m.Width}|{m.Height}").ToList(),
                updated.Select(m => $"{m.MachineName}|{m.MonitorId}|{m.X}|{m.Y}|{m.Width}|{m.Height}").ToList(),
                "Empty stored layout should be populated from the authoritative peer layout.");
        }
        finally
        {
            Setting.Values = originalSettings;
            Common.MachineName = originalMachineName;
        }
    }

    [TestMethod]
    public void UpdateRemoteMachineLayoutsInSettings_FullLayoutUsesAuthoritativeCoordinates()
    {
        var originalSettings = Setting.Values;
        string originalMachineName = Common.MachineName;

        try
        {
            Common.MachineName = "DELLA";
            Setting.Values = new MouseWithoutBorders.Class.Settings
            {
                MonitorLayout = new List<MonitorLayoutInfo>
                {
                    Mon("DELLA", "DELLA-0", 3840, 2158, 3840, 2160),
                    Mon("Delphinium", @"\\.\DISPLAY3", 3840, 0, 3840, 2160),
                    Mon("Delphinium", @"\\.\DISPLAY4", 7680, 701, 2560, 1440),
                    Mon("AU001", "AU001-0", -2601, 400, 1920, 1080),
                },
            };

            var receivedLayout = new List<MonitorLayoutInfo>
            {
                Mon("AU001", "AU001-0", 0, 0, 1920, 1080),
                Mon("Delphinium", @"\\.\DISPLAY3", 5760, 0, 3840, 2160),
                Mon("Delphinium", @"\\.\DISPLAY4", 9600, 701, 2560, 1440),
                Mon("DELLA", "DELLA-0", 9824, 2141, 3840, 2160),
            };

            MachineStuff.UpdateRemoteMachineLayoutsInSettings(receivedLayout);

            var updated = Setting.Values.MonitorLayout;
            CollectionAssert.AreEqual(
                receivedLayout.Select(m => $"{m.MachineName}|{m.MonitorId}|{m.X}|{m.Y}|{m.Width}|{m.Height}").ToList(),
                updated.Select(m => $"{m.MachineName}|{m.MonitorId}|{m.X}|{m.Y}|{m.Width}|{m.Height}").ToList(),
                "The received full layout should replace stale local coordinates so every machine renders the same canvas.");
        }
        finally
        {
            Setting.Values = originalSettings;
            Common.MachineName = originalMachineName;
        }
    }
}
