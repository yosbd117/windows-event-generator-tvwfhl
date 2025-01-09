using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using EventSimulator.UI.ViewModels;
using EventSimulator.UI.Controls;

namespace EventSimulator.UI.Views
{
    /// <summary>
    /// Interaction logic for EventGeneratorView.xaml
    /// Implements the code-behind functionality for the Event Generator interface
    /// with comprehensive accessibility support and Material Design integration.
    /// </summary>
    public partial class EventGeneratorView : UserControl
    {
        private EventGeneratorViewModel ViewModel => DataContext as EventGeneratorViewModel;
        private bool _isInitialized;
        private readonly SnackbarMessageQueue _messageQueue;
        private double _currentDpiScale;

        /// <summary>
        /// Gets or sets whether accessibility features are enabled
        /// </summary>
        public bool IsAccessibilityEnabled { get; private set; }

        /// <summary>
        /// Gets or sets whether high contrast mode is enabled
        /// </summary>
        public bool IsHighContrastEnabled { get; private set; }

        /// <summary>
        /// Initializes a new instance of the EventGeneratorView
        /// </summary>
        public EventGeneratorView()
        {
            InitializeComponent();
            
            _messageQueue = new SnackbarMessageQueue(TimeSpan.FromSeconds(3));
            _currentDpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            ConfigureAccessibility();
            ConfigureAutomation();
            ConfigureKeyboardNavigation();
            ConfigureValidation();
            ConfigureDpiScaling();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized) return;

            if (ViewModel == null)
            {
                throw new InvalidOperationException("EventGeneratorViewModel not found in DataContext");
            }

            // Initialize view state
            UpdateAccessibilityFeatures(SystemParameters.HighContrast);
            RegisterEventHandlers();
            InitializeValidationTracking();

            _isInitialized = true;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnregisterEventHandlers();
            CleanupValidationTracking();
            _messageQueue.Clear();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged oldViewModel)
            {
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is INotifyPropertyChanged newViewModel)
            {
                newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void ConfigureAccessibility()
        {
            // Set up automation properties
            AutomationProperties.SetName(this, "Event Generator");
            AutomationProperties.SetHelpText(this, "Generate Windows Event Log entries with customizable parameters");
            AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);

            // Configure high contrast support
            IsHighContrastEnabled = SystemParameters.HighContrast;
            SystemParameters.StaticPropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.HighContrast))
                {
                    UpdateAccessibilityFeatures(SystemParameters.HighContrast);
                }
            };
        }

        private void ConfigureAutomation()
        {
            // Set up automation peer
            var peer = UIElementAutomationPeer.FromElement(this) ?? 
                      new FrameworkElementAutomationPeer(this);

            // Configure automation patterns
            peer.SetAutomationControlType(AutomationControlType.Pane);
            peer.SetAutomationId("EventGeneratorView");

            // Set up live region for status updates
            AutomationProperties.SetLiveSetting(statusBar, AutomationLiveSetting.Polite);
        }

        private void ConfigureKeyboardNavigation()
        {
            // Set up keyboard navigation
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Contained);

            // Configure keyboard shortcuts
            var generateBinding = new KeyBinding(
                ViewModel?.GenerateCommand,
                Key.G,
                ModifierKeys.Control);
            InputBindings.Add(generateBinding);
        }

        private void ConfigureValidation()
        {
            // Set up validation visual states
            foreach (var editor in parameterPanel.Children)
            {
                if (editor is EventParameterEditor paramEditor)
                {
                    paramEditor.ParameterChanged += OnParameterChanged;
                }
            }
        }

        private void ConfigureDpiScaling()
        {
            // Handle DPI changes
            DpiChanged += (s, e) =>
            {
                _currentDpiScale = e.NewDpi.DpiScaleX;
                UpdateDpiScaling();
            };
        }

        private void RegisterEventHandlers()
        {
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged += OnViewModelPropertyChanged;
                ViewModel.ValidationState.ValidationStateChanged += OnValidationStateChanged;
            }
        }

        private void UnregisterEventHandlers()
        {
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                ViewModel.ValidationState.ValidationStateChanged -= OnValidationStateChanged;
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(EventGeneratorViewModel.IsGenerating):
                    UpdateGenerationState();
                    break;
                case nameof(EventGeneratorViewModel.StatusMessage):
                    UpdateStatusMessage();
                    break;
                case nameof(EventGeneratorViewModel.HasErrors):
                    UpdateValidationState();
                    break;
            }
        }

        private void OnValidationStateChanged(object sender, ValidationStateChangedEventArgs e)
        {
            UpdateValidationState();
            UpdateAccessibilityFeedback(e.HasErrors, e.ErrorMessage);
        }

        private void OnParameterChanged(object sender, EventParameter e)
        {
            if (sender is EventParameterEditor editor)
            {
                UpdateParameterValidation(editor, e);
            }
        }

        private void UpdateAccessibilityFeatures(bool highContrastEnabled)
        {
            IsHighContrastEnabled = highContrastEnabled;
            IsAccessibilityEnabled = true;

            var theme = highContrastEnabled ? 
                Application.Current.Resources["HighContrastTheme"] : 
                Application.Current.Resources["LightTheme"];

            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(theme);

            AutomationProperties.SetIsRequiredForForm(this, true);
            UpdateAccessibilityFeedback(ViewModel?.HasErrors ?? false, ViewModel?.StatusMessage);
        }

        private void UpdateAccessibilityFeedback(bool hasErrors, string message = null)
        {
            if (hasErrors)
            {
                var peer = UIElementAutomationPeer.FromElement(this);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                AutomationProperties.SetHelpText(this, message ?? "Validation errors present");
            }
        }

        private void UpdateDpiScaling()
        {
            var scale = _currentDpiScale;
            foreach (var control in LogicalTreeHelper.GetChildren(this))
            {
                if (control is FrameworkElement element)
                {
                    element.LayoutTransform = new ScaleTransform(scale, scale);
                }
            }
        }

        private void UpdateGenerationState()
        {
            if (ViewModel == null) return;

            var state = ViewModel.IsGenerating ? "Generating events..." : "Ready";
            AutomationProperties.SetHelpText(this, state);
            AutomationProperties.SetName(generateButton, 
                ViewModel.IsGenerating ? "Cancel Generation" : "Generate Events");
        }

        private void UpdateStatusMessage()
        {
            if (ViewModel == null) return;

            AutomationProperties.SetName(statusBar, ViewModel.StatusMessage);
            var peer = UIElementAutomationPeer.FromElement(statusBar);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        private void UpdateValidationState()
        {
            if (ViewModel == null) return;

            var validationState = ViewModel.HasErrors ? "Invalid" : "Valid";
            AutomationProperties.SetItemStatus(this, validationState);
        }

        private void UpdateParameterValidation(EventParameterEditor editor, EventParameter parameter)
        {
            if (editor == null || parameter == null) return;

            var validationMessage = editor.IsValid ? string.Empty : editor.ValidationMessage;
            AutomationProperties.SetHelpText(editor, validationMessage);

            if (!editor.IsValid)
            {
                var peer = UIElementAutomationPeer.FromElement(editor);
                peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        private void InitializeValidationTracking()
        {
            if (ViewModel?.ValidationState != null)
            {
                foreach (var editor in parameterPanel.Children)
                {
                    if (editor is EventParameterEditor paramEditor)
                    {
                        ViewModel.ValidationState.RegisterEditor(paramEditor);
                    }
                }
            }
        }

        private void CleanupValidationTracking()
        {
            if (ViewModel?.ValidationState != null)
            {
                ViewModel.ValidationState.ClearEditors();
            }
        }
    }
}