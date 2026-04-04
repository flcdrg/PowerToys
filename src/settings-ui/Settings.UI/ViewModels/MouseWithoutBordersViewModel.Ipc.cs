// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using StreamJsonRpc;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public partial class MouseWithoutBordersViewModel
    {
        private enum SocketStatus : int
        {
            NA = 0,
            Resolving = 1,
            Connecting = 2,
            Handshaking = 3,
            Error = 4,
            ForceClosed = 5,
            InvalidKey = 6,
            Timeout = 7,
            SendError = 8,
            Connected = 9,
        }

        private interface ISettingsSyncHelper
        {
            [Newtonsoft.Json.JsonObject(Newtonsoft.Json.MemberSerialization.OptIn)]
            public struct MachineSocketState
            {
                // Disable false-positive warning due to IPC
#pragma warning disable CS0649
                [Newtonsoft.Json.JsonProperty]
                public string Name;

                [Newtonsoft.Json.JsonProperty]
                public SocketStatus Status;
#pragma warning restore CS0649
            }

            void Shutdown();

            void Reconnect();

            void GenerateNewKey();

            void ConnectToMachine(string machineName, string securityKey);

            Task<MachineSocketState[]> RequestMachineSocketStateAsync();

            Task<bool> GetUseMonitorLayoutAsync();

            void SetUseMonitorLayout(bool value);

            Task<List<MonitorLayoutInfo>> GetMonitorLayoutAsync();

            void SetMonitorLayout(List<MonitorLayoutInfo> monitors);
        }

        private static VisualStudio.Threading.AsyncSemaphore _ipcSemaphore = new VisualStudio.Threading.AsyncSemaphore(1);

        private sealed class SyncHelper : IDisposable
        {
            public SyncHelper(NamedPipeClientStream stream)
            {
                Stream = stream;
                Endpoint = JsonRpc.Attach<ISettingsSyncHelper>(Stream);
            }

            public NamedPipeClientStream Stream { get; }

            public ISettingsSyncHelper Endpoint { get; private set; }

            public void Dispose()
            {
                ((IDisposable)Endpoint).Dispose();
            }
        }

        private static NamedPipeClientStream syncHelperStream;

        private async Task<SyncHelper> GetSettingsSyncHelperAsync()
        {
            try
            {
                var recreateStream = false;
                if (syncHelperStream == null)
                {
                    recreateStream = true;
                }
                else
                {
                    if (!syncHelperStream.IsConnected || !syncHelperStream.CanWrite)
                    {
                        await syncHelperStream.DisposeAsync();
                        recreateStream = true;
                    }
                }

                if (recreateStream)
                {
                    syncHelperStream = new NamedPipeClientStream(".", "MouseWithoutBorders/SettingsSync", PipeDirection.InOut, PipeOptions.Asynchronous);
                    await syncHelperStream.ConnectAsync(10000);
                }

                return new SyncHelper(syncHelperStream);
            }
            catch (Exception ex)
            {
                // Always log IPC errors, not just when enabled
                Logger.LogInfo($"[MWB] GetSettingsSyncHelperAsync error (IsEnabled={IsEnabled}): {ex}");
                return null;
            }
        }

        public async Task SubmitShutdownRequestAsync()
        {
            using (await _ipcSemaphore.EnterAsync())
            {
                using (var syncHelper = await GetSettingsSyncHelperAsync())
                {
                    syncHelper?.Endpoint?.Shutdown();
                    var task = syncHelper?.Stream.FlushAsync();
                    if (task != null)
                    {
                        await task;
                    }
                }
            }
        }

        public async Task SubmitReconnectRequestAsync()
        {
            using (await _ipcSemaphore.EnterAsync())
            {
                using (var syncHelper = await GetSettingsSyncHelperAsync())
                {
                    syncHelper?.Endpoint?.Reconnect();
                    var task = syncHelper?.Stream.FlushAsync();
                    if (task != null)
                    {
                        await task;
                    }
                }
            }
        }

        public async Task SubmitNewKeyRequestAsync()
        {
            using (await _ipcSemaphore.EnterAsync())
            {
                using (var syncHelper = await GetSettingsSyncHelperAsync())
                {
                    syncHelper?.Endpoint?.GenerateNewKey();
                    var task = syncHelper?.Stream.FlushAsync();
                    if (task != null)
                    {
                        await task;
                    }
                }
            }
        }

        public async Task SubmitConnectionRequestAsync(string pcName, string securityKey)
        {
            using (await _ipcSemaphore.EnterAsync())
            {
                using (var syncHelper = await GetSettingsSyncHelperAsync())
                {
                    syncHelper?.Endpoint?.ConnectToMachine(pcName, securityKey);
                    var task = syncHelper?.Stream.FlushAsync();
                    if (task != null)
                    {
                        await task;
                    }
                }
            }
        }

        private async Task<ISettingsSyncHelper.MachineSocketState[]> PollMachineSocketStateAsync()
        {
            try
            {
                // Use a timeout when acquiring the IPC semaphore to prevent deadlock
                // if other operations are holding the semaphore for too long
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    var semaphoreHandle = await _ipcSemaphore.EnterAsync(timeoutCts.Token);

                    try
                    {
                        using (var syncHelper = await GetSettingsSyncHelperAsync())
                        {
                            if (syncHelper == null)
                            {
                                // Log when IPC connection fails (which is not the same as PollMachineSocketStateAsync throwing)
                                return null;
                            }

                            var task = syncHelper?.Endpoint?.RequestMachineSocketStateAsync();
                            if (task != null)
                            {
                                return await task;
                            }
                            else
                            {
                                return null;
                            }
                        }
                    }
                    finally
                    {
                        semaphoreHandle.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogInfo("[MWB] PollMachineSocketStateAsync: timeout waiting for IPC semaphore (likely another IPC operation in progress)");
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogInfo($"[MWB] PollMachineSocketStateAsync exception: {ex}");
                return null;
            }
        }

        public async Task SubmitSetUseMonitorLayoutAsync(bool value)
        {
            using (await _ipcSemaphore.EnterAsync())
            {
                using (var syncHelper = await GetSettingsSyncHelperAsync())
                {
                    syncHelper?.Endpoint?.SetUseMonitorLayout(value);
                    var task = syncHelper?.Stream.FlushAsync();
                    if (task != null)
                    {
                        await task;
                    }
                }
            }
        }

        public async Task SubmitSetMonitorLayoutAsync(List<MonitorLayoutInfo> monitors)
        {
            using (await _ipcSemaphore.EnterAsync())
            {
                using (var syncHelper = await GetSettingsSyncHelperAsync())
                {
                    syncHelper?.Endpoint?.SetMonitorLayout(monitors);
                    var task = syncHelper?.Stream.FlushAsync();
                    if (task != null)
                    {
                        await task;
                    }
                }
            }
        }

        private async Task<List<MonitorLayoutInfo>> FetchMonitorLayoutAsync()
        {
            using (await _ipcSemaphore.EnterAsync())
            {
                using (var syncHelper = await GetSettingsSyncHelperAsync())
                {
                    var task = syncHelper?.Endpoint?.GetMonitorLayoutAsync();
                    if (task != null)
                    {
                        return await task;
                    }

                    return Settings.Properties.MonitorLayout ?? new List<MonitorLayoutInfo>();
                }
            }
        }

        private async Task<List<MonitorLayoutInfo>> PollMonitorLayoutAsync()
        {
            try
            {
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                {
                    var semaphoreHandle = await _ipcSemaphore.EnterAsync(timeoutCts.Token);
                    try
                    {
                        using (var syncHelper = await GetSettingsSyncHelperAsync())
                        {
                            var task = syncHelper?.Endpoint?.GetMonitorLayoutAsync();
                            return task != null ? await task : null;
                        }
                    }
                    finally
                    {
                        semaphoreHandle.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogInfo($"[MWB] PollMonitorLayoutAsync exception: {ex}");
                return null;
            }
        }
    }
}
