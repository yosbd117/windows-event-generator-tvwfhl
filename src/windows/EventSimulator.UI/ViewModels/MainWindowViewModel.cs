using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.State;
using EventSimulator.UI.Services;

namespace EventSimulator.UI.ViewModels
{
    /// <summary>
    /// Main ViewModel for the Windows Event Simulator application that coordinates child ViewModels
    /// and manages the overall application state with enhanced production features.
    /// </summary>
    public class MainWindowViewModel : ObservableObject, IDisposable
    {
        private readonly ILogger<MainWindowViewModel> _logger;
        private readonly INotificationService _notificationService;
        private readonly ITelemetryService _telemetryService;
        private readonly IStateManager _stateManager;
        private readonly Stack<object> _navigationHistory;
        private readonly ConcurrentQueue<string> _statusMessageQueue;
        private readonly object _stateLock;
        private readonly PerformanceCounter _navigationCounter;
        private readonly PerformanceCounter _memoryUsageCounter;

        private object _currentView;
        private bool _isBusy;
        private string _statusMessage;
        private bool _isNavigationEnabled = true;

        /// <summary>
        /// Gets the Event Generator ViewModel instance.
        /// </summary>
        public EventGeneratorViewModel EventGeneratorVM { get; }

        /// <summary>
        /// Gets the Scenario Builder ViewModel instance.
        /// </summary>
        public ScenarioBuilderViewModel ScenarioBuilderVM { get; }

        /// <summary>
        /// Gets the Template Manager ViewModel instance.
        /// </summary>
        public TemplateManagerViewModel TemplateManagerVM { get; }

        /// <summary>
        /// Gets or sets the currently active view.
        /// </summary>
        public object CurrentView
        {
            get => _currentView;
            private set => SetProperty(ref _currentView, value);
        }

        /// <summary>
        /// Gets or sets whether the application is busy processing.
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>
        /// Gets or sets the current status message.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Gets or sets whether navigation is enabled.
        /// </summary>
        public bool IsNavigationEnabled
        {
            get => _isNavigationEnabled;
            private set => SetProperty(ref _isNavigationEnabled, value);
        }

        /// <summary>
        /// Gets the command to navigate to Event Generator view.
        /// </summary>
        public IAsyncRelayCommand NavigateToEventGeneratorCommand { get; }

        /// <summary>
        /// Gets the command to navigate to Scenario Builder view.
        /// </summary>
        public IAsyncRelayCommand NavigateToScenarioBuilderCommand { get; }

        /// <summary>
        /// Gets the command to navigate to Template Manager view.
        /// </summary>
        public IAsyncRelayCommand NavigateToTemplateManagerCommand { get; }

        /// <summary>
        /// Initializes a new instance of the MainWindowViewModel class.
        /// </summary>
        public MainWindowViewModel(
            EventGeneratorViewModel eventGeneratorVM,
            ScenarioBuilderViewModel scenarioBuilderVM,
            TemplateManagerViewModel templateManagerVM,
            INotificationService notificationService,
            ITelemetryService telemetryService,
            IStateManager stateManager,
            ILogger<MainWindowViewModel> logger)
        {
            // Validate dependencies
            EventGeneratorVM = eventGeneratorVM ?? throw new ArgumentNullException(nameof(eventGeneratorVM));
            ScenarioBuilderVM = scenarioBuilderVM ?? throw new ArgumentNullException(nameof(scenarioBuilderVM));
            TemplateManagerVM = templateManagerVM ?? throw new ArgumentNullException(nameof(templateManagerVM));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
            _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Initialize collections and synchronization
            _navigationHistory = new Stack<object>();
            _statusMessageQueue = new ConcurrentQueue<string>();
            _stateLock = new object();

            // Initialize performance counters
            _navigationCounter = new PerformanceCounter("Windows Event Simulator", "Navigation Operations", false);
            _memoryUsageCounter = new PerformanceCounter("Windows Event Simulator", "Memory Usage (MB)", false);

            // Initialize navigation commands with error handling
            NavigateToEventGeneratorCommand = new AsyncRelayCommand(
                async () => await NavigateToView(EventGeneratorVM),
                () => IsNavigationEnabled);

            NavigateToScenarioBuilderCommand = new AsyncRelayCommand(
                async () => await NavigateToView(ScenarioBuilderVM),
                () => IsNavigationEnabled);

            NavigateToTemplateManagerCommand = new AsyncRelayCommand(
                async () => await NavigateToView(TemplateManagerVM),
                () => IsNavigationEnabled);

            // Set default view
            CurrentView = EventGeneratorVM;
            _navigationHistory.Push(CurrentView);

            // Initialize telemetry
            _telemetryService.TrackEvent("ApplicationStarted");

            // Register for memory pressure notifications
            GC.RegisterForFullGCNotification(50, 50);
            _ = MonitorMemoryPressureAsync();

            _logger.LogInformation("MainWindowViewModel initialized successfully");
        }

