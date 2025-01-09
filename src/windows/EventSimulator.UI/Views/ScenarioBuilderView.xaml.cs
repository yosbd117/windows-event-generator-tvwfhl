// External package versions:
// System.Windows.Controls v6.0.0
// System.Windows v6.0.0
// System.Windows.Automation v6.0.0

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Threading;
using EventSimulator.UI.ViewModels;
using EventSimulator.UI.Controls;

namespace EventSimulator.UI.Views
{
    /// <summary>
    /// Interaction logic for ScenarioBuilderView.xaml - Provides a comprehensive interface
    /// for creating and managing event simulation scenarios with timeline-based visualization,
    /// accessibility support, and real-time execution feedback.
    /// </summary>
    public partial class ScenarioBuilderView : UserControl, IDisposable
    {
        private readonly TimelineControl _timelineControl;
        private readonly ScenarioBuilderViewModel _viewModel;
        private CancellationTokenSource _executionCancellation;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the ScenarioBuilderView with full accessibility support
        /// and real-time execution feedback capabilities.
        /// </summary>
        public ScenarioBuilderView()
        {
            InitializeComponent();
            
            // Initialize timeline control
            _timelineControl = (TimelineControl)FindName("TimelineControl");
            if (_timelineControl != null)
            {
                _timelineControl.TimeScaleChanged += OnTimelineScaleChanged;
                _timelineControl.EventSelected += OnEventSelected;
                _timelineControl.DependencyChanged += OnDependencyChanged;
            }

            // Initialize view model
            _viewModel = (ScenarioBuilderViewModel)DataContext;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }

            InitializeAccessibility();
            InitializeKeyboardNavigation();
            InitializeExecutionHandlers();
        }

        private void InitializeAccessibility()
        {
            // Set automation properties for the main view
            AutomationProperties.SetName(this, "Scenario Builder");
            AutomationProperties.SetHelpText(this, "Create and manage event simulation scenarios");

            // Set up live region for status updates
            if (FindName("StatusText") is TextBlock statusText)
            {
                AutomationProperties.SetLiveSetting(statusText, AutomationLiveSetting.Polite);
            }

            // Configure automation properties for main controls
            if (FindName("ScenarioList") is ListBox scenarioList)
            {
                AutomationProperties.SetName(scenarioList, "Scenario List");
                AutomationProperties.SetItemStatus(scenarioList, "Select a scenario to edit");
            }

            if (FindName("ExecuteButton") is Button executeButton)
            {
                AutomationProperties.SetName(executeButton, "Execute Scenario");
                AutomationProperties.SetHelpText(executeButton, "Start executing the selected scenario");
            }
        }

        private void InitializeKeyboardNavigation()
        {
            // Set tab navigation order
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            
            // Set up keyboard shortcuts
            var executeBinding = new KeyBinding(
                _viewModel.ExecuteScenarioCommand,
                Key.F5,
                ModifierKeys.None);
            
            var cancelBinding = new KeyBinding(
                _viewModel.CancelExecutionCommand,
                Key.Escape,
                ModifierKeys.None);

            InputBindings.Add(executeBinding);
            InputBindings.Add(cancelBinding);
        }

