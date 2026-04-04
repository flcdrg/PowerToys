// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Helpers;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public partial class MouseWithoutBordersViewModel
    {
        // Static initialization happens before any instances are created
        static MouseWithoutBordersViewModel()
        {
            Logger.LogInfo("[MWB] Static constructor of MouseWithoutBordersViewModel starting");
            try
            {
                // Verify StatusColors dictionary is initialized
                var colorCount = StatusColors.Count;
                Logger.LogInfo($"[MWB] Static constructor: StatusColors has {colorCount} entries");
            }
            catch (Exception ex)
            {
                Logger.LogInfo($"[MWB] Static constructor error: {ex}");
            }
        }

        private readonly Lock _machineMatrixStringLock = new();

        private static readonly Dictionary<SocketStatus, Brush> StatusColors = new()
        {
            { SocketStatus.NA, CreateStatusBrush(ColorHelper.FromArgb(0, 0x71, 0x71, 0x71)) },
            { SocketStatus.Resolving, CreateStatusBrush(Colors.Yellow) },
            { SocketStatus.Connecting, CreateStatusBrush(Colors.Orange) },
            { SocketStatus.Handshaking, CreateStatusBrush(Colors.Blue) },
            { SocketStatus.Error, CreateStatusBrush(Colors.Red) },
            { SocketStatus.ForceClosed, CreateStatusBrush(Colors.Purple) },
            { SocketStatus.InvalidKey, CreateStatusBrush(Colors.Brown) },
            { SocketStatus.Timeout, CreateStatusBrush(Colors.Pink) },
            { SocketStatus.SendError, CreateStatusBrush(Colors.Maroon) },
            { SocketStatus.Connected, CreateStatusBrush(Colors.Green) },
        };

        private static Brush CreateStatusBrush(Color color)
        {
            try
            {
                return new SolidColorBrush(color);
            }
            catch (Exception ex)
            {
                Logger.LogInfo($"Failed to create status brush, falling back to null: {ex.GetType().Name}");
                return null;
            }
        }

        private CancellationTokenSource _cancellationTokenSource;

        private Task _machinePollingThreadTask;

        private Task StartMachineStatusPollingThread(Task previousThreadTask, CancellationToken token)
        {
            string diagnosticMsg = "[MWB] StartMachineStatusPollingThread method called - START";
            Logger.LogInfo(diagnosticMsg);

            // Fallback: write directly to a file if logging fails
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        @"Microsoft\PowerToys\MouseWithoutBorders\diagnostic.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {diagnosticMsg}{Environment.NewLine}");
            }
            catch
            {
                // Swallow exceptions from fallback logging
            }

            Logger.LogInfo($"[MWB] previousThreadTask is {(previousThreadTask == null ? "null" : "not null")}");
            Logger.LogInfo($"[MWB] token IsCancellationRequested: {token.IsCancellationRequested}");

            try
            {
                var pollingTask = Task.Run(
                    async () =>
                    {
                        string threadMsg = "[MWB] Polling thread started - ASYNC LAMBDA EXECUTING";
                        Logger.LogInfo(threadMsg);

                        // Fallback logging
                        try
                        {
                            System.IO.File.AppendAllText(
                                System.IO.Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    @"Microsoft\PowerToys\MouseWithoutBorders\diagnostic.log"),
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {threadMsg}{Environment.NewLine}");
                        }
                        catch
                        {
                            // Swallow exceptions from fallback logging
                        }

                        // Wait for previous polling thread to complete (if one exists)
                        if (previousThreadTask != null)
                        {
                            Logger.LogInfo("[MWB] Polling thread: waiting for previous thread to complete");
                            try
                            {
                                await previousThreadTask.WaitAsync(token);
                            }
                            catch (OperationCanceledException)
                            {
                                Logger.LogInfo("[MWB] Polling thread: previous thread wait was cancelled");
                            }
                        }

                        Logger.LogInfo("[MWB] Polling thread: past await previousThreadTask, entering poll loop");

                        int pollCount = 0;
                        while (!token.IsCancellationRequested)
                        {
                            Dictionary<string, ISettingsSyncHelper.MachineSocketState> states;
                            try
                            {
                                if (pollCount % 20 == 0)
                                {
                                    Logger.LogInfo($"[MWB] Polling thread: iteration {pollCount + 1}, about to call PollMachineSocketStateAsync");
                                }

                                states = (await PollMachineSocketStateAsync())
                                    ?.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                                pollCount++;
                                if (pollCount % 10 == 0)
                                {
                                    // Log every 10th poll to avoid log spam
                                    Logger.LogInfo($"[MWB] Polling thread: poll #{pollCount}, got {states?.Count ?? 0} states");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogInfo($"Poll ISettingsSyncHelper.MachineSocketState error: {ex}");
                                continue;
                            }

                            if (states != null && states.Count > 0)
                            {
                                lock (_machineMatrixStringLock)
                                {
                                    if (_machineMatrixString == null || _machineMatrixString.Count == 0)
                                    {
                                        Logger.LogInfo($"[MWB] Polling thread: _machineMatrixString is null or empty, cannot update status");
                                        continue;
                                    }

                                    foreach (var machine in _machineMatrixString)
                                    {
                                        if (states.TryGetValue(machine.Item.Name, out var state))
                                        {
                                            _uiDispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
                                            {
                                                try
                                                {
                                                    machine.Item.StatusBrush = StatusColors[state.Status];

                                                    // Also update monitor view models with the new status
                                                    UpdateMonitorViewModelsStatus(machine.Item.Name, StatusColors[state.Status], state.Status.ToString());
                                                }
                                                catch (Exception ex)
                                                {
                                                    Logger.LogInfo($"[MWB] Error updating status for machine {machine.Item.Name}: {ex}");
                                                }
                                            });
                                        }
                                    }
                                }
                            }

                            // Every ~4 seconds, check for monitor layout updates pushed from peers.
                            if (pollCount % 8 == 0)
                            {
                                try
                                {
                                    var freshLayout = await PollMonitorLayoutAsync();
                                    if (freshLayout is { Count: > 0 })
                                    {
                                        var snapshot = _monitorLayouts;
                                        if (!MonitorLayoutsEquivalent(freshLayout, snapshot))
                                        {
                                            _uiDispatcherQueue.TryEnqueue(() =>
                                            {
                                                _monitorLayouts = new ObservableCollection<MonitorLayoutInfo>(freshLayout);
                                                RebuildMonitorViewModels();
                                            });
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logger.LogInfo($"[MWB] Periodic layout refresh error: {ex}");
                                }
                            }

                            Thread.Sleep(500);
                        }

                        Logger.LogInfo("[MWB] Polling thread terminated");
                    },
                    token);

                Logger.LogInfo("[MWB] Task.Run returned pollingTask - SCHEDULER ACCEPTED");
                return pollingTask;
            }
            catch (Exception ex)
            {
                string errMsg = $"[MWB] StartMachineStatusPollingThread EXCEPTION: {ex}";
                Logger.LogInfo(errMsg);

                // Fallback logging
                try
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            @"Microsoft\PowerToys\MouseWithoutBorders\diagnostic.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {errMsg}{Environment.NewLine}");
                }
                catch
                {
                    // Swallow exceptions from fallback logging
                }

                throw;
            }
        }

        // Loads the machine matrix, taking into account changes to the machine pool.
        private void LoadMachineMatrixString()
        {
            List<string> loadMachineMatrixString = Settings.Properties.MachineMatrixString ??
                                                   [string.Empty, string.Empty, string.Empty, string.Empty];

            if (loadMachineMatrixString.Count < 4)
            {
                // Current logic of MWB assumes there are always 4 slots. Any other configuration means data corruption here.
                loadMachineMatrixString = [string.Empty, string.Empty, string.Empty, string.Empty];
            }

            bool editedTheMatrix = false; // keep track of changes to the matrix because of changes to the available machine pool.

            if (!string.IsNullOrEmpty(Settings.Properties.MachinePool?.Value))
            {
                List<string> availableMachines = new List<string>();

                // Format of this field is "NAME1:ID1,NAME2:ID2,..."
                // Load the available machines
                foreach (string availableMachineIdPair in Settings.Properties.MachinePool.Value.Split(","))
                {
                    string availableMachineName = availableMachineIdPair.Split(':')[0];
                    availableMachines.Add(availableMachineName);
                }

                // Start by removing the machines from the matrix that are no longer available to pick.
                for (int i = 0; i < loadMachineMatrixString.Count; i++)
                {
                    if (!availableMachines.Contains(loadMachineMatrixString[i]))
                    {
                        editedTheMatrix = true;
                        loadMachineMatrixString[i] = string.Empty;
                    }
                }

                // If an available machine is not in the matrix already, fill it in the first available spot.
                foreach (string availableMachineName in availableMachines)
                {
                    if (!loadMachineMatrixString.Contains(availableMachineName))
                    {
                        int availableIndex = loadMachineMatrixString.FindIndex(name => string.IsNullOrEmpty(name));
                        if (availableIndex >= 0)
                        {
                            loadMachineMatrixString[availableIndex] = availableMachineName;
                            editedTheMatrix = true;
                        }
                    }
                }
            }

            // Dragging while elevated crashes on WinUI3: https://github.com/microsoft/microsoft-ui-xaml/issues/7690
            _machineMatrixString = new IndexedObservableCollection<DeviceViewModel>(loadMachineMatrixString.Select(name => new DeviceViewModel { Name = name, CanDragDrop = !IsElevated }));

            if (editedTheMatrix)
            {
                // Set the property directly to save the new matrix right away with the new available machines.
                MachineMatrixString = _machineMatrixString;
            }
        }

        private IndexedObservableCollection<DeviceViewModel> _machineMatrixString;

        public class DeviceViewModel : Observable
        {
            public string Name { get; set; }

            public bool CanDragDrop { get; set; }

            private Brush _statusBrush = StatusColors[SocketStatus.NA];

            public Brush StatusBrush
            {
                get
                {
                    return _statusBrush;
                }

                set
                {
                    if (_statusBrush != value)
                    {
                        _statusBrush = value;
                        OnPropertyChanged();
                    }
                }
            }
        }

        public IndexedObservableCollection<DeviceViewModel> MachineMatrixString
        {
            get
            {
                lock (_machineMatrixStringLock)
                {
                    return _machineMatrixString;
                }
            }

            set
            {
                lock (_machineMatrixStringLock)
                {
                    _machineMatrixString = value;
                }

                Settings.Properties.MachineMatrixString = new List<string>(value.ToEnumerable().Select(d => d.Name));
                NotifyPropertyChanged();
            }
        }
    }
}