        /// <summary>
        /// Navigates to the specified view with telemetry and error handling.
        /// </summary>
        private async Task NavigateToView(object viewModel)
        {
            _logger.LogInformation("Attempting navigation to {ViewType}", viewModel.GetType().Name);

            if (!IsNavigationEnabled)
            {
                _logger.LogWarning("Navigation attempted while disabled");
                return;
            }

            try
            {
                lock (_stateLock)
                {
                    // Deactivate current view if needed
                    if (CurrentView is IDeactivatable deactivatable)
                    {
                        deactivatable.Deactivate();
                    }

                    _navigationHistory.Push(CurrentView);
                    CurrentView = viewModel;
                    _navigationCounter.Increment();

                    // Activate new view if needed
                    if (viewModel is IActivatable activatable)
                    {
                        activatable.Activate();
                    }

                    UpdateApplicationState(false, $"Navigated to {viewModel.GetType().Name}");
                }

                _telemetryService.TrackPageView(viewModel.GetType().Name);
                _logger.LogInformation("Successfully navigated to {ViewType}", viewModel.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Navigation failed to {ViewType}", viewModel.GetType().Name);
                await _notificationService.ShowNotification(
                    "Navigation failed. Please try again.",
                    NotificationType.Error);
            }
        }

        /// <summary>
        /// Updates application state with thread safety.
        /// </summary>
        private void UpdateApplicationState(bool isBusy, string statusMessage)
        {
            lock (_stateLock)
            {
                IsBusy = isBusy;
                _statusMessageQueue.Enqueue(statusMessage);

                while (_statusMessageQueue.TryDequeue(out var message))
                {
                    StatusMessage = message;
                    _notificationService.UpdateStatusBar(message, IsBusy);
                }

                IsNavigationEnabled = !IsBusy;
                _telemetryService.TrackMetric("ApplicationBusy", IsBusy ? 1 : 0);
            }
        }

        /// <summary>
        /// Monitors memory pressure and triggers cleanup when needed.
        /// </summary>
        private async Task MonitorMemoryPressureAsync()
        {
            while (true)
            {
                try
                {
                    if (GC.WaitForFullGCApproach(1000))
                    {
                        _logger.LogWarning("High memory pressure detected");
                        _memoryUsageCounter.RawValue = Process.GetCurrentProcess().PrivateMemorySize64 / 1024 / 1024;
                        GC.Collect(2, GCCollectionMode.Optimized, true);
                    }
                    await Task.Delay(5000);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error monitoring memory pressure");
                }
            }
        }

        /// <summary>
        /// Performs application cleanup.
        /// </summary>
        public void Dispose()
        {
            try
            {
                // Save current view state
                _stateManager.SaveState("LastView", CurrentView.GetType().Name);

                // Clear collections
                _navigationHistory.Clear();
                while (_statusMessageQueue.TryDequeue(out _)) { }

                // Dispose performance counters
                _navigationCounter.Dispose();
                _memoryUsageCounter.Dispose();

                // Dispose child view models if needed
                if (EventGeneratorVM is IDisposable eventGeneratorDisposable)
                    eventGeneratorDisposable.Dispose();
                if (ScenarioBuilderVM is IDisposable scenarioBuilderDisposable)
                    scenarioBuilderDisposable.Dispose();
                if (TemplateManagerVM is IDisposable templateManagerDisposable)
                    templateManagerDisposable.Dispose();

                _logger.LogInformation("MainWindowViewModel disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during MainWindowViewModel disposal");
            }
        }
    }
}