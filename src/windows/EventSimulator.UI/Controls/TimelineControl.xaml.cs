// External package versions:
// System.Windows.Controls v6.0.0
// System.Windows v6.0.0
// System.Windows.Input v6.0.0
// System.Windows.Media v6.0.0
// System.Collections.ObjectModel v6.0.0

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using EventSimulator.Core.Models;

namespace EventSimulator.UI.Controls
{
    /// <summary>
    /// Interaction logic for TimelineControl.xaml - Provides an interactive timeline visualization
    /// for scenario events with advanced rendering and manipulation capabilities.
    /// </summary>
    public class TimelineControl : Control
    {
        #region Dependency Properties

        public static readonly DependencyProperty EventsProperty =
            DependencyProperty.Register(nameof(Events), typeof(ObservableCollection<ScenarioEvent>),
                typeof(TimelineControl), new PropertyMetadata(null, OnEventsChanged));

        public static readonly DependencyProperty TimeScaleProperty =
            DependencyProperty.Register(nameof(TimeScale), typeof(double),
                typeof(TimelineControl), new PropertyMetadata(1.0, OnTimeScaleChanged));

        public static readonly DependencyProperty SelectedEventProperty =
            DependencyProperty.Register(nameof(SelectedEvent), typeof(ScenarioEvent),
                typeof(TimelineControl), new PropertyMetadata(null, OnSelectedEventChanged));

        public static readonly DependencyProperty ZoomLevelProperty =
            DependencyProperty.Register(nameof(ZoomLevel), typeof(double),
                typeof(TimelineControl), new PropertyMetadata(1.0, OnZoomLevelChanged));

        #endregion

        #region Public Properties

        public ObservableCollection<ScenarioEvent> Events
        {
            get => (ObservableCollection<ScenarioEvent>)GetValue(EventsProperty);
            set => SetValue(EventsProperty, value);
        }

        public double TimeScale
        {
            get => (double)GetValue(TimeScaleProperty);
            set => SetValue(TimeScaleProperty, value);
        }

        public ScenarioEvent SelectedEvent
        {
            get => (ScenarioEvent)GetValue(SelectedEventProperty);
            set => SetValue(SelectedEventProperty, value);
        }

        public double ZoomLevel
        {
            get => (double)GetValue(ZoomLevelProperty);
            set => SetValue(ZoomLevelProperty, value);
        }

        #endregion

        #region Private Fields

        private Canvas _timelineCanvas;
        private VirtualizingPanel _virtualPanel;
        private readonly Dictionary<int, UIElement> _eventNodes;
        private readonly Dictionary<string, Path> _connectionPool;
        private Point _lastDragPosition;
        private bool _isDragging;
        private const double GridSize = 10.0;
        private const double MinZoom = 0.1;
        private const double MaxZoom = 5.0;

        #endregion

        #region Constructor

        public TimelineControl()
        {
            _eventNodes = new Dictionary<int, UIElement>();
            _connectionPool = new Dictionary<string, Path>();

            DefaultStyleKey = typeof(TimelineControl);
            ClipToBounds = true;

            // Initialize the virtualization panel for event nodes
            _virtualPanel = new VirtualizingStackPanel
            {
                Orientation = Orientation.Horizontal,
                IsVirtualizing = true,
                VirtualizationMode = VirtualizationMode.Recycling
            };

            // Initialize the canvas for drawing connections
            _timelineCanvas = new Canvas
            {
                IsHitTestVisible = false,
                CacheMode = new BitmapCache()
            };

            InitializeEventHandlers();
        }

        #endregion

        #region Override Methods

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _timelineCanvas = GetTemplateChild("PART_Canvas") as Canvas;
            if (_timelineCanvas != null)
            {
                _timelineCanvas.Background = Brushes.Transparent;
                _timelineCanvas.UseLayoutRounding = true;
                RenderOptions.SetEdgeMode(_timelineCanvas, EdgeMode.Aliased);
            }

            InitializeTimelineComponents();
        }

        #endregion

        #region Private Methods

        private void InitializeEventHandlers()
        {
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            MouseMove += OnMouseMove;
            MouseWheel += OnMouseWheel;

            if (Events != null)
            {
                ((INotifyCollectionChanged)Events).CollectionChanged += OnEventsCollectionChanged;
            }
        }

        private void InitializeTimelineComponents()
        {
            if (_timelineCanvas == null) return;

            _timelineCanvas.Children.Clear();
            _eventNodes.Clear();
            _connectionPool.Clear();

            if (Events == null) return;

            foreach (var evt in Events)
            {
                CreateEventNode(evt);
            }

            UpdateEventConnections();
            UpdateLayout();
        }

