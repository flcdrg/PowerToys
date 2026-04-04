// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using MouseWithoutBorders.Core;

namespace MouseWithoutBorders.UnitTests.Core;

[TestClass]
public sealed class ProportionalPositionTests
{
    [TestMethod]
    public void PhysicalCoords_LeftEdge_ReturnsZero()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 100,
            physMin: 100,
            physMax: 200,
            canvasCoord: 0,
            canvasMin: 0,
            canvasSize: 1000);

        Assert.AreEqual(0.0, result, 1e-9);
    }

    [TestMethod]
    public void PhysicalCoords_RightEdge_ReturnsNearOne()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 199,
            physMin: 100,
            physMax: 200,
            canvasCoord: 0,
            canvasMin: 0,
            canvasSize: 1000);

        Assert.AreEqual(0.99, result, 0.001);
    }

    [TestMethod]
    public void PhysicalCoords_Middle_ReturnsHalf()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 150,
            physMin: 100,
            physMax: 200,
            canvasCoord: 0,
            canvasMin: 0,
            canvasSize: 1000);

        Assert.AreEqual(0.5, result, 1e-9);
    }

    [TestMethod]
    public void PhysicalCoords_AtQuarter_ReturnsQuarter()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 640,
            physMin: 0,
            physMax: 2560,
            canvasCoord: 0,
            canvasMin: 0,
            canvasSize: 0);

        Assert.AreEqual(0.25, result, 1e-9);
    }

    [TestMethod]
    public void PhysicalCoords_AtThreeQuarters_ReturnsThreeQuarters()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 1920,
            physMin: 0,
            physMax: 2560,
            canvasCoord: 0,
            canvasMin: 0,
            canvasSize: 0);

        Assert.AreEqual(0.75, result, 1e-9);
    }

    [TestMethod]
    public void PhysicalCoords_BeyondMax_ClampsToOne()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 300,
            physMin: 100,
            physMax: 200,
            canvasCoord: 0,
            canvasMin: 0,
            canvasSize: 1000);

        Assert.AreEqual(1.0, result, 1e-9);
    }

    [TestMethod]
    public void PhysicalCoords_BelowMin_ClampsToZero()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 50,
            physMin: 100,
            physMax: 200,
            canvasCoord: 0,
            canvasMin: 0,
            canvasSize: 1000);

        Assert.AreEqual(0.0, result, 1e-9);
    }

    [TestMethod]
    public void CanvasFallback_WhenNoPhysical_UsesCanvasCoords()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 0,
            physMin: null,
            physMax: null,
            canvasCoord: 960,
            canvasMin: 0,
            canvasSize: 1920);

        Assert.AreEqual(0.5, result, 1e-9);
    }

    [TestMethod]
    public void CanvasFallback_ZeroCanvasSize_ReturnsMidpoint()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 0,
            physMin: null,
            physMax: null,
            canvasCoord: 100,
            canvasMin: 0,
            canvasSize: 0);

        Assert.AreEqual(0.5, result, 1e-9);
    }

    [TestMethod]
    public void PhysicalCoords_ZeroWidthMonitor_FallsBackToCanvas()
    {
        double result = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 100,
            physMin: 100,
            physMax: 100,
            canvasCoord: 480,
            canvasMin: 0,
            canvasSize: 1920);

        Assert.AreEqual(0.25, result, 1e-9);
    }

    [TestMethod]
    public void Monitor4ToDella_LeftEdge_UniversalXIsZero()
    {
        double rel = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 0,
            physMin: 0,
            physMax: 2560,
            canvasCoord: 0,
            canvasMin: 0,
            canvasSize: 2560);
        int universalX = (int)(rel * 65535);

        Assert.AreEqual(0, universalX);
    }

    [TestMethod]
    public void Monitor4ToDella_RightEdge_UniversalXIsNearMax()
    {
        double rel = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 2559,
            physMin: 0,
            physMax: 2560,
            canvasCoord: 2559,
            canvasMin: 0,
            canvasSize: 2560);
        int universalX = (int)(rel * 65535);

        Assert.IsTrue(universalX > 65500, $"Expected near-max universal X, got {universalX}");
    }

    [TestMethod]
    public void Monitor4ToDella_Middle_UniversalXIsHalf()
    {
        double rel = MonitorLayoutNavigator.ProportionalPosition(
            physCoord: 1280,
            physMin: 0,
            physMax: 2560,
            canvasCoord: 1280,
            canvasMin: 0,
            canvasSize: 2560);
        int universalX = (int)(rel * 65535);

        Assert.AreEqual(32767, universalX);
    }
}
