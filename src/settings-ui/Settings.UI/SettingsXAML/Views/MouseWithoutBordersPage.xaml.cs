// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Helpers;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Utilities;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using WinRT;

using static Microsoft.PowerToys.Settings.UI.ViewModels.MouseWithoutBordersViewModel;

namespace Microsoft.PowerToys.Settings.UI.Views
{
    public sealed partial class MouseWithoutBordersPage : NavigablePage, IRefreshablePage
    {
        private const string MouseWithoutBordersDragDropCheckString = "MWB Device Drag Drop";

        private const string PowerToyName = "MouseWithoutBorders";

        private MouseWithoutBordersViewModel ViewModel { get; set; }

        private readonly IFileSystemWatcher watcher;

        // Proximity highlight: one Path element per machine group whose geometry is the
        // union of all that machine's monitor rectangles.  Shown when the dragged group
        // moves within proximity; hidden on release.
        private readonly Dictionary<string, Path> _groupHighlightPaths = new();

        // Monitor canvas drag state – all monitors of the same machine are moved as one group.
        private List<MonitorViewModel> _draggingGroup;
        private MonitorViewModel _draggingPressedVm; // the specific tile the pointer landed on
        private uint _dragPointerId;
        private string _highlightedMachine;

        public MouseWithoutBordersPage()
        {
            Logger.LogInfo("[MWB-PAGE-INIT] MouseWithoutBordersPage constructor called - START OF INIT");

            var settingsUtils = SettingsUtils.Default;
            Logger.LogInfo("[MWB-PAGE-INIT] SettingsUtils acquired");

            ViewModel = new MouseWithoutBordersViewModel(
                settingsUtils,
                SettingsRepository<GeneralSettings>.GetInstance(settingsUtils),
                ShellPage.SendDefaultIPCMessage,
                DispatcherQueue);

            Logger.LogInfo("[MWB-PAGE-INIT] ViewModel created");

            watcher = Helper.GetFileWatcher(
                PowerToyName,
                "settings.json",
                OnConfigFileUpdate);

            DataContext = ViewModel;
            InitializeComponent();

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;

            Loaded += (s, e) =>
            {
                Logger.LogInfo("[MWB] Page Loaded event fired");
                ViewModel.OnPageLoaded();
                RebuildMonitorCanvas();
            };

            Logger.LogInfo("[MWB-PAGE-INIT] MouseWithoutBordersPage constructor completed - END OF INIT");
        }

