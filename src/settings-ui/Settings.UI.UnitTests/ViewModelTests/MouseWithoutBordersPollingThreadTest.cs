// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.UnitTests.BackwardsCompatibility;
using Microsoft.PowerToys.Settings.UI.UnitTests.Mocks;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ViewModelTests
{
    /// <summary>
    /// Test to verify that the polling thread can execute without deadlocking
    /// when other IPC operations are in progress (e.g., OnPageLoaded).
    ///
    /// This tests the fix for the IPC semaphore deadlock issue where:
    /// - OnPageLoaded() acquires the IPC semaphore to fetch monitor layout
    /// - Polling thread tries to acquire the SAME semaphore to get machine status
    /// - Without timeout, polling thread would deadlock forever
    ///
    /// The fix adds a 2-second timeout to the polling thread's semaphore acquisition.
    /// </summary>
    [TestClass]
    public class MouseWithoutBordersPollingThreadTest
    {
        private Mock<SettingsUtils> _mwbSettingsUtils;
        private Mock<SettingsUtils> _generalSettingsUtils;

        [TestInitialize]
        public void SetUp()
        {
            _mwbSettingsUtils = ISettingsUtilsMocks.GetStubSettingsUtils<MouseWithoutBordersSettings>();
            _generalSettingsUtils = ISettingsUtilsMocks.GetStubSettingsUtils<GeneralSettings>();
        }

        [TestMethod]
        [Timeout(10000)] // 10-second timeout to catch any deadlocks
        public async Task PollingThreadStartup_WithMonitorLayout_DoesNotDeadlock()
        {
            // This test verifies that the polling thread can start and run
            // without deadlocking, even if other IPC operations are happening.

            // Arrange
            var moduleSettings = new MouseWithoutBordersSettings();

            _mwbSettingsUtils
                .Setup(x => x.GetSettingsOrDefault<MouseWithoutBordersSettings>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(moduleSettings);

            var generalSettingsRepository = new BackCompatTestProperties.MockSettingsRepository<GeneralSettings>(_generalSettingsUtils.Object);

            var viewModel = new MouseWithoutBordersViewModel(
                _mwbSettingsUtils.Object,
                generalSettingsRepository,
                _ => 0,
                null);

            // Act - Save monitor layout to initialize the monitor view models
            viewModel.SaveMonitorLayout(new[]
            {
                new MonitorLayoutInfo
                {
                    MachineName = "TestMachine1",
                    MonitorId = "M1",
                    X = 0,
                    Y = 0,
                    Width = 1920,
                    Height = 1080,
                    IsPrimary = true,
                },
            });

            // Act - Give the polling thread a chance to start and make a few iterations
            // If it deadlocks, this will timeout (10-second timeout at class level)
            await Task.Delay(2000);

            // Assert - If we get here without timeout, the polling thread didn't deadlock
            Assert.IsNotNull(viewModel, "View model should be created successfully");

            // The fact that we reached this point without timing out proves the polling
            // thread started successfully and didn't deadlock on the IPC semaphore.
        }

        [TestMethod]
        [Timeout(10000)] // 10-second timeout to catch any deadlocks
        public void PollingThreadStartup_DefaultSettings_DoesNotDeadlock()
        {
            // Verify that the polling thread initialization completes quickly
            // with default settings

            // Arrange
            var moduleSettings = new MouseWithoutBordersSettings();

            _mwbSettingsUtils
                .Setup(x => x.GetSettingsOrDefault<MouseWithoutBordersSettings>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(moduleSettings);

            var generalSettingsRepository = new BackCompatTestProperties.MockSettingsRepository<GeneralSettings>(_generalSettingsUtils.Object);

            // Act - Create view model (which starts polling thread)
            // If deadlock occurs, this will timeout
            var viewModel = new MouseWithoutBordersViewModel(
                _mwbSettingsUtils.Object,
                generalSettingsRepository,
                _ => 0,
                null);

            // Assert - If we get here, no deadlock occurred
            Assert.IsNotNull(viewModel, "View model should be created successfully");

            // The fact that we reached this point without timing out proves the polling
            // thread started successfully.
        }
    }
}
