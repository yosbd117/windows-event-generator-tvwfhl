// External package versions:
// MaterialDesignThemes.Wpf - v4.9.0
// System.Windows.Controls - v6.0.0
// System.Windows - v6.0.0

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using EventSimulator.Core.Models;
using EventSimulator.Core.Interfaces;

namespace EventSimulator.UI.Controls
{
    /// <summary>
    /// Interaction logic for EventParameterEditor.xaml
    /// </summary>
    public partial class EventParameterEditor : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty ParameterProperty =
            DependencyProperty.Register(
                nameof(Parameter),
                typeof(EventParameter),
                typeof(EventParameterEditor),
                new PropertyMetadata(null, OnParameterChanged));

        public static readonly DependencyProperty IsValidProperty =
            DependencyProperty.Register(
                nameof(IsValid),
                typeof(bool),
                typeof(EventParameterEditor),
                new PropertyMetadata(true));

        public static readonly DependencyProperty ValidationMessageProperty =
            DependencyProperty.Register(
                nameof(ValidationMessage),
                typeof(string),
                typeof(EventParameterEditor),
                new PropertyMetadata(string.Empty));

        #endregion

        #region Properties

        public EventParameter Parameter
        {
            get => (EventParameter)GetValue(ParameterProperty);
            set => SetValue(ParameterProperty, value);
        }

        public bool IsValid
        {
            get => (bool)GetValue(IsValidProperty);
            private set => SetValue(IsValidProperty, value);
        }

        public string ValidationMessage
        {
            get => (string)GetValue(ValidationMessageProperty);
            private set => SetValue(ValidationMessageProperty, value);
        }

        private bool IsAsyncValidationInProgress { get; set; }
        private CancellationTokenSource ValidationCancellation { get; set; }

        #endregion

        #region Events

        public event EventHandler<EventParameter> ParameterChanged;

        #endregion

        public EventParameterEditor()
        {
            InitializeComponent();

            // Configure accessibility properties
            AutomationProperties.SetName(this, "Event Parameter Editor");
            AutomationProperties.SetHelpText(this, "Editor for configuring Windows Event Log parameter values");
            AutomationProperties.SetIsRequiredForForm(this, true);

            // Set up keyboard navigation
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Contained);

            // Initialize validation support
            ValidationCancellation = new CancellationTokenSource();
        }

        private static void OnParameterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EventParameterEditor editor)
            {
                editor.HandleParameterChanged(e);
            }
        }

        private async void HandleParameterChanged(DependencyPropertyChangedEventArgs e)
        {
            ValidationCancellation?.Cancel();
            ValidationCancellation = new CancellationTokenSource();

            var parameter = e.NewValue as EventParameter;
            if (parameter == null)
            {
                IsValid = false;
                ValidationMessage = "Invalid parameter";
                return;
            }

            await ValidateParameterAsync(ValidationCancellation.Token);
            ParameterChanged?.Invoke(this, parameter);
        }

        private async Task<bool> ValidateParameterAsync(CancellationToken cancellationToken)
        {
            IsAsyncValidationInProgress = true;
            IsValid = false;

            try
            {
                if (Parameter == null)
                {
                    ValidationMessage = "Parameter is required";
                    return false;
                }

                // Check if parameter is required but empty
                if (Parameter.IsRequired && string.IsNullOrWhiteSpace(Parameter.Value))
                {
                    ValidationMessage = $"{Parameter.Name} is required";
                    return false;
                }

                // Skip validation if value is empty and not required
                if (string.IsNullOrWhiteSpace(Parameter.Value) && !Parameter.IsRequired)
                {
                    ValidationMessage = string.Empty;
                    IsValid = true;
                    return true;
                }

                // Validate data type
                if (!await ValidateDataTypeAsync(cancellationToken))
                {
                    ValidationMessage = $"Invalid {Parameter.DataType} value";
                    return false;
                }

                // Check validation pattern if specified
                if (!string.IsNullOrWhiteSpace(Parameter.ValidationPattern))
                {
                    try
                    {
                        if (!System.Text.RegularExpressions.Regex.IsMatch(Parameter.Value, Parameter.ValidationPattern))
                        {
                            ValidationMessage = "Value does not match required pattern";
                            return false;
                        }
                    }
                    catch (Exception)
                    {
                        ValidationMessage = "Invalid validation pattern";
                        return false;
                    }
                }

                ValidationMessage = string.Empty;
                IsValid = true;
                return true;
            }
            finally
            {
                IsAsyncValidationInProgress = false;
                UpdateAccessibilityProperties();
            }
        }

        private async Task<bool> ValidateDataTypeAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return false;

            try
            {
                switch (Parameter.DataType.ToLowerInvariant())
                {
                    case "int":
                        return int.TryParse(Parameter.Value, out _);
                    case "long":
                        return long.TryParse(Parameter.Value, out _);
                    case "datetime":
                        return DateTime.TryParse(Parameter.Value, out _);
                    case "bool":
                        return bool.TryParse(Parameter.Value, out _);
                    case "guid":
                        return Guid.TryParse(Parameter.Value, out _);
                    case "string":
                        return true;
                    default:
                        ValidationMessage = $"Unsupported data type: {Parameter.DataType}";
                        return false;
                }
            }
            catch (Exception ex)
            {
                ValidationMessage = $"Validation error: {ex.Message}";
                return false;
            }
        }

        private void UpdateAccessibilityProperties()
        {
            AutomationProperties.SetName(this, $"Parameter Editor - {Parameter?.Name ?? "Unknown"}");
            AutomationProperties.SetHelpText(this, Parameter?.Description ?? "No description available");
            
            var validationState = IsValid ? "Valid" : $"Invalid - {ValidationMessage}";
            AutomationProperties.SetItemStatus(this, validationState);
            
            if (!IsValid)
            {
                var peer = UIElementAutomationPeer.FromElement(this) ?? 
                          new FrameworkElementAutomationPeer(this);
                peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            UpdateAccessibilityProperties();
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            if (!IsAsyncValidationInProgress)
            {
                _ = ValidateParameterAsync(CancellationToken.None);
            }
        }
    }
}