        private void OnConfigFileUpdate()
        {
            // Note: FileSystemWatcher raise notification multiple times for single update operation.
            // Todo: Handle duplicate events either by somehow suppress them or re-read the configuration every time since we will be updating the UI only if something is changed.
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (ViewModel.LoadUpdatedSettings())
                {
                    ViewModel.NotifyUpdatedSettings();
                }
            });
        }

        private static T GetChildOfType<T>(DependencyObject depObj, string tag)
            where T : FrameworkElement
        {
            if (depObj == null)
            {
                return null;
            }

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);

                var result = (child as T) ?? GetChildOfType<T>(child, tag);
                if (result != null && (string)result.Tag == tag)
                {
                    return result;
                }
            }

            return null;
        }

        private int GetDeviceIndex(Border b)
        {
            return b.DataContext.As<IndexedItem<DeviceViewModel>>().Index;
        }

        private void Device_DragStarting(UIElement sender, DragStartingEventArgs args)
        {
            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.Properties.Add("check-usage", MouseWithoutBordersDragDropCheckString);
            args.Data.Properties.Add("index", GetDeviceIndex((Border)sender));
        }

        private void Device_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Properties.TryGetValue("check-usage", out object checkUsage))
            {
                // Guard against values dragged from somewhere else
                if (!((string)checkUsage).Equals(MouseWithoutBordersDragDropCheckString, StringComparison.Ordinal))
                {
                    return;
                }
            }
            else
            {
                return;
            }

            if (!e.DataView.Properties.TryGetValue("index", out object boxIndex))
            {
                return;
            }

            var draggedDeviceIndex = (int)boxIndex;

            if (draggedDeviceIndex < 0 || draggedDeviceIndex >= ViewModel.MachineMatrixString.Count)
            {
                return;
            }

            var targetDeviceIndex = GetDeviceIndex((Border)e.OriginalSource);

            ViewModel.MachineMatrixString.Swap(draggedDeviceIndex, targetDeviceIndex);
            var itemsControl = (ItemsControl)FindName("DevicesItemsControl");
            var binding = itemsControl.GetBindingExpression(ItemsControl.ItemsSourceProperty);
            binding.UpdateSource();
        }

        private void Device_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }

        public ICommand ShowConnectFieldsCommand => new RelayCommand(ShowConnectFields);

        public ICommand ConnectCommand => new AsyncCommand(Connect);

        public ICommand GenerateNewKeyCommand => new AsyncCommand(ViewModel.SubmitNewKeyRequestAsync);

        public ICommand CopyPCNameCommand => new RelayCommand(ViewModel.CopyMachineNameToClipboard);

        public ICommand ReconnectCommand => new AsyncCommand(ViewModel.SubmitReconnectRequestAsync);

        private void ShowConnectFields()
        {
            ViewModel.ConnectFieldsVisible = true;
        }

        private async Task Connect()
        {
            if (ConnectPCNameTextBox.Text.Length != 0 && ConnectSecurityKeyTextBox.Text.Length != 0)
            {
                string pcName = ConnectPCNameTextBox.Text;
                string securityKey = ConnectSecurityKeyTextBox.Text.Trim();

                await ViewModel.SubmitConnectionRequestAsync(pcName, securityKey);

                ConnectPCNameTextBox.Text = string.Empty;
                ConnectSecurityKeyTextBox.Text = string.Empty;
            }
        }

        public void RefreshEnabledState()
        {
            ViewModel.RefreshEnabledState();
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Rebuild monitor canvas when the layout or use-monitor-layout setting changes
            if (e.PropertyName == nameof(ViewModel.MonitorViewModels) ||
                e.PropertyName == nameof(ViewModel.UseMonitorLayout))
            {
                Logger.LogInfo($"[MWB] ViewModel_PropertyChanged: {e.PropertyName}, dispatching RebuildMonitorCanvas");
                DispatcherQueue.TryEnqueue(RebuildMonitorCanvas);
            }
        }

        private void RebuildMonitorCanvas()
        {
            if (MonitorCanvas == null)
            {
                return;
            }

            Logger.LogInfo($"[MWB] RebuildMonitorCanvas called, UseMonitorLayout={ViewModel.UseMonitorLayout}, MonitorViewModels.Count={ViewModel.MonitorViewModels?.Count ?? 0}");

            // Detach old pointer handlers and clear
            foreach (var child in MonitorCanvas.Children)
            {
                if (child is Border oldBorder)
                {
                    oldBorder.PointerPressed -= MonitorTile_PointerPressed;
                    oldBorder.PointerMoved -= MonitorTile_PointerMoved;
                    oldBorder.PointerReleased -= MonitorTile_PointerReleased;
                    oldBorder.PointerCanceled -= MonitorTile_PointerCanceled;
                }
            }

            MonitorCanvas.Children.Clear();
            _draggingGroup = null;
            _draggingPressedVm = null;
            _groupHighlightPaths.Clear();
            _highlightedMachine = null;

            if (!ViewModel.UseMonitorLayout || ViewModel.MonitorViewModels == null)
            {
                return;
            }

            foreach (var vm in ViewModel.MonitorViewModels)
            {
                var textBlock = new TextBlock
                {
                    Text = vm.Label,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"],
                };

                // Determine which of this tile's four edges are internal – i.e. shared with
                // another monitor that belongs to the same machine. Internal edges get a zero
                // border thickness so that only the outer perimeter of the group is visible.
                // The group may not be rectangular (e.g. an L-shape), so each side is checked
                // independently by testing whether any sibling monitor is flush against it and
                // overlaps it along the perpendicular axis.
                var siblings = ViewModel.MonitorViewModels
                    .Where(m => m != vm && string.Equals(m.MachineName, vm.MachineName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                const double b = 2.0;
                const double r = 4.0;

                // Allow up to 20 canvas pixels of gap between same-machine siblings before
                // treating an edge as "external". This tolerates stored layouts where the
                // physical pixel positions of adjacent monitors leave a small gap due to DPI
                // scaling or rounding.
                const double sameMachineBorderTolerance = 20.0;

                bool nborTop = siblings.Any(o =>
                    Math.Abs((o.CanvasTop + o.CanvasHeight) - vm.CanvasTop) < sameMachineBorderTolerance &&
                    o.CanvasLeft < vm.CanvasLeft + vm.CanvasWidth &&
                    o.CanvasLeft + o.CanvasWidth > vm.CanvasLeft);
                bool nborBottom = siblings.Any(o =>
                    Math.Abs(o.CanvasTop - (vm.CanvasTop + vm.CanvasHeight)) < sameMachineBorderTolerance &&
                    o.CanvasLeft < vm.CanvasLeft + vm.CanvasWidth &&
                    o.CanvasLeft + o.CanvasWidth > vm.CanvasLeft);
                bool nborLeft = siblings.Any(o =>
                    Math.Abs((o.CanvasLeft + o.CanvasWidth) - vm.CanvasLeft) < sameMachineBorderTolerance &&
                    o.CanvasTop < vm.CanvasTop + vm.CanvasHeight &&
                    o.CanvasTop + o.CanvasHeight > vm.CanvasTop);
                bool nborRight = siblings.Any(o =>
                    Math.Abs(o.CanvasLeft - (vm.CanvasLeft + vm.CanvasWidth)) < sameMachineBorderTolerance &&
                    o.CanvasTop < vm.CanvasTop + vm.CanvasHeight &&
                    o.CanvasTop + o.CanvasHeight > vm.CanvasTop);

                var border = new Border
                {
                    Width = vm.CanvasWidth,
                    Height = vm.CanvasHeight,
                    Tag = vm,
                    Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                    BorderBrush = vm.StatusBrush ?? (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(
                        nborLeft ? 0 : b,    // left
                        nborTop ? 0 : b,     // top
                        nborRight ? 0 : b,   // right
                        nborBottom ? 0 : b), // bottom
                    CornerRadius = new CornerRadius(
                        nborTop || nborLeft ? 0 : r,     // top-left
                        nborTop || nborRight ? 0 : r,    // top-right
                        nborBottom || nborRight ? 0 : r, // bottom-right
                        nborBottom || nborLeft ? 0 : r), // bottom-left
                    Child = textBlock,
                };

                Logger.LogInfo($"[MWB] Created border for {vm.Label}: StatusBrush={vm.StatusBrush?.GetHashCode() ?? 0}, BorderThickness.Left={border.BorderThickness.Left}, BorderThickness.Right={border.BorderThickness.Right}");

                AutomationProperties.SetName(border, vm.Label);
                Canvas.SetLeft(border, vm.CanvasLeft);
                Canvas.SetTop(border, vm.CanvasTop);

                // Set tooltip: local machine monitors show "This machine", others show connection status
                bool isLocalMonitor = string.Equals(vm.MachineName, ViewModel.MachineHostName, StringComparison.OrdinalIgnoreCase);
                ToolTipService.SetToolTip(border, isLocalMonitor ? "This machine" : (vm.StatusText ?? vm.Label));

                border.PointerPressed += MonitorTile_PointerPressed;
                border.PointerMoved += MonitorTile_PointerMoved;
                border.PointerReleased += MonitorTile_PointerReleased;
                border.PointerCanceled += MonitorTile_PointerCanceled;

                // Keep canvas position in sync when ViewModel updates (e.g. during drag)
                vm.PropertyChanged += (s, e) =>
                {
                    if (s is not MonitorViewModel monVm)
                    {
                        return;
                    }

                    if (e.PropertyName == nameof(MonitorViewModel.CanvasLeft))
                    {
                        Canvas.SetLeft(border, monVm.CanvasLeft);
                    }
                    else if (e.PropertyName == nameof(MonitorViewModel.CanvasTop))
                    {
                        Canvas.SetTop(border, monVm.CanvasTop);
                    }
                    else if (e.PropertyName == nameof(MonitorViewModel.CanvasWidth))
                    {
                        border.Width = monVm.CanvasWidth;
                    }
                    else if (e.PropertyName == nameof(MonitorViewModel.CanvasHeight))
                    {
                        border.Height = monVm.CanvasHeight;
                    }
                    else if (e.PropertyName == nameof(MonitorViewModel.IsSelected))
                    {
                        border.Opacity = monVm.IsSelected ? 0.75 : 1.0;
                    }
                    else if (e.PropertyName == nameof(MonitorViewModel.StatusBrush))
                    {
                        border.BorderBrush = monVm.StatusBrush ?? (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
                    }
                    else if (e.PropertyName == nameof(MonitorViewModel.StatusText))
                    {
                        bool isLocal = string.Equals(monVm.MachineName, ViewModel.MachineHostName, StringComparison.OrdinalIgnoreCase);
                        ToolTipService.SetToolTip(border, isLocal ? "This machine" : (monVm.StatusText ?? monVm.Label));
                    }
                };

                MonitorCanvas.Children.Add(border);
            }

            // Second pass: one highlight Path per machine group.
            // CombinedGeometry.Union merges all monitor rectangles into a single shape;
            // monitors are inflated slightly before union so that near-adjacent monitors
            // (small gap due to different resolutions or scaling) merge into one region.
            // Each Path is added after all regular tiles so it renders on top.
            var machineNames = ViewModel.MonitorViewModels
                .Select(m => m.MachineName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var machineName in machineNames)
            {
                var groupVms = ViewModel.MonitorViewModels
                    .Where(m => string.Equals(m.MachineName, machineName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var highlightPath = new Path
                {
                    Data = BuildGroupHighlightGeometry(groupVms),
                    Stroke = new SolidColorBrush(Colors.Yellow),
                    StrokeThickness = 3,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                };

                MonitorCanvas.Children.Add(highlightPath);
                _groupHighlightPaths[machineName] = highlightPath;
            }

            Logger.LogInfo($"[MWB] RebuildMonitorCanvas complete: {MonitorCanvas.Children.Count} total children added ({ViewModel.MonitorViewModels.Count} borders + {machineNames.Count} highlight paths)");
        }

        private void MonitorTile_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Border border || border.Tag is not MonitorViewModel vm)
            {
                return;
            }

            // Collect every monitor that belongs to the same machine so they all move together.
            _draggingGroup = ViewModel.MonitorViewModels
                .Where(m => string.Equals(m.MachineName, vm.MachineName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _draggingPressedVm = vm;
            _dragPointerId = e.Pointer.PointerId;

            var pos = e.GetCurrentPoint(MonitorCanvas).Position;
            vm.DragOffsetX = pos.X - vm.CanvasLeft;
            vm.DragOffsetY = pos.Y - vm.CanvasTop;

            foreach (var groupVm in _draggingGroup)
            {
                groupVm.IsDragging = true;
                groupVm.IsSelected = true;
            }

            border.CapturePointer(e.Pointer);
            RebuildHighlightPaths();
            UpdateProximityHighlight();
            e.Handled = true;
        }

        private void MonitorTile_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_draggingPressedVm == null || _draggingGroup == null ||
                !_draggingPressedVm.IsDragging || e.Pointer.PointerId != _dragPointerId)
            {
                return;
            }

            var pos = e.GetCurrentPoint(MonitorCanvas).Position;

            // Where the pressed tile's top-left would land based on cursor position.
            double pressedDesiredLeft = pos.X - _draggingPressedVm.DragOffsetX;
            double pressedDesiredTop = pos.Y - _draggingPressedVm.DragOffsetY;

            // Compute the movement delta relative to the pressed tile's current position.
            double deltaX = pressedDesiredLeft - _draggingPressedVm.CanvasLeft;
            double deltaY = pressedDesiredTop - _draggingPressedVm.CanvasTop;

            // Compute the group's combined bounding box BEFORE the move.
            double groupLeft = _draggingGroup.Min(m => m.CanvasLeft);
            double groupTop = _draggingGroup.Min(m => m.CanvasTop);
            double groupRight = _draggingGroup.Max(m => m.CanvasLeft + m.CanvasWidth);
            double groupBottom = _draggingGroup.Max(m => m.CanvasTop + m.CanvasHeight);
            double groupW = groupRight - groupLeft;
            double groupH = groupBottom - groupTop;

            // Clamp the delta so the entire group stays within the canvas.
            double clampedGroupLeft = Math.Max(0, Math.Min(groupLeft + deltaX, MonitorCanvas.ActualWidth - groupW));
            double clampedGroupTop = Math.Max(0, Math.Min(groupTop + deltaY, MonitorCanvas.ActualHeight - groupH));
            double clampedDeltaX = clampedGroupLeft - groupLeft;
            double clampedDeltaY = clampedGroupTop - groupTop;

            foreach (var groupVm in _draggingGroup)
            {
                groupVm.UpdatePosition(groupVm.CanvasLeft + clampedDeltaX, groupVm.CanvasTop + clampedDeltaY);
            }

            UpdateProximityHighlight();

            e.Handled = true;
        }

        private void MonitorTile_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndDrag(sender, e);
        }

        private void MonitorTile_PointerCanceled(object sender, PointerRoutedEventArgs e)
        {
            EndDrag(sender, e);
        }

        private void EndDrag(object sender, PointerRoutedEventArgs e)
        {
            if (_draggingGroup != null)
            {
                foreach (var groupVm in _draggingGroup)
                {
                    groupVm.IsDragging = false;
                }

                // Snap the released group so it lands on an outer edge of another machine group
                // rather than floating inside or between another machine's monitors.
                HideAllHighlights();
                SnapGroupToOuterEdges(_draggingGroup);

                _draggingGroup = null;
            }

            _draggingPressedVm = null;

            if (sender is Border border)
            {
                border.ReleasePointerCapture(e.Pointer);
            }

            e.Handled = true;
        }

        /// <summary>
        /// Builds a <see cref="PathGeometry"/> that is the outer-perimeter outline of the
        /// union of all monitor rectangles in <paramref name="monitors"/>.
        /// <para>
        /// Each rectangle is inflated by <paramref name="inflate"/> canvas pixels before the
        /// union so that monitors separated by a small gap (e.g. different resolutions that
        /// produce sub-pixel offsets after scaling) merge into one connected region.
        /// </para>
        /// <para>
        /// The algorithm builds a grid from the unique X / Y coordinates of the inflated
        /// rectangles, classifies each grid cell as inside or outside, then collects directed
        /// boundary segments and traces them into closed <see cref="PathFigure"/> elements.
        /// </para>
        /// </summary>
        private static Geometry BuildGroupHighlightGeometry(IList<MonitorViewModel> monitors, double inflate = 8.0)
        {
            if (monitors.Count == 0)
            {
                return new PathGeometry();
            }

            // Inflate every monitor rectangle to bridge small inter-monitor gaps.
            var rects = monitors.Select(m => (
                L: m.CanvasLeft - inflate,
                T: m.CanvasTop - inflate,
                R: (m.CanvasLeft + m.CanvasWidth) + inflate,
                B: (m.CanvasTop + m.CanvasHeight) + inflate)).ToList();

            // Unique, sorted X and Y coordinates define the grid.
            var xs = rects.SelectMany(r => new[] { r.L, r.R }).Distinct().OrderBy(v => v).ToList();
            var ys = rects.SelectMany(r => new[] { r.T, r.B }).Distinct().OrderBy(v => v).ToList();
            int nx = xs.Count - 1;
            int ny = ys.Count - 1;

            if (nx <= 0 || ny <= 0)
            {
                return new PathGeometry();
            }

            // For each grid cell, determine whether it is covered by at least one rectangle.
            bool[,] inside = new bool[nx, ny];
            for (int xi = 0; xi < nx; xi++)
            {
                double cx = (xs[xi] + xs[xi + 1]) * 0.5;
                for (int yi = 0; yi < ny; yi++)
                {
                    double cy = (ys[yi] + ys[yi + 1]) * 0.5;
                    inside[xi, yi] = rects.Any(r => cx > r.L && cx < r.R && cy > r.T && cy < r.B);
                }
            }

            // Collect directed boundary segments. Each segment goes from (x1,y1) to (x2,y2)
            // with the exterior region to the right of the direction of travel.
            var edgeMap = new Dictionary<(double X, double Y), (double X, double Y)>();
            void AddEdge(double x1, double y1, double x2, double y2) => edgeMap[(x1, y1)] = (x2, y2);

            // Horizontal boundaries (top/bottom of column strips).
            for (int xi = 0; xi < nx; xi++)
            {
                if (inside[xi, 0])
                {
                    AddEdge(xs[xi], ys[0], xs[xi + 1], ys[0]);      // top outer edge: left→right
                }

                if (inside[xi, ny - 1])
                {
                    AddEdge(xs[xi + 1], ys[ny], xs[xi], ys[ny]);    // bottom outer edge: right→left
                }

                for (int yi = 0; yi < ny - 1; yi++)
                {
                    bool above = inside[xi, yi];
                    bool below = inside[xi, yi + 1];
                    if (above && !below)
                    {
                        AddEdge(xs[xi + 1], ys[yi + 1], xs[xi], ys[yi + 1]); // inner top-of-below
                    }
                    else if (!above && below)
                    {
                        AddEdge(xs[xi], ys[yi + 1], xs[xi + 1], ys[yi + 1]); // inner bottom-of-above
                    }
                }
            }

            // Vertical boundaries (left/right of row strips).
            for (int yi = 0; yi < ny; yi++)
            {
                if (inside[0, yi])
                {
                    AddEdge(xs[0], ys[yi + 1], xs[0], ys[yi]);      // left outer edge: bottom→top
                }

                if (inside[nx - 1, yi])
                {
                    AddEdge(xs[nx], ys[yi], xs[nx], ys[yi + 1]);    // right outer edge: top→bottom
                }

                for (int xi = 0; xi < nx - 1; xi++)
                {
                    bool left = inside[xi, yi];
                    bool right = inside[xi + 1, yi];
                    if (left && !right)
                    {
                        AddEdge(xs[xi + 1], ys[yi], xs[xi + 1], ys[yi + 1]); // inner right boundary: top→bottom
                    }
                    else if (!left && right)
                    {
                        AddEdge(xs[xi + 1], ys[yi + 1], xs[xi + 1], ys[yi]); // inner left boundary: bottom→top
                    }
                }
            }

            // Trace closed polygons from the directed edge map.
            var pathGeometry = new PathGeometry();
            var visited = new HashSet<(double X, double Y)>();

            foreach (var startKey in edgeMap.Keys.ToList())
            {
                if (visited.Contains(startKey))
                {
                    continue;
                }

                var figure = new PathFigure { IsClosed = true };
                var current = startKey;
                bool first = true;

                while (edgeMap.ContainsKey(current) && !visited.Contains(current))
                {
                    if (first)
                    {
                        figure.StartPoint = new Point(current.X, current.Y);
                        first = false;
                    }

                    visited.Add(current);
                    var next = edgeMap[current];
                    figure.Segments.Add(new LineSegment { Point = new Point(next.X, next.Y) });
                    current = next;
                }

                if (!first)
                {
                    pathGeometry.Figures.Add(figure);
                }
            }

            return pathGeometry;
        }

        /// <summary>
        /// Refreshes the geometry of every highlight path to the current canvas positions.
        /// Called at the start of each drag so that positions changed by a previous drag-and-snap
        /// are reflected correctly.
        /// </summary>
        private void RebuildHighlightPaths()
        {
            foreach (var (machineName, path) in _groupHighlightPaths)
            {
                var groupVms = ViewModel.MonitorViewModels
                    .Where(m => string.Equals(m.MachineName, machineName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (groupVms.Count > 0)
                {
                    path.Data = BuildGroupHighlightGeometry(groupVms);
                }
            }
        }

        /// <summary>
        /// closest to the dragged group and show its yellow outline when within the proximity
        /// threshold. Hides the outline when the dragged group moves away.
        /// </summary>
        private void UpdateProximityHighlight()
        {
            if (_draggingGroup == null || _groupHighlightPaths.Count == 0)
            {
                HideAllHighlights();
                return;
            }

            string draggedMachine = _draggingGroup[0].MachineName;

            double dragLeft = _draggingGroup.Min(m => m.CanvasLeft);
            double dragTop = _draggingGroup.Min(m => m.CanvasTop);
            double dragRight = _draggingGroup.Max(m => m.CanvasLeft + m.CanvasWidth);
            double dragBottom = _draggingGroup.Max(m => m.CanvasTop + m.CanvasHeight);

            const double proximityThreshold = 120.0;

            string nearestMachine = null;
            double nearestDist = double.MaxValue;

            foreach (var (machineName, _) in _groupHighlightPaths)
            {
                if (string.Equals(machineName, draggedMachine, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var groupVms = ViewModel.MonitorViewModels
                    .Where(m => string.Equals(m.MachineName, machineName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                double gLeft = groupVms.Min(m => m.CanvasLeft);
                double gTop = groupVms.Min(m => m.CanvasTop);
                double gRight = groupVms.Max(m => m.CanvasLeft + m.CanvasWidth);
                double gBottom = groupVms.Max(m => m.CanvasTop + m.CanvasHeight);

                // Minimum axis-aligned distance between the two bounding boxes.
                double dx = Math.Max(0, Math.Max(gLeft - dragRight, dragLeft - gRight));
                double dy = Math.Max(0, Math.Max(gTop - dragBottom, dragTop - gBottom));
                double dist = dx + dy;

                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestMachine = machineName;
                }
            }

            if (nearestMachine != null && nearestDist <= proximityThreshold)
            {
                ShowHighlight(nearestMachine);
            }
            else
            {
                HideAllHighlights();
            }
        }

        private void ShowHighlight(string machineName)
        {
            if (_highlightedMachine == machineName)
            {
                return;
            }

            HideAllHighlights();
            _highlightedMachine = machineName;

            if (_groupHighlightPaths.TryGetValue(machineName, out var path))
            {
                path.Visibility = Visibility.Visible;
            }
        }

        private void HideAllHighlights()
        {
            if (_highlightedMachine == null)
            {
                return;
            }

            foreach (var (_, path) in _groupHighlightPaths)
            {
                path.Visibility = Visibility.Collapsed;
            }

            _highlightedMachine = null;
        }

        /// <summary>
        /// it overlaps with so that it lands flush against the nearest outer edge.
        /// Iterates until no overlaps remain or the canvas boundary prevents further movement.
        /// </summary>
        private void SnapGroupToOuterEdges(List<MonitorViewModel> group)
        {
            if (group == null || group.Count == 0 || ViewModel.MonitorViewModels == null)
            {
                return;
            }

            string draggedMachine = group[0].MachineName;

            // Each individual monitor from other machines is its own snap target.
            // Using per-machine bounding boxes here caused DELLA to snap to the bottom
            // of the ENTIRE Delphinium group (below Monitor 2) even when the user was
            // trying to place it just below Monitor 3 or Monitor 4, because the combined
            // bounding box's bottom is at the lowest monitor of the group.
            var otherBounds = ViewModel.MonitorViewModels
                .Where(m => !string.Equals(m.MachineName, draggedMachine, StringComparison.OrdinalIgnoreCase))
                .Select(m => (
                    Left: m.CanvasLeft,
                    Top: m.CanvasTop,
                    Right: m.CanvasLeft + m.CanvasWidth,
                    Bottom: m.CanvasTop + m.CanvasHeight))
                .ToList();

            if (otherBounds.Count == 0)
            {
                return;
            }

            // Iteratively resolve overlaps. One pass handles most real-world layouts;
            // additional passes catch cases where fixing one overlap creates another.
            const int maxIterations = 8;
            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                double dragLeft = group.Min(m => m.CanvasLeft);
                double dragTop = group.Min(m => m.CanvasTop);
                double dragRight = group.Max(m => m.CanvasLeft + m.CanvasWidth);
                double dragBottom = group.Max(m => m.CanvasTop + m.CanvasHeight);
                double dragW = dragRight - dragLeft;
                double dragH = dragBottom - dragTop;

                bool anyOverlap = false;

                foreach (var t in otherBounds)
                {
                    // No overlap with this target – skip.
                    if (dragLeft >= t.Right || dragRight <= t.Left ||
                        dragTop >= t.Bottom || dragBottom <= t.Top)
                    {
                        continue;
                    }

                    anyOverlap = true;

                    // Four candidate positions, one per outer edge of the target group.
                    // Each places the dragged group flush against that edge, non-overlapping.
                    (double RawLeft, double RawTop)[] candidates =
                    [
                        (t.Right,        dragTop),        // dragged sits to the right of target
                        (t.Left - dragW, dragTop),        // dragged sits to the left of target
                        (dragLeft,       t.Bottom),       // dragged sits below target
                        (dragLeft,       t.Top - dragH),  // dragged sits above target
                    ];

                    double bestMove = double.MaxValue;
                    double bestL = dragLeft;
                    double bestT = dragTop;

                    foreach (var (rawL, rawT) in candidates)
                    {
                        // Clamp to canvas boundary first.
                        double cl = Math.Max(0, Math.Min(rawL, MonitorCanvas.ActualWidth - dragW));
                        double ct = Math.Max(0, Math.Min(rawT, MonitorCanvas.ActualHeight - dragH));

                        // Discard if clamping pushed the group back into overlap with this target.
                        if (cl < t.Right && (cl + dragW) > t.Left &&
                            ct < t.Bottom && (ct + dragH) > t.Top)
                        {
                            continue;
                        }

                        double move = Math.Abs(cl - dragLeft) + Math.Abs(ct - dragTop);
                        if (move < bestMove)
                        {
                            bestMove = move;
                            bestL = cl;
                            bestT = ct;
                        }
                    }

                    // No valid resolution found (canvas too small to clear the overlap).
                    if (bestMove == double.MaxValue ||
                        (Math.Abs(bestL - dragLeft) < 0.5 && Math.Abs(bestT - dragTop) < 0.5))
                    {
                        break;
                    }

                    double deltaX = bestL - dragLeft;
                    double deltaY = bestT - dragTop;
                    foreach (var vm in group)
                    {
                        vm.UpdatePosition(vm.CanvasLeft + deltaX, vm.CanvasTop + deltaY);
                    }

                    break; // Re-evaluate all targets after each move.
                }

                if (!anyOverlap)
                {
                    break;
                }
            }
        }

        private async void ApplyMonitorLayout_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CommitCanvasLayout();

            MonitorLayoutStatusText.Text = Helpers.ResourceLoaderInstance.ResourceLoader.GetString("MouseWithoutBorders_LayoutSaved");
            MonitorLayoutStatusText.Visibility = Visibility.Visible;
            await Task.Delay(2000);
            MonitorLayoutStatusText.Visibility = Visibility.Collapsed;
        }
    }
}
