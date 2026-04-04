// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    /// <summary>
    /// Per-monitor tile view model for the Windows Display Settings-like canvas.
    /// Wraps a <see cref="MonitorLayoutInfo"/> and adds UI-specific state such as
    /// canvas position, selection, and drag tracking.
    /// </summary>
    public class MonitorViewModel : INotifyPropertyChanged
    {
        private readonly MonitorLayoutInfo _info;
        private readonly double _scale;
        private readonly double _canvasOffsetX;
        private readonly double _canvasOffsetY;
        private readonly int _layoutOriginX;
        private readonly int _layoutOriginY;

        private double _canvasLeft;
        private double _canvasTop;
        private double _canvasWidth;
        private double _canvasHeight;
        private bool _isSelected;
        private bool _isDragging;
        private double _dragOffsetX;
        private double _dragOffsetY;
        private Brush? _statusBrush;
        private string? _statusText;

        public MonitorViewModel(MonitorLayoutInfo info, int indexWithinMachine, double scale, int layoutOriginX = 0, int layoutOriginY = 0, double canvasOffsetX = 0, double canvasOffsetY = 0)
        {
            ArgumentNullException.ThrowIfNull(info);

            _info = info;
            _scale = scale;
            _layoutOriginX = layoutOriginX;
            _layoutOriginY = layoutOriginY;
            _canvasOffsetX = canvasOffsetX;
            _canvasOffsetY = canvasOffsetY;

            MachineName = info.MachineName ?? string.Empty;
            MonitorId = info.MonitorId ?? string.Empty;
            IsPrimary = info.IsPrimary;
            Label = $"{MachineName} Monitor {indexWithinMachine}";

            _canvasLeft = ((info.X - _layoutOriginX) * scale) + canvasOffsetX;
            _canvasTop = ((info.Y - _layoutOriginY) * scale) + canvasOffsetY;
            _canvasWidth = info.Width * scale;
            _canvasHeight = info.Height * scale;
        }

        /// <summary>Gets the machine name this monitor belongs to.</summary>
        public string MachineName { get; }

        /// <summary>Gets the unique monitor identifier.</summary>
        public string MonitorId { get; }

        /// <summary>Gets the display label, e.g. "Desktop-A Monitor 1".</summary>
        public string Label { get; }

        /// <summary>Gets a value indicating whether this is the primary monitor on its machine.</summary>
        public bool IsPrimary { get; }

        /// <summary>Gets or sets the scaled horizontal position on the UI canvas.</summary>
        public double CanvasLeft
        {
            get => _canvasLeft;
            set
            {
                if (_canvasLeft != value)
                {
                    _canvasLeft = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gets or sets the scaled vertical position on the UI canvas.</summary>
        public double CanvasTop
        {
            get => _canvasTop;
            set
            {
                if (_canvasTop != value)
                {
                    _canvasTop = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gets the scaled width of the monitor tile on the UI canvas.</summary>
        public double CanvasWidth
        {
            get => _canvasWidth;
            set
            {
                if (_canvasWidth != value)
                {
                    _canvasWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gets the scaled height of the monitor tile on the UI canvas.</summary>
        public double CanvasHeight
        {
            get => _canvasHeight;
            set
            {
                if (_canvasHeight != value)
                {
                    _canvasHeight = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gets or sets a value indicating whether this tile is currently selected/focused.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gets or sets a value indicating whether a drag operation is in progress on this tile.</summary>
        public bool IsDragging
        {
            get => _isDragging;
            set
            {
                if (_isDragging != value)
                {
                    _isDragging = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gets or sets the horizontal offset from the tile origin to the drag-start point.</summary>
        public double DragOffsetX
        {
            get => _dragOffsetX;
            set
            {
                if (_dragOffsetX != value)
                {
                    _dragOffsetX = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gets or sets the vertical offset from the tile origin to the drag-start point.</summary>
        public double DragOffsetY
        {
            get => _dragOffsetY;
            set
            {
                if (_dragOffsetY != value)
                {
                    _dragOffsetY = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gets or sets the brush color indicating the connection status of this monitor's machine.</summary>
        public Brush? StatusBrush
        {
            get => _statusBrush;
            set
            {
                if (_statusBrush != value)
                {
                    _statusBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gets or sets the text description of the connection status.</summary>
        public string? StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Updates <see cref="CanvasLeft"/> and <see cref="CanvasTop"/> and fires
        /// <see cref="PropertyChanged"/> for both.
        /// </summary>
        public void UpdatePosition(double newCanvasLeft, double newCanvasTop)
        {
            CanvasLeft = newCanvasLeft;
            CanvasTop = newCanvasTop;
        }

        /// <summary>
        /// Converts this view model back to a <see cref="MonitorLayoutInfo"/>,
        /// translating the scaled canvas coordinates back to virtual pixel coordinates.
        /// </summary>
        public MonitorLayoutInfo ToLayoutInfo()
        {
            double safeScale = _scale > 0 ? _scale : 1.0;

            return new MonitorLayoutInfo
            {
                MachineName = _info.MachineName,
                MonitorId = _info.MonitorId,
                X = (int)Math.Round((_canvasLeft - _canvasOffsetX) / safeScale) + _layoutOriginX,
                Y = (int)Math.Round((_canvasTop - _canvasOffsetY) / safeScale) + _layoutOriginY,
                Width = _info.Width,
                Height = _info.Height,
                IsPrimary = _info.IsPrimary,
            };
        }

        /// <inheritdoc/>
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