        private void InitializeExecutionHandlers()
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += (s, e) =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(ScenarioBuilderViewModel.IsExecuting):
                            UpdateExecutionState(_viewModel.IsExecuting);
                            break;
                        case nameof(ScenarioBuilderViewModel.ExecutionProgress):
                            UpdateProgressIndicators(_viewModel.ExecutionProgress);
                            break;
                        case nameof(ScenarioBuilderViewModel.CurrentEventStatus):
                            UpdateStatusMessage(_viewModel.CurrentEventStatus);
                            break;
                    }
                };
            }
        }

        private void OnTimelineScaleChanged(object sender, TimelineScaleChangedEventArgs e)
        {
            if (_timelineControl != null)
            {
                // Update timeline visualization
                _timelineControl.TimeScale = e.NewScale;
                
                // Announce scale change for screen readers
                if (AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged))
                {
                    var peer = UIElementAutomationPeer.CreatePeerForElement(_timelineControl);
                    peer.RaiseAutomationEvent(AutomationEvents.PropertyChanged);
                }
            }
        }

        private void OnEventSelected(object sender, EventSelectedEventArgs e)
        {
            if (_viewModel != null && e.SelectedEvent != null)
            {
                _viewModel.SelectedEvent = e.SelectedEvent;
                
                // Update accessibility focus
                if (FindName("EventProperties") is FrameworkElement properties)
                {
                    properties.Focus();
                    AutomationProperties.SetName(properties, 
                        $"Properties for event {e.SelectedEvent.ScenarioEventId}");
                }
            }
        }

        private void OnDependencyChanged(object sender, DependencyChangedEventArgs e)
        {
            if (_timelineControl != null)
            {
                _timelineControl.UpdateConnections();
                
                // Announce dependency change for screen readers
                var message = e.IsAdding ? 
                    $"Added dependency between events {e.SourceEventId} and {e.TargetEventId}" :
                    $"Removed dependency between events {e.SourceEventId} and {e.TargetEventId}";
                
                RaiseAutomationNotification(message);
            }
        }

        private void UpdateExecutionState(bool isExecuting)
        {
            if (isExecuting)
            {
                _executionCancellation = new CancellationTokenSource();
                
                // Update UI state
                if (FindName("ExecuteButton") is Button executeButton)
                {
                    executeButton.IsEnabled = false;
                    AutomationProperties.SetName(executeButton, "Executing scenario...");
                }
                
                // Enable progress indicators
                if (FindName("ProgressBar") is ProgressBar progressBar)
                {
                    progressBar.IsIndeterminate = false;
                    progressBar.Value = 0;
                    AutomationProperties.SetName(progressBar, "Execution progress");
                }
            }
            else
            {
                _executionCancellation?.Cancel();
                _executionCancellation?.Dispose();
                _executionCancellation = null;

                // Reset UI state
                if (FindName("ExecuteButton") is Button executeButton)
                {
                    executeButton.IsEnabled = true;
                    AutomationProperties.SetName(executeButton, "Execute Scenario");
                }
            }
        }

        private void UpdateProgressIndicators(double progress)
        {
            if (FindName("ProgressBar") is ProgressBar progressBar)
            {
                progressBar.Value = progress;
                AutomationProperties.SetName(progressBar, 
                    $"Execution progress: {progress:F0}%");
            }

            // Update timeline visualization
            _timelineControl?.HighlightCurrentEvent(progress);
        }

        private void UpdateStatusMessage(string message)
        {
            if (FindName("StatusText") is TextBlock statusText)
            {
                statusText.Text = message;
                
                // Announce status change for screen readers
                RaiseAutomationNotification(message);
            }
        }

        private void RaiseAutomationNotification(string message)
        {
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(this);
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                AutomationNotification.CreateNotification(
                    message,
                    AutomationNotificationKind.Other,
                    AutomationNotificationProcessing.CurrentThenMostRecent);
            }
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            
            // Announce focused element for screen readers
            if (e.NewFocus is FrameworkElement element)
            {
                var automationName = AutomationProperties.GetName(element);
                if (!string.IsNullOrEmpty(automationName))
                {
                    RaiseAutomationNotification($"Focused: {automationName}");
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _executionCancellation?.Dispose();
                    
                    if (_timelineControl != null)
                    {
                        _timelineControl.TimeScaleChanged -= OnTimelineScaleChanged;
                        _timelineControl.EventSelected -= OnEventSelected;
                        _timelineControl.DependencyChanged -= OnDependencyChanged;
                    }

                    if (_viewModel != null)
                    {
                        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                    }
                }
                _disposed = true;
            }
        }
    }
}