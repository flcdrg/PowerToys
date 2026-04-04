// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.PowerToys.Settings.UI.Helpers;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.UnitTests.BackwardsCompatibility;
using Microsoft.PowerToys.Settings.UI.UnitTests.Mocks;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ViewModelTests
{
    [TestClass]
    public class MouseWithoutBordersMonitorLayout
    {
        private Mock<SettingsUtils> _mwbSettingsUtils;
        private Mock<SettingsUtils> _generalSettingsUtils;
        private MouseWithoutBordersViewModel _viewModel;

        [TestInitialize]
        public void SetUp()
        {
            _mwbSettingsUtils = ISettingsUtilsMocks.GetStubSettingsUtils<MouseWithoutBordersSettings>();
            _generalSettingsUtils = ISettingsUtilsMocks.GetStubSettingsUtils<GeneralSettings>();
        }

        [TestCleanup]
        public void CleanUp()
        {
            _viewModel = null;
        }

        [TestMethod]
        public void RebuildMonitorViewModels_MultiMonitorMachine_AssignsStableIndicesAndScaledPositions()
        {
            _viewModel = CreateViewModel();

            _viewModel.SaveMonitorLayout(new[]
            {
                Mon("A", "A2", 1920, 0, 1920, 1080),
                Mon("B", "B1", -1280, 100, 1280, 1024),
                Mon("A", "A1", 0, 0, 1920, 1080),
            });

            _viewModel.RebuildMonitorViewModels();

            Assert.AreEqual(3, _viewModel.MonitorViewModels.Count);

            var a1 = _viewModel.MonitorViewModels.Single(vm => vm.MonitorId == "A1");
            var a2 = _viewModel.MonitorViewModels.Single(vm => vm.MonitorId == "A2");
            var b1 = _viewModel.MonitorViewModels.Single(vm => vm.MonitorId == "B1");

            Assert.AreEqual("A Monitor 1", a1.Label);
            Assert.AreEqual("A Monitor 2", a2.Label);

            Assert.AreEqual(171.429d, b1.CanvasLeft, 0.001, "Expected left drag margin for the left-most monitor.");
            Assert.AreEqual(285.714d, a1.CanvasLeft, 0.001, "Expected normalized+scaled X for A1.");
            Assert.AreEqual(457.143d, a2.CanvasLeft, 0.001, "Expected normalized+scaled X for A2.");

            Assert.AreEqual(199.821d, a1.CanvasTop, 0.001);
            Assert.AreEqual(208.750d, b1.CanvasTop, 0.001);
        }

        [TestMethod]
        public void CommitCanvasLayout_MultiMonitorMove_PersistsMovedCoordinates()
        {
            _viewModel = CreateViewModel();

            _viewModel.SaveMonitorLayout(new[]
            {
                Mon("A", "A1", 0, 0, 1920, 1080),
                Mon("A", "A2", 1920, 0, 1920, 1080),
            });

            _viewModel.RebuildMonitorViewModels();

            var first = _viewModel.MonitorViewModels.Single(vm => vm.MonitorId == "A1");
            first.UpdatePosition(first.CanvasLeft + 37.0, first.CanvasTop + 19.0);

            _viewModel.CommitCanvasLayout();

            var persisted = _viewModel.MonitorLayouts.Single(m => m.MonitorId == "A1");

            Assert.AreEqual(355, persisted.X, "Canvas delta should be converted back to pixel coordinates by scale.");
            Assert.AreEqual(182, persisted.Y, "Canvas delta should be converted back to pixel coordinates by scale.");
            Assert.AreEqual(1920, persisted.Width);
            Assert.AreEqual(1080, persisted.Height);
        }

        [TestMethod]
        public void CommitCanvasLayout_PreservesOriginalLayoutOrigin()
        {
            _viewModel = CreateViewModel();

            _viewModel.SaveMonitorLayout(new[]
            {
                Mon("A", "A1", 4000, 300, 1920, 1080),
                Mon("A", "A2", 5920, 300, 1920, 1080),
            });

            _viewModel.RebuildMonitorViewModels();

            var first = _viewModel.MonitorViewModels.Single(vm => vm.MonitorId == "A1");
            first.UpdatePosition(first.CanvasLeft + 37.0, first.CanvasTop + 19.0);

            _viewModel.CommitCanvasLayout();

            var persisted = _viewModel.MonitorLayouts.Single(m => m.MonitorId == "A1");

            Assert.AreEqual(4355, persisted.X, "Saving from the canvas should preserve the shared layout origin.");
            Assert.AreEqual(482, persisted.Y, "Saving from the canvas should preserve the shared layout origin.");
        }

        [TestMethod]
        public void RebuildMonitorViewModels_WhenLayoutEmpty_UsesMachineMatrixFallback()
        {
            _viewModel = CreateViewModel();

            _viewModel.SaveMonitorLayout(Array.Empty<MonitorLayoutInfo>());
            _viewModel.MachineMatrixString = new IndexedObservableCollection<MouseWithoutBordersViewModel.DeviceViewModel>(new[]
            {
                new MouseWithoutBordersViewModel.DeviceViewModel { Name = "RemoteA" },
                new MouseWithoutBordersViewModel.DeviceViewModel { Name = "RemoteB" },
                new MouseWithoutBordersViewModel.DeviceViewModel { Name = string.Empty },
                new MouseWithoutBordersViewModel.DeviceViewModel { Name = string.Empty },
            });

            _viewModel.RebuildMonitorViewModels();

            Assert.AreEqual(2, _viewModel.MonitorLayouts.Count);
            Assert.AreEqual(2, _viewModel.MonitorViewModels.Count);
            Assert.IsTrue(_viewModel.MonitorLayouts.All(m => m.MachineName == "RemoteA" || m.MachineName == "RemoteB"));
        }

        [TestMethod]
        public void SaveMonitorLayout_ReplacesCollectionWithProvidedMonitors()
        {
            _viewModel = CreateViewModel();

            _viewModel.SaveMonitorLayout(new[]
            {
                Mon("M1", "M1-1", 0, 0, 1920, 1080),
                Mon("M2", "M2-1", 1920, 0, 2560, 1440),
                Mon("M2", "M2-2", 4480, 0, 1920, 1080),
            });

            Assert.AreEqual(3, _viewModel.MonitorLayouts.Count);
            Assert.AreEqual(2, _viewModel.MonitorLayouts.Count(m => m.MachineName == "M2"));
        }

        private MouseWithoutBordersViewModel CreateViewModel(MouseWithoutBordersSettings settings = null)
        {
            var moduleSettings = settings ?? new MouseWithoutBordersSettings();

            _mwbSettingsUtils
                .Setup(x => x.GetSettingsOrDefault<MouseWithoutBordersSettings>(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(moduleSettings);

            var generalSettingsRepository = new BackCompatTestProperties.MockSettingsRepository<GeneralSettings>(_generalSettingsUtils.Object);

            return new MouseWithoutBordersViewModel(
                _mwbSettingsUtils.Object,
                generalSettingsRepository,
                _ => 0,
                null);
        }

        private static MonitorLayoutInfo Mon(string machine, string id, int x, int y, int width, int height)
        {
            return new MonitorLayoutInfo
            {
                MachineName = machine,
                MonitorId = id,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                IsPrimary = false,
            };
        }
    }
}
