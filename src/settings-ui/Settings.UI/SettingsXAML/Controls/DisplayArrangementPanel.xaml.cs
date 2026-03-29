// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace Microsoft.PowerToys.Settings.UI.Controls
{
    /// <summary>
    /// Renders the current machine's display arrangement and lets the user
    /// position remote machines on the edges of individual displays via drag-and-drop.
    /// </summary>
    public sealed partial class DisplayArrangementPanel : UserControl
    {
        private const double EdgeDropZoneThickness = 36.0;
        private const double MonitorLabelFontSize = 11.0;
        private const double MachineLabelFontSize = 11.0;
        private const double CanvasPadding = 8.0;

        // Tag attached to each edge drop-zone border so we can identify it on drop
        private sealed class EdgeTag
        {
            public DisplayEdge Edge { get; init; }

            public int DisplayIndex { get; init; }
        }

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(MouseWithoutBordersViewModel),
                typeof(DisplayArrangementPanel),
                new PropertyMetadata(null, OnViewModelChanged));

        public MouseWithoutBordersViewModel ViewModel
        {
            get => (MouseWithoutBordersViewModel)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public DisplayArrangementPanel()
        {
            InitializeComponent();
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DisplayArrangementPanel panel)
            {
                panel.RebuildLayout();
            }
        }

        private void LayoutCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RebuildLayout();
        }

        private void RebuildLayout()
        {
            LayoutCanvas.Children.Clear();

            var vm = ViewModel;
            if (vm == null)
            {
                return;
            }

            var displays = vm.LocalDisplays;
            if (displays == null || displays.Count == 0)
            {
                NoDisplaysText.Visibility = Visibility.Visible;
                return;
            }

            NoDisplaysText.Visibility = Visibility.Collapsed;

            double canvasW = Math.Max(LayoutCanvas.ActualWidth, 100);
            double canvasH = Math.Max(LayoutCanvas.ActualHeight, 100);

            var (scaled, _) = vm.GetScaledDisplays(canvasW, canvasH);

            for (int i = 0; i < scaled.Count; i++)
            {
                var sr = scaled[i];
                double w = Math.Max(sr.Width, 4);
                double h = Math.Max(sr.Height, 4);

                // Display body
                var displayBorder = new Border
                {
                    Width = w,
                    Height = h,
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                };
                Canvas.SetLeft(displayBorder, sr.Left);
                Canvas.SetTop(displayBorder, sr.Top);
                LayoutCanvas.Children.Add(displayBorder);

                // Monitor index label
                var label = new TextBlock
                {
                    Text = i == 0 ? "●" : $"{i + 1}",
                    FontSize = MonitorLabelFontSize,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                };
                var labelContainer = new Grid
                {
                    Width = w,
                    Height = h,
                };
                labelContainer.Children.Add(label);
                Canvas.SetLeft(labelContainer, sr.Left);
                Canvas.SetTop(labelContainer, sr.Top);
                LayoutCanvas.Children.Add(labelContainer);

                // Edge drop zones
                AddEdgeDropZone(sr, i, DisplayEdge.Left, vm);
                AddEdgeDropZone(sr, i, DisplayEdge.Right, vm);
                AddEdgeDropZone(sr, i, DisplayEdge.Top, vm);
                AddEdgeDropZone(sr, i, DisplayEdge.Bottom, vm);
            }
        }

        private void AddEdgeDropZone(DisplayRect sr, int displayIndex, DisplayEdge edge, MouseWithoutBordersViewModel vm)
        {
            double thickness = EdgeDropZoneThickness;
            double left, top, width, height;

            switch (edge)
            {
                case DisplayEdge.Left:
                    left = sr.Left - thickness;
                    top = sr.Top;
                    width = thickness;
                    height = sr.Height;
                    break;
                case DisplayEdge.Right:
                    left = sr.Right;
                    top = sr.Top;
                    width = thickness;
                    height = sr.Height;
                    break;
                case DisplayEdge.Top:
                    left = sr.Left;
                    top = sr.Top - thickness;
                    width = sr.Width;
                    height = thickness;
                    break;
                case DisplayEdge.Bottom:
                    left = sr.Left;
                    top = sr.Bottom;
                    width = sr.Width;
                    height = thickness;
                    break;
                default:
                    return;
            }

            // Clamp to avoid negative canvas positions for leftmost/topmost displays
            left = Math.Max(0, left);
            top = Math.Max(0, top);

            string assignedMachine = vm.GetMachineAtEdge(edge, displayIndex);
            bool hasAssignment = !string.IsNullOrEmpty(assignedMachine);

            var zone = new Border
            {
                Width = Math.Max(width, 4),
                Height = Math.Max(height, 4),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                AllowDrop = true,
                Tag = new EdgeTag { Edge = edge, DisplayIndex = displayIndex },
            };

            if (hasAssignment)
            {
                zone.Background = new SolidColorBrush(Color.FromArgb(0x22, 0x00, 0x78, 0xD4));
                zone.BorderBrush = new SolidColorBrush(Color.FromArgb(0xAA, 0x00, 0x78, 0xD4));

                // Machine name label inside the zone
                var machineLabel = new TextBlock
                {
                    Text = assignedMachine,
                    FontSize = MachineLabelFontSize,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap,
                    MaxWidth = Math.Max(width - 4, 4),
                };
                zone.Child = machineLabel;
            }
            else
            {
                zone.Background = new SolidColorBrush(Color.FromArgb(0x08, 0x80, 0x80, 0x80));
                zone.BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));
                zone.BorderThickness = new Thickness(1) { };
            }

            zone.DragOver += EdgeZone_DragOver;
            zone.Drop += EdgeZone_Drop;

            // Right-click to clear
            var flyout = new MenuFlyout();
            var clearItem = new MenuFlyoutItem
            {
                Text = hasAssignment ? $"Remove \"{assignedMachine}\"" : string.Empty,
                IsEnabled = hasAssignment,
            };
            clearItem.Click += (_, __) =>
            {
                if (zone.Tag is EdgeTag t)
                {
                    vm.AssignMachineToDisplayEdge(string.Empty, t.Edge, t.DisplayIndex);
                    RebuildLayout();
                }
            };
            flyout.Items.Add(clearItem);
            zone.ContextFlyout = flyout;

            Canvas.SetLeft(zone, left);
            Canvas.SetTop(zone, top);
            LayoutCanvas.Children.Add(zone);
        }

        // ---- Drag and Drop ----

        private const string DragDropFormatMachineName = "MWB.DisplayPanel.MachineName";

        private void Canvas_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Move;
        }

        private void Canvas_Drop(object sender, DragEventArgs e)
        {
            // Drops outside edge zones are ignored
        }

        private void EdgeZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(DragDropFormatMachineName) ||
                e.DataView.Properties.ContainsKey("MWB Device Drag Drop"))
            {
                e.AcceptedOperation = DataPackageOperation.Move;

                if (sender is Border b)
                {
                    b.Background = new SolidColorBrush(Color.FromArgb(0x44, 0x00, 0x78, 0xD4));
                }
            }
        }

        private async void EdgeZone_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Border zone || zone.Tag is not EdgeTag tag)
            {
                return;
            }

            string machineName = null;

            // Accept drops from the device matrix (existing flat grid)
            if (e.DataView.Properties.TryGetValue("index", out object boxIndex) &&
                e.DataView.Properties.ContainsKey("MWB Device Drag Drop"))
            {
                // The flat grid passes the matrix slot index; we need the machine name.
                // We get it from the ViewModel's MachineMatrixString.
                int slotIndex = (int)boxIndex;
                var vm = ViewModel;
                if (vm != null)
                {
                    var matrix = vm.MachineMatrixString?.ToEnumerable().ToList();
                    if (matrix != null && slotIndex >= 0 && slotIndex < matrix.Count)
                    {
                        machineName = matrix[slotIndex].Name;
                    }
                }
            }
            else if (e.DataView.Contains(DragDropFormatMachineName))
            {
                try
                {
                    machineName = await e.DataView.GetTextAsync(DragDropFormatMachineName);
                }
                catch (Exception)
                {
                    machineName = null;
                }
            }

            if (!string.IsNullOrEmpty(machineName))
            {
                ViewModel?.AssignMachineToDisplayEdge(machineName, tag.Edge, tag.DisplayIndex);
                RebuildLayout();
            }
        }

        /// <summary>
        /// Initiates a drag operation for a machine, to be used from external code (e.g. the machine list).
        /// </summary>
        public static void BeginMachineDrag(UIElement source, string machineName, DragStartingEventArgs args)
        {
            if (args == null || string.IsNullOrEmpty(machineName))
            {
                return;
            }

            args.Data.RequestedOperation = DataPackageOperation.Move;
            args.Data.SetText(machineName);
            args.Data.Properties[DragDropFormatMachineName] = machineName;
        }
    }
}