        private void CreateEventNode(ScenarioEvent evt)
        {
            var node = new Border
            {
                Width = 100,
                Height = 50,
                Background = new SolidColorBrush(Colors.LightBlue),
                CornerRadius = new CornerRadius(5),
                Tag = evt
            };

            var content = new StackPanel
            {
                Orientation = Orientation.Vertical
            };

            content.Children.Add(new TextBlock
            {
                Text = $"Event {evt.ScenarioEventId}",
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            node.Child = content;

            Canvas.SetLeft(node, evt.Sequence * GridSize * TimeScale);
            Canvas.SetTop(node, evt.DelayMilliseconds / 1000.0 * GridSize);

            _eventNodes[evt.ScenarioEventId] = node;
            _timelineCanvas.Children.Add(node);
        }

        private void UpdateEventConnections()
        {
            foreach (var path in _connectionPool.Values)
            {
                _timelineCanvas.Children.Remove(path);
            }
            _connectionPool.Clear();

            foreach (var evt in Events)
            {
                foreach (var dependencyId in evt.DependsOnEvents)
                {
                    if (_eventNodes.TryGetValue(evt.ScenarioEventId, out var sourceNode) &&
                        _eventNodes.TryGetValue(dependencyId, out var targetNode))
                    {
                        CreateConnectionLine(sourceNode, targetNode, evt.ScenarioEventId, dependencyId);
                    }
                }
            }
        }

        private void CreateConnectionLine(UIElement source, UIElement target, int sourceId, int targetId)
        {
            var connectionKey = $"{sourceId}-{targetId}";
            
            var sourcePt = new Point(
                Canvas.GetLeft(source) + source.RenderSize.Width / 2,
                Canvas.GetTop(source) + source.RenderSize.Height / 2);
            
            var targetPt = new Point(
                Canvas.GetLeft(target) + target.RenderSize.Width / 2,
                Canvas.GetTop(target) + target.RenderSize.Height / 2);

            var path = new Path
            {
                Stroke = new SolidColorBrush(Colors.Gray),
                StrokeThickness = 2,
                Data = CreateBezierPath(sourcePt, targetPt)
            };

            _connectionPool[connectionKey] = path;
            _timelineCanvas.Children.Add(path);
            Panel.SetZIndex(path, -1);
        }

        private static Geometry CreateBezierPath(Point start, Point end)
        {
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = start };
            
            var controlPoint1 = new Point(start.X + (end.X - start.X) * 0.5, start.Y);
            var controlPoint2 = new Point(start.X + (end.X - start.X) * 0.5, end.Y);
            
            var segment = new BezierSegment(controlPoint1, controlPoint2, end, true);
            figure.Segments.Add(segment);
            geometry.Figures.Add(figure);

            return geometry;
        }

        #endregion

        #region Event Handlers

        private static void OnEventsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (TimelineControl)d;
            control.InitializeTimelineComponents();
        }

        private static void OnTimeScaleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (TimelineControl)d;
            control.UpdateEventConnections();
        }

        private static void OnSelectedEventChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (TimelineControl)d;
            control.UpdateSelectionVisuals();
        }

        private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (TimelineControl)d;
            control.ApplyZoom();
        }

        private void OnEventsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            InitializeTimelineComponents();
        }

        private void UpdateSelectionVisuals()
        {
            foreach (var node in _eventNodes.Values)
            {
                if (node is Border border)
                {
                    border.BorderBrush = (border.Tag as ScenarioEvent) == SelectedEvent ?
                        new SolidColorBrush(Colors.Orange) : null;
                    border.BorderThickness = (border.Tag as ScenarioEvent) == SelectedEvent ?
                        new Thickness(2) : new Thickness(0);
                }
            }
        }

        private void ApplyZoom()
        {
            var zoom = Math.Clamp(ZoomLevel, MinZoom, MaxZoom);
            var transform = new ScaleTransform(zoom, zoom);
            _timelineCanvas.LayoutTransform = transform;
        }

        #endregion

        #region Mouse Event Handlers

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var hitTestResult = VisualTreeHelper.HitTest(_timelineCanvas, e.GetPosition(_timelineCanvas));
            if (hitTestResult?.VisualHit is Border border && border.Tag is ScenarioEvent evt)
            {
                SelectedEvent = evt;
                _isDragging = true;
                _lastDragPosition = e.GetPosition(_timelineCanvas);
                border.CaptureMouse();
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                foreach (var node in _eventNodes.Values)
                {
                    if (node is Border border)
                    {
                        border.ReleaseMouseCapture();
                    }
                }
                UpdateEventConnections();
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(_timelineCanvas);
                var delta = new Point(
                    currentPos.X - _lastDragPosition.X,
                    currentPos.Y - _lastDragPosition.Y);

                if (e.OriginalSource is Border border && border.Tag is ScenarioEvent evt)
                {
                    var newLeft = Math.Max(0, Canvas.GetLeft(border) + delta.X);
                    var newTop = Math.Max(0, Canvas.GetTop(border) + delta.Y);

                    // Snap to grid
                    newLeft = Math.Round(newLeft / GridSize) * GridSize;
                    newTop = Math.Round(newTop / GridSize) * GridSize;

                    Canvas.SetLeft(border, newLeft);
                    Canvas.SetTop(border, newTop);

                    evt.Sequence = (int)(newLeft / (GridSize * TimeScale));
                    evt.DelayMilliseconds = (int)(newTop * 1000 / GridSize);

                    UpdateEventConnections();
                }

                _lastDragPosition = currentPos;
            }
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
            ZoomLevel = Math.Clamp(ZoomLevel + zoomDelta, MinZoom, MaxZoom);
        }

        #endregion
    }
}