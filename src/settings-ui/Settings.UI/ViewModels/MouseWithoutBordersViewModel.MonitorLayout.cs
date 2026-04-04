// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public partial class MouseWithoutBordersViewModel
    {
        private ObservableCollection<MonitorLayoutInfo> _monitorLayouts;

        public ObservableCollection<MonitorLayoutInfo> MonitorLayouts
        {
            get => _monitorLayouts;
            private set
            {
                _monitorLayouts = value;
                OnPropertyChanged();
            }
        }

        public bool UseMonitorLayout
        {
            get => Settings.Properties.UseMonitorLayout;

            set
            {
                if (Settings.Properties.UseMonitorLayout != value)
                {
                    Settings.Properties.UseMonitorLayout = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(IsMatrixOneRowSettingEnabled));
                    Task.Run(async () => await SubmitSetUseMonitorLayoutAsync(value));
                }
            }
        }

        /// <summary>Gets a value indicating whether the Devices In A Single Row setting is editable (disabled when UseMonitorLayout is on).</summary>
        public bool IsMatrixOneRowSettingEnabled => !UseMonitorLayout;

        public void SaveMonitorLayout(IEnumerable<MonitorLayoutInfo> monitors)
        {
            var monitorList = monitors != null ? new List<MonitorLayoutInfo>(monitors) : new List<MonitorLayoutInfo>();
            Settings.Properties.MonitorLayout = monitorList;
            _monitorLayouts = new ObservableCollection<MonitorLayoutInfo>(monitorList);
            OnPropertyChanged(nameof(MonitorLayouts));
            NotifyPropertyChanged(nameof(UseMonitorLayout));
            Task.Run(async () => await SubmitSetMonitorLayoutAsync(monitorList));
        }

        private ObservableCollection<MonitorViewModel> _monitorViewModels = new();

        private static bool MonitorLayoutsEquivalent(IReadOnlyList<MonitorLayoutInfo> a, IReadOnlyList<MonitorLayoutInfo> b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null || a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                var left = a[i];
                var right = b[i];

                if (!string.Equals(left.MachineName, right.MachineName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(left.MonitorId, right.MonitorId, StringComparison.Ordinal)
                    || left.X != right.X
                    || left.Y != right.Y
                    || left.Width != right.Width
                    || left.Height != right.Height
                    || left.IsPrimary != right.IsPrimary)
                {
                    return false;
                }
            }

            return true;
        }

        public ObservableCollection<MonitorViewModel> MonitorViewModels
        {
            get => _monitorViewModels;
            private set
            {
                _monitorViewModels = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Converts <see cref="MonitorLayouts"/> into <see cref="MonitorViewModels"/> using a
        /// proportional scale factor derived from the bounding box of all monitors so that the
        /// layout fits within an ~800×500 canvas target.
        /// </summary>
        private List<MonitorLayoutInfo> BuildDefaultMonitorLayout()
        {
            var machines = _machineMatrixString?
                .ToEnumerable()
                .Select(vm => vm.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (machines == null || machines.Count == 0)
            {
                return new List<MonitorLayoutInfo>();
            }

            const int defaultWidth = 1920;
            const int defaultHeight = 1080;

            var fallback = machines.Select((name, idx) => new MonitorLayoutInfo
            {
                MachineName = name,
                MonitorId = $"{name}-0",
                X = idx * defaultWidth,
                Y = 0,
                Width = defaultWidth,
                Height = defaultHeight,
                IsPrimary = true,
            }).ToList();

            return ExpandLocalMachineMonitorsIfNeeded(fallback);
        }

        private List<MonitorLayoutInfo> ExpandLocalMachineMonitorsIfNeeded(IEnumerable<MonitorLayoutInfo> layout)
        {
            var layoutList = layout?.ToList() ?? new List<MonitorLayoutInfo>();
            if (layoutList.Count == 0)
            {
                return layoutList;
            }

            var localNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Dns.GetHostName(),
                Environment.MachineName,
            };

            var localEntries = layoutList
                .Where(m => !string.IsNullOrWhiteSpace(m.MachineName) && localNames.Contains(m.MachineName))
                .ToList();

            if (localEntries.Count == 0)
            {
                return layoutList;
            }

            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length <= 1)
            {
                return layoutList;
            }

            // This machine has multiple monitors. Always re-derive every monitor's position
            // from Screen.AllScreens so that individual monitors within the local machine's
            // group cannot be repositioned independently via the drag-and-drop UI. The
            // group's anchor (the top-left of the bounding box of all saved local entries)
            // is preserved so that the group's position relative to other machines on the
            // canvas does not change.
            int anchorX = localEntries.Min(e => e.X);
            int anchorY = localEntries.Min(e => e.Y);
            string localMachineName = localEntries[0].MachineName;
            int minLocalX = screens.Min(s => s.Bounds.X);
            int minLocalY = screens.Min(s => s.Bounds.Y);

            // Build a lookup from DeviceName to stored entry so we can use the stored
            // physical Width/Height instead of Screen.Bounds (which reports logical pixels
            // at the display's DPI scale). Using logical sizes would introduce artificial
            // gaps between physically-adjacent monitors whenever DPI ≠ 100%.
            var storedByDeviceName = localEntries
                .Where(e => !string.IsNullOrEmpty(e.MonitorId))
                .ToDictionary(e => e.MonitorId, e => e, StringComparer.OrdinalIgnoreCase);

            var expandedLocal = screens
                .Select(s =>
                {
                    storedByDeviceName.TryGetValue(s.DeviceName, out var stored);
                    return new MonitorLayoutInfo
                    {
                        MachineName = localMachineName,
                        MonitorId = s.DeviceName,
                        X = anchorX + (s.Bounds.X - minLocalX),
                        Y = anchorY + (s.Bounds.Y - minLocalY),
                        Width = stored?.Width ?? s.Bounds.Width,
                        Height = stored?.Height ?? s.Bounds.Height,
                        IsPrimary = s.Primary,
                    };
                })
                .OrderBy(m => m.X)
                .ThenBy(m => m.Y)
                .ToList();

            // Replace all existing local entries with the freshly expanded set, inserting
            // at the position of the first local entry so the ordering relative to other
            // machines is preserved.
            var localEntriesSet = new HashSet<MonitorLayoutInfo>(localEntries, ReferenceEqualityComparer.Instance);
            var result = new List<MonitorLayoutInfo>(layoutList.Count - localEntries.Count + expandedLocal.Count);
            bool insertedExpandedLocal = false;

            foreach (var item in layoutList)
            {
                if (localEntriesSet.Contains(item))
                {
                    if (!insertedExpandedLocal)
                    {
                        result.AddRange(expandedLocal);
                        insertedExpandedLocal = true;
                    }

                    // Skip subsequent local entries – replaced by the expanded set.
                }
                else
                {
                    result.Add(item);
                }
            }

            return result;
        }

        public override void OnPageLoaded()
        {
            base.OnPageLoaded();

            _ = Task.Run(async () =>
            {
                try
                {
                    var layout = await FetchMonitorLayoutAsync();
                    if (layout is { Count: > 0 })
                    {
                        _uiDispatcherQueue.TryEnqueue(() =>
                        {
                            _monitorLayouts = new ObservableCollection<MonitorLayoutInfo>(layout);
                            RebuildMonitorViewModels();
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogInfo($"FetchMonitorLayoutAsync on page load error: {ex}");
                }
            });
        }

        public void RebuildMonitorViewModels()
        {
            if (_monitorLayouts == null || !_monitorLayouts.Any())
            {
                var defaults = BuildDefaultMonitorLayout();
                if (defaults.Count == 0)
                {
                    _monitorViewModels = new ObservableCollection<MonitorViewModel>();
                    OnPropertyChanged(nameof(MonitorViewModels));
                    return;
                }

                _monitorLayouts = new ObservableCollection<MonitorLayoutInfo>(defaults);
            }

            var expandedLayout = ExpandLocalMachineMonitorsIfNeeded(_monitorLayouts);
            if (!MonitorLayoutsEquivalent(expandedLayout, _monitorLayouts))
            {
                _monitorLayouts = new ObservableCollection<MonitorLayoutInfo>(expandedLayout);
                OnPropertyChanged(nameof(MonitorLayouts));
            }

            int minX = _monitorLayouts.Min(m => m.X);
            int minY = _monitorLayouts.Min(m => m.Y);
            int maxX = _monitorLayouts.Max(m => m.X + m.Width);
            int maxY = _monitorLayouts.Max(m => m.Y + m.Height);

            int totalWidth = maxX - minX;
            int totalHeight = maxY - minY;

            const double targetWidth = 800.0;
            const double targetHeight = 500.0;

            int maxMonitorWidth = _monitorLayouts.Max(m => m.Width);
            int maxMonitorHeight = _monitorLayouts.Max(m => m.Height);

            // Leave enough margin for at least one full monitor tile on each side so a dragged
            // machine can be repositioned to another monitor's outer edge without immediately
            // clamping against the canvas boundary.
            double scaleX = totalWidth > 0 ? targetWidth / (totalWidth + (2.0 * maxMonitorWidth)) : 1.0;
            double scaleY = totalHeight > 0 ? targetHeight / (totalHeight + (2.0 * maxMonitorHeight)) : 1.0;
            double scale = Math.Min(scaleX, scaleY);

            // Center the scaled content so the drag margin is equal on all sides.
            double offsetX = (targetWidth - (totalWidth * scale)) / 2.0;
            double offsetY = (targetHeight - (totalHeight * scale)) / 2.0;

            // Group monitors by machine and sort by X then Y to assign stable 1-based indices.
            var machineGroups = _monitorLayouts
                .GroupBy(m => m.MachineName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(m => m.X).ThenBy(m => m.Y).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            var viewModels = new ObservableCollection<MonitorViewModel>();

            foreach (var monitor in _monitorLayouts)
            {
                var machineMonitors = machineGroups[monitor.MachineName ?? string.Empty];
                int index = machineMonitors.IndexOf(monitor) + 1; // 1-based

                var vm = new MonitorViewModel(monitor, index, scale, minX, minY, offsetX, offsetY);

                // Set the status brush and text based on the machine's connection status
                UpdateMonitorViewModelStatus(vm);

                Logger.LogInfo($"[MWB] Created monitor view model for {vm.Label}, initial status: {vm.StatusText}");

                viewModels.Add(vm);
            }

            _monitorViewModels = viewModels;
            OnPropertyChanged(nameof(MonitorViewModels));
        }

        /// <summary>
        /// Updates the StatusBrush and StatusText for a monitor view model based on its machine's connection status.
        /// </summary>
        private void UpdateMonitorViewModelStatus(MonitorViewModel vm)
        {
            // Try to find the machine in the machine matrix to get its status
            lock (_machineMatrixStringLock)
            {
                var machineDevice = _machineMatrixString?.FirstOrDefault(d =>
                    string.Equals(d.Item.Name, vm.MachineName, StringComparison.OrdinalIgnoreCase));

                if (machineDevice != null)
                {
                    vm.StatusBrush = machineDevice.Item.StatusBrush;
                    vm.StatusText = GetStatusTextForBrush(machineDevice.Item.StatusBrush);
                    Logger.LogInfo($"[MWB] Set {vm.Label} status from machine matrix: {vm.StatusText} (brush: {machineDevice.Item.StatusBrush?.GetHashCode()})");
                }
                else
                {
                    // Default to NA status if machine not found
                    vm.StatusBrush = StatusColors[SocketStatus.NA];
                    vm.StatusText = SocketStatus.NA.ToString();
                    Logger.LogInfo($"[MWB] Machine '{vm.MachineName}' not found in matrix (matrix has {_machineMatrixString?.Count ?? 0} items), using NA status");
                }
            }
        }

        /// <summary>
        /// Gets the text description for a status brush color.
        /// </summary>
        private string GetStatusTextForBrush(Brush brush)
        {
            foreach (var kvp in StatusColors)
            {
                if (kvp.Value == brush)
                {
                    return kvp.Key.ToString();
                }
            }

            return "Unknown";
        }

        /// <summary>
        /// Updates the StatusBrush and StatusText for all monitors belonging to a specific machine.
        /// </summary>
        private void UpdateMonitorViewModelsStatus(string machineName, Brush statusBrush, string statusText)
        {
            if (_monitorViewModels == null)
            {
                Logger.LogInfo($"[MWB] UpdateMonitorViewModelsStatus: _monitorViewModels is null, cannot update status for {machineName}");
                return;
            }

            var count = 0;
            foreach (var vm in _monitorViewModels)
            {
                if (string.Equals(vm.MachineName, machineName, StringComparison.OrdinalIgnoreCase))
                {
                    vm.StatusBrush = statusBrush;
                    vm.StatusText = statusText;
                    count++;
                    Logger.LogInfo($"[MWB] Updated {vm.Label} status to {statusText}");
                }
            }

            if (count == 0)
            {
                Logger.LogInfo($"[MWB] No monitors found for machine {machineName} to update status");
            }
        }

        /// <summary>
        /// Converts <see cref="MonitorViewModels"/> back to <see cref="MonitorLayoutInfo"/> records
        /// via <see cref="MonitorViewModel.ToLayoutInfo"/> and persists them via
        /// <see cref="SaveMonitorLayout"/>.
        /// </summary>
        public void CommitCanvasLayout()
        {
            if (_monitorViewModels == null)
            {
                return;
            }

            var layoutInfos = _monitorViewModels.Select(vm => vm.ToLayoutInfo()).ToList();
            SaveMonitorLayout(layoutInfos);
        }
    }
}
