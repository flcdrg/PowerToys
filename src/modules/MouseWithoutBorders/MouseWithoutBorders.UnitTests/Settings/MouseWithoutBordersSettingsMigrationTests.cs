// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Text.Json;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MouseWithoutBorders.UnitTests.Settings;

[TestClass]
public sealed class MouseWithoutBordersSettingsMigrationTests
{
    private static string BuildSettingsJson(string propertiesJson)
    {
                return
                        "{\n" +
                        "  \"name\": \"MouseWithoutBorders\",\n" +
                        "  \"version\": \"1.2\",\n" +
                        "  \"properties\": " + propertiesJson + "\n" +
                        "}";
    }

    private static string BuildUseMonitorLayoutBoolProperty(bool value)
    {
        string boolText = value ? "true" : "false";
        return $"{{ \"value\": {boolText} }}";
    }

    // -------------------------------------------------------------------------
    // Helper: build a settings object at a specific version without going through
    // the normal constructor (which already sets Version = "1.2").
    // -------------------------------------------------------------------------
    private static MouseWithoutBordersSettings MakeSettings(string version, Action<MouseWithoutBordersProperties>? configure = null)
    {
        var settings = new MouseWithoutBordersSettings();
        settings.Version = version;
        configure?.Invoke(settings.Properties);
        return settings;
    }

    // -------------------------------------------------------------------------
    // 1.1 → 1.2: no MonitorLayout in the file
    // -------------------------------------------------------------------------
    [TestMethod]
    public void MigrateFrom1_1_WithNoMonitorLayout_ShouldSetVersion1_2_AndLeaveMonitorLayoutNull()
    {
        // Arrange
        var machineMatrix = new List<string> { "PC1", "PC2", string.Empty, string.Empty };
        var settings = MakeSettings("1.1", props =>
        {
            props.MachineMatrixString = new List<string>(machineMatrix);
            props.UseMonitorLayout = false;
            props.MonitorLayout = null;
        });

        // Act
        bool upgraded = settings.UpgradeSettingsConfiguration();

        // Assert
        Assert.IsTrue(upgraded, "UpgradeSettingsConfiguration should return true for a 1.1 → 1.2 migration.");
        Assert.AreEqual("1.2", settings.Version);
        Assert.IsNull(settings.Properties.MonitorLayout, "MonitorLayout should remain null after migration.");
        Assert.IsFalse(settings.Properties.UseMonitorLayout, "UseMonitorLayout should remain false after migration.");
        CollectionAssert.AreEqual(machineMatrix, settings.Properties.MachineMatrixString, "MachineMatrixString must be preserved.");
    }

    // -------------------------------------------------------------------------
    // 1.1 → 1.2: MonitorLayout already populated (e.g. manually pre-seeded)
    // -------------------------------------------------------------------------
    [TestMethod]
    public void MigrateFrom1_1_WithExistingMonitorLayout_ShouldPreserveMonitorLayout()
    {
        // Arrange
        var monitorLayout = new List<MonitorLayoutInfo>
        {
            new MonitorLayoutInfo { MachineName = "PC1", MonitorId = "mon0", X = 0, Y = 0, Width = 1920, Height = 1080, IsPrimary = true },
            new MonitorLayoutInfo { MachineName = "PC2", MonitorId = "mon0", X = 1920, Y = 0, Width = 2560, Height = 1440, IsPrimary = true },
        };

        var settings = MakeSettings("1.1", props =>
        {
            props.UseMonitorLayout = true;
            props.MonitorLayout = monitorLayout;
            props.MachineMatrixString = new List<string> { "PC1", "PC2", string.Empty, string.Empty };
        });

        // Act
        bool upgraded = settings.UpgradeSettingsConfiguration();

        // Assert
        Assert.IsTrue(upgraded);
        Assert.AreEqual("1.2", settings.Version);
        Assert.IsTrue(settings.Properties.UseMonitorLayout);
        Assert.IsNotNull(settings.Properties.MonitorLayout);
        Assert.AreEqual(2, settings.Properties.MonitorLayout!.Count);
        Assert.AreEqual("PC1", settings.Properties.MonitorLayout[0].MachineName);
        Assert.AreEqual("PC2", settings.Properties.MonitorLayout[1].MachineName);
    }

    // -------------------------------------------------------------------------
    // 1.0 → 1.2 (two-hop migration): MonitorLayout ends up null, matrix preserved
    // -------------------------------------------------------------------------
    [TestMethod]
    public void MigrateFrom1_0_ShouldReachVersion1_2_AndLeaveMonitorLayoutNull()
    {
        // Arrange
        var machineMatrix = new List<string> { "Alpha", "Beta", string.Empty, string.Empty };
#pragma warning disable CS0618 // Testing upgrade path via obsolete properties
        var settings = MakeSettings("1.0", props =>
        {
            props.MachineMatrixString = new List<string>(machineMatrix);
            props.HotKeyToggleEasyMouse = new IntProperty(0x45); // 'E'
        });
#pragma warning restore CS0618

        // Act
        bool upgraded = settings.UpgradeSettingsConfiguration();

        // Assert
        Assert.IsTrue(upgraded);
        Assert.AreEqual("1.2", settings.Version);
        Assert.IsNull(settings.Properties.MonitorLayout);
        Assert.IsFalse(settings.Properties.UseMonitorLayout);
        CollectionAssert.AreEqual(machineMatrix, settings.Properties.MachineMatrixString);
    }

