using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Threading;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using EventSimulator.UI.ViewModels;
using EventSimulator.UI.Services;
using EventSimulator.Core.Models;

namespace EventSimulator.UI.Views
{
    /// <summary>
    /// Interaction logic for LogViewerView.xaml with comprehensive event viewing, filtering,
    /// and accessibility support.
    /// </summary>
    public partial class LogViewerView : UserControl
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<LogViewerView> _logger;
        private readonly DispatcherTimer _searchDebounceTimer;
        private readonly VirtualizingStackPanel _virtualizingPanel;
        private LogViewerViewModel ViewModel => DataContext as LogViewerViewModel;

        private bool _isHighContrastEnabled;
        public bool IsHighContrastEnabled
        {
            get => _isHighContrastEnabled;
            private set
            {
                _isHighContrastEnabled = value;
                UpdateHighContrastMode();
            }
        }

        /// <summary>
        /// Initializes a new instance of the LogViewerView with required services
        /// </summary>
        public LogViewerView(INotificationService notificationService, ILogger<LogViewerView> logger)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            InitializeComponent();

            // Initialize virtualizing panel for efficient scrolling
            _virtualizingPanel = new VirtualizingStackPanel
            {
                VirtualizationMode = VirtualizationMode.Recycling,
                CacheLength = new VirtualizationCacheLength(1),
                CacheLengthUnit = VirtualizationCacheLengthUnit.Page
            };

            // Configure search debounce timer
            _searchDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            // Set up event handlers
            Loaded += OnLoaded;
            SystemParameters.HighContrastChanged += SystemParameters_HighContrastChanged;

            ConfigureAccessibility();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (ViewModel == null)
                {
                    _logger.LogError("ViewModel not initialized");
                    _notificationService.ShowNotification("Failed to initialize Log Viewer", NotificationType.Error);
                    return;
                }

                // Configure DataGrid
                EventsDataGrid.ItemsSource = ViewModel.Events;
                EventsDataGrid.VirtualizingPanel.ScrollUnit = ScrollUnit.Pixel;
                EventsDataGrid.EnableRowVirtualization = true;
                EventsDataGrid.SelectionChanged += OnEventSelected;

                // Initialize high contrast detection
                IsHighContrastEnabled = SystemParameters.HighContrast;

                _logger.LogInformation("LogViewerView loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing LogViewerView");
                _notificationService.ShowNotification("Error initializing Log Viewer", NotificationType.Error);
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in search text changed handler");
                _notificationService.ShowNotification("Error processing search", NotificationType.Error);
            }
        }

        private async void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                _searchDebounceTimer.Stop();
                await ViewModel.FilterEvents();
                UpdateAccessibilityStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying search filter");
                _notificationService.ShowNotification("Error applying filter", NotificationType.Error);
            }
        }

        private void OnEventSelected(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (EventsDataGrid.SelectedItem is EventInstance selectedEvent)
                {
                    ViewModel.SelectedEvent = selectedEvent;
                    UpdateEventDetailsAccessibility(selectedEvent);
                    _logger.LogInformation("Event {EventId} selected", selectedEvent.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling event selection");
                _notificationService.ShowNotification("Error displaying event details", NotificationType.Error);
            }
        }

        private void ConfigureAccessibility()
        {
            // Configure AutomationProperties for main controls
            AutomationProperties.SetName(SearchTextBox, "Search events");
            AutomationProperties.SetHelpText(SearchTextBox, "Enter text to filter events");

            AutomationProperties.SetName(EventsDataGrid, "Events list");
            AutomationProperties.SetItemType(EventsDataGrid, "Event log entries");

            AutomationProperties.SetName(EventDetailsPanel, "Event details");
            AutomationProperties.SetItemType(EventDetailsPanel, "Selected event information");

            // Configure keyboard navigation
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(EventsDataGrid, KeyboardNavigationMode.Contained);

            // Set up live region for status updates
            AutomationProperties.SetLiveSetting(StatusTextBlock, AutomationLiveSetting.Polite);
        }

        private void UpdateEventDetailsAccessibility(EventInstance selectedEvent)
        {
            if (selectedEvent != null)
            {
                var detailsDescription = $"Event ID {selectedEvent.EventId} from {selectedEvent.Source}";
                AutomationProperties.SetName(EventDetailsPanel, detailsDescription);
                AutomationProperties.SetHelpText(EventDetailsPanel, 
                    $"Detailed information for {detailsDescription}. Press ENTER to expand.");
            }
        }

        private void UpdateAccessibilityStatus()
        {
            var statusMessage = ViewModel.Events.Count == 0 
                ? "No events found" 
                : $"Showing {ViewModel.Events.Count} events";
            
            AutomationProperties.SetName(StatusTextBlock, statusMessage);
            AutomationProperties.SetLiveSetting(StatusTextBlock, AutomationLiveSetting.Polite);
        }

        private void UpdateHighContrastMode()
        {
            if (IsHighContrastEnabled)
            {
                // Apply high contrast theme resources
                Resources.MergedDictionaries.Add(
                    new ResourceDictionary
                    {
                        Source = new Uri("/EventSimulator.UI;component/Themes/HighContrast.xaml", 
                            UriKind.Relative)
                    });
            }
            else
            {
                // Remove high contrast theme
                Resources.MergedDictionaries.RemoveAt(Resources.MergedDictionaries.Count - 1);
            }
        }

        private void SystemParameters_HighContrastChanged(object sender, EventArgs e)
        {
            IsHighContrastEnabled = SystemParameters.HighContrast;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            
            // Clean up event handlers
            SystemParameters.HighContrastChanged -= SystemParameters_HighContrastChanged;
            _searchDebounceTimer.Stop();
            EventsDataGrid.SelectionChanged -= OnEventSelected;
        }
    }
}