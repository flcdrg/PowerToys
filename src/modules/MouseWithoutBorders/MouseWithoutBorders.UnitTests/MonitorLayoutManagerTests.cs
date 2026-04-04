// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Class;

namespace MouseWithoutBorders.UnitTests;

[TestClass]
public sealed class MonitorLayoutManagerTests
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

    private static MonitorLayoutManager BuildManager(List<MonitorLayoutInfo> layout)
    {
        var mgr = new MonitorLayoutManager();
        mgr.RebuildAdjacency(layout);
        return mgr;
    }

    [TestMethod]
    public void EmptyLayout_AllTransitions_ReturnNotResolved()
    {
        var mgr = BuildManager([]);

        var r = mgr.ResolveEdgeTransition("PC1", 100, 100, MoveDirection.Right);

        Assert.IsFalse(r.IsResolved);
    }

    [TestMethod]
    public void NullLayout_AllTransitions_ReturnNotResolved()
    {
        var mgr = new MonitorLayoutManager();
        mgr.RebuildAdjacency(null);

        var r = mgr.ResolveEdgeTransition("PC1", 100, 100, MoveDirection.Down);

        Assert.IsFalse(r.IsResolved);
    }

    [TestMethod]
    public void HorizontalPair_CursorOnRightEdgeOfLeft_ResolvesToRightMonitor()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 1920, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("PC2", r.TargetMachine);
        Assert.AreEqual("M2", r.TargetMonitorId);
    }

    [TestMethod]
    public void HorizontalPair_CursorOnLeftEdgeOfRight_ResolvesToLeftMonitor()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 1920, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC2", 1920, 540, MoveDirection.Left);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("PC1", r.TargetMachine);
        Assert.AreEqual("M1", r.TargetMonitorId);
    }

    [TestMethod]
    public void HorizontalPair_CursorOnRightEdge_MovingDown_NotResolved()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 1920, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Down);

        Assert.IsFalse(r.IsResolved);
    }

    [TestMethod]
    public void VerticalPair_CursorOnBottomEdgeOfTop_ResolvesToBottomMonitor()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 0, 1080, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 960, 1079, MoveDirection.Down);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("PC2", r.TargetMachine);
        Assert.AreEqual("M2", r.TargetMonitorId);
    }

    [TestMethod]
    public void VerticalPair_CursorOnTopEdgeOfBottom_ResolvesToTopMonitor()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 0, 1080, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC2", 960, 1080, MoveDirection.Up);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("PC1", r.TargetMachine);
        Assert.AreEqual("M1", r.TargetMonitorId);
    }

    [TestMethod]
    public void LShape_AToB_Right_Resolves()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "A", 0, 0, 1920, 1080),
            Mon("PC2", "B", 1920, 0, 1920, 1080),
            Mon("PC3", "C", 1920, 1080, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("PC2", r.TargetMachine);
        Assert.AreEqual("B", r.TargetMonitorId);
    }

    [TestMethod]
    public void LShape_BToC_Down_Resolves()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "A", 0, 0, 1920, 1080),
            Mon("PC2", "B", 1920, 0, 1920, 1080),
            Mon("PC3", "C", 1920, 1080, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC2", 2880, 1079, MoveDirection.Down);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("PC3", r.TargetMachine);
        Assert.AreEqual("C", r.TargetMonitorId);
    }

    [TestMethod]
    public void LShape_ADown_NonAdjacent_NotResolved()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "A", 0, 0, 1920, 1080),
            Mon("PC2", "B", 1920, 0, 1920, 1080),
            Mon("PC3", "C", 1920, 1080, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 960, 1079, MoveDirection.Down);

        Assert.IsFalse(r.IsResolved);
    }

    [TestMethod]
    public void Gap_BeyondTolerance_BetweenMonitors_NotAdjacent()
    {
        // Gap of 300px between different-machine monitors is well beyond AdjacencyTolerance (128).
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 2220, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right);

        Assert.IsFalse(r.IsResolved);
    }

    [TestMethod]
    public void Gap_WithinTolerance_IsAdjacent()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 1921, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("PC2", r.TargetMachine);
    }

    [TestMethod]
    public void Overlap_BothDimensionsBeyondThreshold_NotAdjacent()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 1840, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 1899, 540, MoveDirection.Right);

        Assert.IsFalse(r.IsResolved);
    }

    [TestMethod]
    public void Overlap_BothDimensionsBeyondThreshold_VerticalCase_NotAdjacent()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 0, 1000, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 960, 1059, MoveDirection.Down);

        Assert.IsFalse(r.IsResolved);
    }

    [TestMethod]
    public void PeerMissing_CursorOnKnownMachine_RightEdge_NotResolved()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right);

        Assert.IsFalse(r.IsResolved);
    }

    [TestMethod]
    public void PeerMissing_CursorOnUnknownMachine_NotResolved()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC2", 100, 100, MoveDirection.Right);

        Assert.IsFalse(r.IsResolved);
    }

    [TestMethod]
    public void RebuildThenResolve_ReturnsFreshAdjacency()
    {
        var mgr = new MonitorLayoutManager();

        mgr.RebuildAdjacency(new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
        });
        Assert.IsFalse(mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right).IsResolved);

        mgr.RebuildAdjacency(new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 1920, 0, 1920, 1080),
        });
        var r = mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("PC2", r.TargetMachine);

        mgr.RebuildAdjacency(null);
        Assert.IsFalse(mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right).IsResolved);
    }

    [TestMethod]
    public void MultiMonitorLocalMachine_ByMonitorId_TransitionsThroughLocalThenRemote()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC1", "M2", 1920, 0, 1920, 1080),
            Mon("PC2", "M3", 3840, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var m1ToRight = mgr.ResolveEdgeTransitionByMonitorId("PC1", "M1", MoveDirection.Right);
        var m2ToRight = mgr.ResolveEdgeTransitionByMonitorId("PC1", "M2", MoveDirection.Right);

        Assert.IsTrue(m1ToRight.IsResolved);
        Assert.AreEqual("PC1", m1ToRight.TargetMachine);
        Assert.AreEqual("M2", m1ToRight.TargetMonitorId);

        Assert.IsTrue(m2ToRight.IsResolved);
        Assert.AreEqual("PC2", m2ToRight.TargetMachine);
        Assert.AreEqual("M3", m2ToRight.TargetMonitorId);
    }

    [TestMethod]
    public void SmallOverlapInOneDimension_IsStillAdjacent()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 1860, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("PC2", r.TargetMachine);
        Assert.AreEqual("M2", r.TargetMonitorId);
    }

    [TestMethod]
    public void ResolveByMonitorId_UnknownMonitor_NotResolved()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 0, 0, 1920, 1080),
            Mon("PC2", "M2", 1920, 0, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransitionByMonitorId("PC1", "DOES_NOT_EXIST", MoveDirection.Right);

        Assert.IsFalse(r.IsResolved);
    }

    // -----------------------------------------------------------------------
    // Real-world regression tests based on David's four-monitor Delphinium
    // setup (log 2026-04-06). Physical layout after PatchLocalMonitorCoordinates:
    //
    //   AU001(0,0,1920,1080) | DISPLAY2(1902,9,3840,2160) | DISPLAY3(5742,0,3840,2160) | DISPLAY4(9582,612,2560,1440)
    //                                   DISPLAY1(2338,2169,3456,2160)
    //                                         Della(3231,4329,1920,1080)
    // -----------------------------------------------------------------------
    private static List<MonitorLayoutInfo> BuildDavidPatchedLayout() =>
    [
        Mon("AU001-G2MDZ64", "AU001-G2MDZ64-0", 0, 0, 1920, 1080),
        Mon("Delphinium", "\\\\.\\DISPLAY2", 1902, 9, 3840, 2160),
        Mon("Delphinium", "\\\\.\\DISPLAY1", 2338, 2169, 3456, 2160),
        Mon("Delphinium", "\\\\.\\DISPLAY3", 5742, 0, 3840, 2160),
        Mon("Delphinium", "\\\\.\\DISPLAY4", 9582, 612, 2560, 1440),
        Mon("Della", "Della-0", 3231, 4329, 1920, 1080),
    ];

    [TestMethod]
    public void DavidSetup_AU001_RightNeighbour_IsDisplay2()
    {
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("AU001-G2MDZ64", "AU001-G2MDZ64-0", MoveDirection.Right);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("Delphinium", r.TargetMachine);
        Assert.AreEqual("\\\\.\\DISPLAY2", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Display2_LeftNeighbour_IsAU001()
    {
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY2", MoveDirection.Left);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("AU001-G2MDZ64", r.TargetMachine);
        Assert.AreEqual("AU001-G2MDZ64-0", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Display2_RightNeighbour_IsDisplay3()
    {
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY2", MoveDirection.Right);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("Delphinium", r.TargetMachine);
        Assert.AreEqual("\\\\.\\DISPLAY3", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Display2_DownNeighbour_IsDisplay1()
    {
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY2", MoveDirection.Down);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("\\\\.\\DISPLAY1", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Display3_LeftNeighbour_IsDisplay2()
    {
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY3", MoveDirection.Left);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("\\\\.\\DISPLAY2", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Display3_RightNeighbour_IsDisplay4()
    {
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY3", MoveDirection.Right);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("\\\\.\\DISPLAY4", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Display3_DownNeighbour_IsDisplay1()
    {
        // DISPLAY3(5742,0,3840,2160) bottom=2160, DISPLAY1(2338,2169,3456,2160) top=2169
        // 9px vertical gap — within the 64px AdjacencyTolerance.
        // Shared horizontal strip: DISPLAY3.x=[5742,9582) ∩ DISPLAY1.x=[2338,5794) = [5742,5794) = 52px.
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY3", MoveDirection.Down);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("\\\\.\\DISPLAY1", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Display4_LeftNeighbour_IsDisplay3()
    {
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY4", MoveDirection.Left);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("\\\\.\\DISPLAY3", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Display1_DownNeighbour_IsDella()
    {
        // Verifies the cross-machine transition that was broken when patching used wrong anchor.
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY1", MoveDirection.Down);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("Della", r.TargetMachine);
        Assert.AreEqual("Della-0", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Della_UpNeighbour_IsDisplay1()
    {
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Della", "Della-0", MoveDirection.Up);
        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("Delphinium", r.TargetMachine);
        Assert.AreEqual("\\\\.\\DISPLAY1", r.TargetMonitorId);
    }

    [TestMethod]
    public void DavidSetup_Display1_HasNoLeftOrRightNeighbour()
    {
        // DISPLAY1 is at the bottom of the canvas with no horizontal neighbours.
        var mgr = BuildManager(BuildDavidPatchedLayout());
        Assert.IsFalse(mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY1", MoveDirection.Left).IsResolved);
        Assert.IsFalse(mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY1", MoveDirection.Right).IsResolved);
    }

    [TestMethod]
    public void NinePixelVerticalGap_WithinTolerance_IsAdjacent()
    {
        // Replicates the DISPLAY3/DISPLAY1 boundary: 9px gap between bottom of
        // upper monitor and top of lower monitor.
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "TOP", 0, 0, 1920, 1080),
            Mon("PC2", "BOT", 0, 1089, 1920, 1080),
        };
        var mgr = BuildManager(layout);

        var down = mgr.ResolveEdgeTransition("PC1", 960, 1079, MoveDirection.Down);
        var up = mgr.ResolveEdgeTransition("PC2", 960, 1089, MoveDirection.Up);

        Assert.IsTrue(down.IsResolved, "9px gap should be within AdjacencyTolerance");
        Assert.AreEqual("PC2", down.TargetMachine);
        Assert.IsTrue(up.IsResolved);
        Assert.AreEqual("PC1", up.TargetMachine);
    }

    [TestMethod]
    public void ByMonitorId_Display3_Down_CursorInSharedStrip_Resolves()
    {
        // DISPLAY3 physical x=[0,3840), DISPLAY1 physical x=[-3404,52). Shared: [0,52).
        // Cursor at physical x=26 → canvas x=5742+26=5768 ∈ [5742,5794) → resolves.
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY3", MoveDirection.Down, 26, 2159, 0, 0);
        Assert.IsTrue(r.IsResolved, "Cursor inside shared strip should resolve");
        Assert.AreEqual("\\\\.\\DISPLAY1", r.TargetMonitorId);
    }

    [TestMethod]
    public void ByMonitorId_Display3_Down_CursorOutsideSharedStrip_NotResolved()
    {
        // Regression: at x=1876 → canvas x=5742+1876=7618 ∉ [5742,5794) → must not fire.
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY3", MoveDirection.Down, 1876, 2159, 0, 0);
        Assert.IsFalse(r.IsResolved, "Cursor outside shared strip must not trigger the transition");
    }

    [TestMethod]
    public void ByMonitorId_Display3_Down_CursorAtLeftEdgeOfStrip_Resolves()
    {
        // x=0 → canvas x=5742 == sharedXMin → in [5742,5794) → resolves.
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY3", MoveDirection.Down, 0, 2159, 0, 0);
        Assert.IsTrue(r.IsResolved, "Cursor at left edge of shared strip should resolve");
    }

    [TestMethod]
    public void ByMonitorId_Display3_Down_CursorAtRightEdgeOfStrip_NotResolved()
    {
        // x=52 → canvas x=5742+52=5794 == sharedXMax (exclusive) → not resolved.
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY3", MoveDirection.Down, 52, 2159, 0, 0);
        Assert.IsFalse(r.IsResolved, "Cursor one pixel past the shared strip must not resolve");
    }

    [TestMethod]
    public void ByMonitorId_Display1_Up_CursorInDisplay2Strip_Resolves()
    {
        // With same-machine closest-wins adjacency, DISPLAY1.Up=DISPLAY2 (0px gap, 3404px shared strip)
        // rather than DISPLAY3 (9px gap, only 52px shared strip).
        // Cursor at canvas x=2500 is inside the DISPLAY1-DISPLAY2 shared strip [2338,5742) → resolves.
        var mgr = BuildManager(BuildDavidPatchedLayout());

        // physMonitorLeft=0, physMonitorTop=2169 → canvasCursorX = 2338 + (162 - 0) = 2500
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY1", MoveDirection.Up, 162, 2170, 0, 2169);
        Assert.IsTrue(r.IsResolved, "Cursor inside the DISPLAY1-DISPLAY2 shared strip should resolve UP");
        Assert.AreEqual("\\\\.\\DISPLAY2", r.TargetMonitorId);
    }

    [TestMethod]
    public void ByMonitorId_Display1_Up_CursorFarRight_NotResolved()
    {
        // canvas x=5790 falls in the tiny 52px strip shared between DISPLAY1 and DISPLAY3, but
        // DISPLAY1.Up=DISPLAY2 (closer/larger overlap). The cursor is outside the DISPLAY1-DISPLAY2
        // shared strip [2338,5742) → transition does not fire.
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY1", MoveDirection.Up, 48, 2170, -3404, 2169);
        Assert.IsFalse(r.IsResolved, "Cursor outside the DISPLAY1-DISPLAY2 shared strip must not trigger transition");
    }

    [TestMethod]
    public void ByMonitorId_Display3_Down_NoCursorProvided_StillResolvesForBackwardCompat()
    {
        // Legacy 3-arg call omits cursor → sentinel int.MinValue → overlap check skipped.
        var mgr = BuildManager(BuildDavidPatchedLayout());
        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY3", MoveDirection.Down);
        Assert.IsTrue(r.IsResolved, "Legacy call without cursor coords must still resolve");
    }

    [TestMethod]
    public void SmallSharedHorizontalStrip_IsAdjacent()
    {
        // Replicates DISPLAY3/DISPLAY1: large monitors only share a narrow
        // 52px horizontal strip in x. The shared strip is enough for adjacency.
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "WIDE", 0, 0, 3840, 2160),
            Mon("PC2", "TALL", 3788, 2160, 3456, 2160), // x=[3788,7244), y=[2160,4320); shared x strip = [3788,3840) = 52px
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 3839, 2159, MoveDirection.Down);

        Assert.IsTrue(r.IsResolved, "52px shared strip should be enough for adjacency");
        Assert.AreEqual("PC2", r.TargetMachine);
    }

    [TestMethod]
    public void XOverlapWithinThreshold_SharedYRange_IsAdjacent()
    {
        // Replicates AU001/DISPLAY2: 18px x-overlap but they share most of their
        // Y range. The overlap is in X only (< OverlapThreshold=64) so the pair
        // is not skipped and horizontal adjacency is detected.
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "LEFT", 0, 0, 1920, 1080),      // right edge at x=1920
            Mon("PC2", "RIGHT", 1902, 9, 3840, 2160),  // left edge at x=1902 (18px overlap in x)
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransition("PC1", 1919, 540, MoveDirection.Right);

        Assert.IsTrue(r.IsResolved, "18px x-overlap should not suppress adjacency when y-overlap is small relative to both thresholds");
        Assert.AreEqual("PC2", r.TargetMachine);
    }

    [TestMethod]
    public void SameMachine_LargeGap_IsAdjacent()
    {
        // Two monitors on the same machine with a 500px gap must still be treated as adjacent
        // so the whole machine group stays connected. This is the root cause of the
        // Delphinium DISPLAY1 isolation bug where a 122px stored gap exceeded AdjacencyTolerance.
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "TOP", 0, 0, 3840, 2160),
            Mon("PC1", "BOT", 270, 2660, 3456, 2160), // 500px gap below TOP
        };
        var mgr = BuildManager(layout);

        var down = mgr.ResolveEdgeTransitionByMonitorId("PC1", "TOP", MoveDirection.Down);
        var up = mgr.ResolveEdgeTransitionByMonitorId("PC1", "BOT", MoveDirection.Up);

        Assert.IsTrue(down.IsResolved, "Same-machine 500px gap must be treated as adjacent");
        Assert.AreEqual("BOT", down.TargetMonitorId);
        Assert.IsTrue(up.IsResolved);
        Assert.AreEqual("TOP", up.TargetMonitorId);
    }

    [TestMethod]
    public void DifferentMachine_LargeGap_IsNotAdjacent()
    {
        // Monitors on different machines with a 500px gap should NOT be adjacent
        // (only same-machine monitors use the large tolerance).
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "TOP", 0, 0, 3840, 2160),
            Mon("PC2", "BOT", 270, 2660, 3456, 2160), // 500px gap below TOP, different machine
        };
        var mgr = BuildManager(layout);

        var down = mgr.ResolveEdgeTransitionByMonitorId("PC1", "TOP", MoveDirection.Down);

        Assert.IsFalse(down.IsResolved, "Different-machine 500px gap must not be treated as adjacent");
    }

    [TestMethod]
    public void SameMachine_ClosestNeighborWins()
    {
        // Three monitors A(left), B(middle), C(right) on the same machine.
        // A's right neighbor should be B (closer), not C (farther).
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "A", 0, 0, 1920, 1080),
            Mon("PC1", "B", 2000, 0, 1920, 1080),  // 80px gap from A
            Mon("PC1", "C", 4500, 0, 1920, 1080),  // 2580px gap from A
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransitionByMonitorId("PC1", "A", MoveDirection.Right);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual("B", r.TargetMonitorId, "Closest same-machine neighbor should win");
    }

    [TestMethod]
    public void DellaStoredLayout_Della_UpNeighbour_IsDisplay3()
    {
        // Regression: Della's stored settings.json (as read on Della's single-monitor
        // machine, no ExpandLocalMachineMonitorsIfNeeded patching) has an 87px gap
        // between DELLA.top and DISPLAY3.bottom — which exceeded the old 64px tolerance.
        // Increasing AdjacencyTolerance to 128 fixes the cross-machine UP transition.
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("DELLA",       "\\\\.\\DISPLAY1", 4007, 2247, 3840, 2160),
            Mon("Delphinium",  "\\\\.\\DISPLAY2",    0,  476, 3840, 2160),
            Mon("Delphinium",  "\\\\.\\DISPLAY1",  270, 2758, 3456, 2160),
            Mon("Delphinium",  "\\\\.\\DISPLAY3", 3840,    0, 3840, 2160),
            Mon("Delphinium",  "\\\\.\\DISPLAY4", 7680,  699, 2560, 1440),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransitionByMonitorId("DELLA", "\\\\.\\DISPLAY1", MoveDirection.Up);

        Assert.IsTrue(r.IsResolved, "87px cross-machine gap should be within AdjacencyTolerance=128");
        Assert.AreEqual("Delphinium", r.TargetMachine);
        Assert.AreEqual("\\\\.\\DISPLAY3", r.TargetMonitorId);
    }

    [TestMethod]
    public void DellaStoredLayout_Delphinium_Display1_HasUpNeighbour()
    {
        // In the stored layout (as seen from Della), Delphinium DISPLAY1 has a 122px gap
        // above it (to DISPLAY2). Because both monitors belong to "Delphinium" the
        // machine-name-based grouping links them regardless of gap size. DISPLAY1.Up=DISPLAY2.
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("DELLA",       "\\\\.\\DISPLAY1", 4007, 2247, 3840, 2160),
            Mon("Delphinium",  "\\\\.\\DISPLAY2",    0,  476, 3840, 2160),
            Mon("Delphinium",  "\\\\.\\DISPLAY1",  270, 2758, 3456, 2160),
            Mon("Delphinium",  "\\\\.\\DISPLAY3", 3840,    0, 3840, 2160),
            Mon("Delphinium",  "\\\\.\\DISPLAY4", 7680,  699, 2560, 1440),
        };
        var mgr = BuildManager(layout);

        var r = mgr.ResolveEdgeTransitionByMonitorId("Delphinium", "\\\\.\\DISPLAY1", MoveDirection.Up);

        Assert.IsTrue(r.IsResolved, "Same-machine monitors must be grouped regardless of stored gap");
        Assert.AreEqual("Delphinium", r.TargetMachine);
        Assert.AreEqual("\\\\.\\DISPLAY2", r.TargetMonitorId);
    }

    [TestMethod]
    public void ResolveEdgeTransition_PopulatesSrcCanvasFields()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "M1", 100, 200, 1920, 1080),
            Mon("PC2", "M2", 2020, 200, 3840, 2160),
        };
        var mgr = BuildManager(layout);

        // Cursor at right edge of M1 moving right.
        var r = mgr.ResolveEdgeTransition("PC1", 2019, 700, MoveDirection.Right);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual(100, r.SrcCanvasX, "SrcCanvasX should match source monitor X");
        Assert.AreEqual(200, r.SrcCanvasY, "SrcCanvasY should match source monitor Y");
        Assert.AreEqual(1920, r.SrcCanvasWidth, "SrcCanvasWidth should match source monitor Width");
        Assert.AreEqual(1080, r.SrcCanvasHeight, "SrcCanvasHeight should match source monitor Height");
        Assert.AreEqual(2019, r.CanvasCursorX, "CanvasCursorX should equal the input cursorX");
        Assert.AreEqual(700, r.CanvasCursorY, "CanvasCursorY should equal the input cursorY");
    }

    [TestMethod]
    public void ResolveEdgeTransitionByMonitorId_PopulatesSrcCanvasFields()
    {
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("PC1", "SRC", 0, 500, 2560, 1440),
            Mon("PC2", "TGT", 0, 1940, 3840, 2160),
        };
        var mgr = BuildManager(layout);

        // Cursor at bottom of SRC moving down; physMonitorLeft=0, physMonitorTop=500.
        var r = mgr.ResolveEdgeTransitionByMonitorId("PC1", "SRC", MoveDirection.Down, 1000, 1939, 0, 500);

        Assert.IsTrue(r.IsResolved);
        Assert.AreEqual(0, r.SrcCanvasX, "SrcCanvasX");
        Assert.AreEqual(500, r.SrcCanvasY, "SrcCanvasY");
        Assert.AreEqual(2560, r.SrcCanvasWidth, "SrcCanvasWidth");
        Assert.AreEqual(1440, r.SrcCanvasHeight, "SrcCanvasHeight");
    }

    [TestMethod]
    public void ResolveEdgeTransitionByMonitorId_SrcCanvasWidth_EnablesProportionalLandingFormula()
    {
        // Verify the raw data that the proportional landing formula consumes:
        // cursor at 25%, 50%, and 75% across a 2560px source should produce
        // CanvasCursorX values that, divided by SrcCanvasWidth, give ~0.25, ~0.50, ~0.75.
        var layout = new List<MonitorLayoutInfo>
        {
            Mon("SRC_PC", "S", 0, 0, 2560, 1440),
            Mon("TGT_PC", "T", 0, 1440, 3840, 2160),
        };
        var mgr = BuildManager(layout);

        foreach (var (physX, expectedRel) in new[] { (0, 0.0), (640, 0.25), (1280, 0.5), (1920, 0.75), (2559, 1.0) })
        {
            var r = mgr.ResolveEdgeTransitionByMonitorId("SRC_PC", "S", MoveDirection.Down, physX, 1439, 0, 0);
            Assert.IsTrue(r.IsResolved);
            double actualRel = r.SrcCanvasWidth > 0 ? (double)(r.CanvasCursorX - r.SrcCanvasX) / r.SrcCanvasWidth : -1;
            Assert.AreEqual(expectedRel, actualRel, 0.001, $"Proportional X for physX={physX}");
        }
    }
}