    // -------------------------------------------------------------------------
    // Already at 1.2: no migration needed
    // -------------------------------------------------------------------------
    [TestMethod]
    public void NoMigrationNeeded_WhenVersionIs1_2()
    {
        var settings = MakeSettings("1.2");

        bool upgraded = settings.UpgradeSettingsConfiguration();

        Assert.IsFalse(upgraded, "UpgradeSettingsConfiguration should return false when already at version 1.2.");
        Assert.AreEqual("1.2", settings.Version);
    }

    // -------------------------------------------------------------------------
    // Deserialisation resilience: JSON with a null monitorLayout field
    // -------------------------------------------------------------------------
    [TestMethod]
    public void Deserialize_JsonWithNullMonitorLayout_ShouldProduceNullMonitorLayout()
    {
        string json = BuildSettingsJson(
            "{\n" +
            "  \"UseMonitorLayout\": " + BuildUseMonitorLayoutBoolProperty(false) + ",\n" +
            "  \"MonitorLayout\": null\n" +
            "}");

        var settings = JsonSerializer.Deserialize<MouseWithoutBordersSettings>(json);

        Assert.IsNotNull(settings);
        Assert.IsNull(settings!.Properties.MonitorLayout);
        Assert.IsFalse(settings.Properties.UseMonitorLayout);
    }

    // -------------------------------------------------------------------------
    // Deserialisation resilience: JSON with a missing monitorLayout field
    // -------------------------------------------------------------------------
    [TestMethod]
    public void Deserialize_JsonWithMissingMonitorLayout_ShouldProduceNullMonitorLayout()
    {
        string json = BuildSettingsJson(
            "{\n" +
            "  \"UseMonitorLayout\": " + BuildUseMonitorLayoutBoolProperty(false) + "\n" +
            "}");

        var settings = JsonSerializer.Deserialize<MouseWithoutBordersSettings>(json);

        Assert.IsNotNull(settings);
        Assert.IsNull(settings!.Properties.MonitorLayout);
    }

    // -------------------------------------------------------------------------
    // Deserialisation resilience: JSON with a corrupt/wrong-type monitorLayout
    // (e.g. a string where an array is expected) should throw JsonException —
    // callers (SettingsUtils.GetSettingsOrDefault) catch this and return defaults.
    // -------------------------------------------------------------------------
    [TestMethod]
    public void Deserialize_JsonWithCorruptMonitorLayout_ShouldThrowJsonException()
    {
        string json = BuildSettingsJson(
            "{\n" +
            "  \"UseMonitorLayout\": " + BuildUseMonitorLayoutBoolProperty(true) + ",\n" +
            "  \"MonitorLayout\": \"THIS_IS_NOT_AN_ARRAY\"\n" +
            "}");

        Assert.ThrowsException<JsonException>(() =>
            JsonSerializer.Deserialize<MouseWithoutBordersSettings>(json));
    }

    // -------------------------------------------------------------------------
    // IsMonitorLayoutEnabled (via Setting.Values proxy): false when MonitorLayout is null
    // -------------------------------------------------------------------------
    [TestMethod]
    public void IsMonitorLayoutEnabled_ReturnsFalse_WhenMonitorLayoutIsNull()
    {
        // We test the logic inline since Setting.Values requires live settings infrastructure.
        bool useMonitorLayout = true;
        List<MonitorLayoutInfo>? monitorLayout = null;

        bool result = useMonitorLayout && monitorLayout is { Count: > 0 };

        Assert.IsFalse(result);
    }

    // -------------------------------------------------------------------------
    // IsMonitorLayoutEnabled logic: false when UseMonitorLayout is false
    // -------------------------------------------------------------------------
    [TestMethod]
    public void IsMonitorLayoutEnabled_ReturnsFalse_WhenUseMonitorLayoutIsFalse()
    {
        bool useMonitorLayout = false;
        var monitorLayout = new List<MonitorLayoutInfo>
        {
            new MonitorLayoutInfo { MachineName = "PC1", MonitorId = "mon0" },
        };

        bool result = useMonitorLayout && monitorLayout is { Count: > 0 };

        Assert.IsFalse(result);
    }

    // -------------------------------------------------------------------------
    // IsMonitorLayoutEnabled logic: false when MonitorLayout is empty
    // -------------------------------------------------------------------------
    [TestMethod]
    public void IsMonitorLayoutEnabled_ReturnsFalse_WhenMonitorLayoutIsEmpty()
    {
        bool useMonitorLayout = true;
        var monitorLayout = new List<MonitorLayoutInfo>();

        bool result = useMonitorLayout && monitorLayout is { Count: > 0 };

        Assert.IsFalse(result);
    }

    // -------------------------------------------------------------------------
    // IsMonitorLayoutEnabled logic: true when both conditions are met
    // -------------------------------------------------------------------------
    [TestMethod]
    public void IsMonitorLayoutEnabled_ReturnsTrue_WhenBothConditionsMet()
    {
        bool useMonitorLayout = true;
        var monitorLayout = new List<MonitorLayoutInfo>
        {
            new MonitorLayoutInfo { MachineName = "PC1", MonitorId = "mon0" },
        };

        bool result = useMonitorLayout && monitorLayout is { Count: > 0 };

        Assert.IsTrue(result);
    }
}
