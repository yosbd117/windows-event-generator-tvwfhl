using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Logging;
using EventSimulator.UI.ViewModels;

namespace EventSimulator.UI.Views
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml with comprehensive settings management,
    /// accessibility features, and Material Design integration
    /// </summary>
    public partial class SettingsView : UserControl
    {
        private readonly ILogger<SettingsView> _logger;
        private readonly SettingsViewModel _viewModel;
        private bool _isInitialized;

        /// <summary>
        /// Initializes a new instance of the SettingsView class
        /// </summary>
        /// <param name="logger">Logger for diagnostic and error tracking</param>
        public SettingsView(ILogger<SettingsView> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                InitializeComponent();
                InitializeAccessibility();
                InitializeMaterialDesign();
                
                _logger.LogInformation("SettingsView initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing SettingsView");
                throw;
            }

            Loaded += OnSettingsViewLoaded;
            Unloaded += OnSettingsViewUnloaded;
        }

        private void InitializeAccessibility()
        {
            // Configure automation properties for accessibility
            AutomationProperties.SetName(this, "Settings Configuration");
            AutomationProperties.SetItemType(this, "Settings Panel");
            AutomationProperties.SetIsDialog(this, true);
            AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);

            // Configure keyboard navigation
            KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetDirectionalNavigation(this, KeyboardNavigationMode.Contained);
        }

        private void InitializeMaterialDesign()
        {
            // Configure Material Design theme settings
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            theme.SetBaseTheme(Theme.Light);
            theme.SetPrimaryColor(System.Windows.Media.Colors.Blue);
            theme.SetSecondaryColor(System.Windows.Media.Colors.LightBlue);

            paletteHelper.SetTheme(theme);
        }

        private void OnSettingsViewLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_isInitialized) return;

                _viewModel = DataContext as SettingsViewModel;
                if (_viewModel == null)
                {
                    _logger.LogError("SettingsViewModel not found in DataContext");
                    throw new InvalidOperationException("SettingsViewModel not found in DataContext");
                }

                // Subscribe to ViewModel property changes
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;

                // Configure validation error handling
                SetupValidation();

                _isInitialized = true;
                _logger.LogInformation("SettingsView loaded and configured successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SettingsView load");
                throw;
            }
        }

        private void OnSettingsViewUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_viewModel != null)
                {
                    _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                }

                _isInitialized = false;
                _logger.LogInformation("SettingsView unloaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SettingsView unload");
            }
        }

        private void SetupValidation()
        {
            // Add validation error handling
            Validation.AddErrorHandler(this, OnValidationError);

            // Configure binding validation rules
            var bindingFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            var validationRules = new ValidationRuleCollection();

            validationRules.Add(new RangeValidationRule
            {
                ValidatesOnTargetUpdated = true,
                Min = 1,
                Max = 10000,
                ErrorMessage = "Value must be between 1 and 10000"
            });
        }

        private void OnValidationError(object sender, ValidationErrorEventArgs e)
        {
            if (e.Action == ValidationErrorEventAction.Added)
            {
                _logger.LogWarning("Validation error: {Error}", e.Error.ErrorContent);
                MessageBox.Show(
                    e.Error.ErrorContent.ToString(),
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                switch (e.PropertyName)
                {
                    case nameof(SettingsViewModel.MaxConcurrentEvents):
                        UpdateConcurrencySettings();
                        break;

                    case nameof(SettingsViewModel.EventGenerationBatchSize):
                        UpdateBatchSettings();
                        break;

                    case nameof(SettingsViewModel.ThemeSettings):
                        UpdateThemeSettings();
                        break;

                    case nameof(SettingsViewModel.LanguageSettings):
                        UpdateLanguageSettings();
                        break;

                    case nameof(SettingsViewModel.AccessibilitySettings):
                        UpdateAccessibilitySettings();
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling property change: {Property}", e.PropertyName);
            }
        }

        private void UpdateConcurrencySettings()
        {
            if (_viewModel.MaxConcurrentEvents <= 0 || _viewModel.MaxConcurrentEvents > 10000)
            {
                _logger.LogWarning("Invalid concurrency setting: {Value}", _viewModel.MaxConcurrentEvents);
                return;
            }

            _logger.LogInformation("Updated concurrency settings: {Value}", _viewModel.MaxConcurrentEvents);
        }

        private void UpdateBatchSettings()
        {
            if (_viewModel.EventGenerationBatchSize <= 0 || 
                _viewModel.EventGenerationBatchSize > _viewModel.MaxConcurrentEvents)
            {
                _logger.LogWarning("Invalid batch size: {Value}", _viewModel.EventGenerationBatchSize);
                return;
            }

            _logger.LogInformation("Updated batch settings: {Value}", _viewModel.EventGenerationBatchSize);
        }

        private void UpdateThemeSettings()
        {
            try
            {
                var paletteHelper = new PaletteHelper();
                var theme = paletteHelper.GetTheme();

                // Update theme based on settings
                theme.SetBaseTheme(_viewModel.ThemeSettings.IsDarkMode ? 
                    Theme.Dark : Theme.Light);

                paletteHelper.SetTheme(theme);
                _logger.LogInformation("Theme updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating theme settings");
            }
        }

        private void UpdateLanguageSettings()
        {
            try
            {
                // Update UI culture and resources
                System.Threading.Thread.CurrentThread.CurrentUICulture = 
                    new System.Globalization.CultureInfo(_viewModel.LanguageSettings.CurrentLanguage);

                _logger.LogInformation("Language updated to: {Language}", 
                    _viewModel.LanguageSettings.CurrentLanguage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating language settings");
            }
        }

        private void UpdateAccessibilitySettings()
        {
            try
            {
                // Update high contrast settings
                if (_viewModel.AccessibilitySettings.UseHighContrast)
                {
                    var paletteHelper = new PaletteHelper();
                    var theme = paletteHelper.GetTheme();
                    theme.SetHighContrastTheme();
                    paletteHelper.SetTheme(theme);
                }

                // Update font size
                this.FontSize = _viewModel.AccessibilitySettings.FontSize;

                _logger.LogInformation("Accessibility settings updated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating accessibility settings");
            }
        }
    }
}