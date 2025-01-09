using CommunityToolkit.Mvvm.ComponentModel; // v8.0.0
using CommunityToolkit.Mvvm.Input; // v8.0.0
using EventSimulator.Common.Configuration;
using EventSimulator.UI.Services;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;

namespace EventSimulator.UI.ViewModels
{
    /// <summary>
    /// ViewModel for managing application settings and configuration with comprehensive validation and persistence
    /// </summary>
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly AppSettings _appSettings;
        private readonly INotificationService _notificationService;
        private readonly IDialogService _dialogService;
        private bool _hasUnsavedChanges;
        private bool _isLoading;

        [ObservableProperty]
        private int _maxConcurrentEvents;

        [ObservableProperty]
        private int _eventGenerationBatchSize;

        [ObservableProperty]
        private bool _enableDetailedLogging;

        [ObservableProperty]
        private bool _enablePerformanceMonitoring;

        [ObservableProperty]
        private string _templateStoragePath;

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set => SetProperty(ref _hasUnsavedChanges, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public ICommand SaveSettingsCommand { get; }
        public ICommand ResetToDefaultCommand { get; }
        public ICommand BrowseTemplatePathCommand { get; }
        public ICommand ValidateSettingsCommand { get; }

        /// <summary>
        /// Initializes a new instance of SettingsViewModel with dependency injection
        /// </summary>
        public SettingsViewModel(
            AppSettings appSettings,
            INotificationService notificationService,
            IDialogService dialogService)
        {
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));

            // Initialize commands
            SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanSaveSettings);
            ResetToDefaultCommand = new AsyncRelayCommand(ResetToDefaultAsync);
            BrowseTemplatePathCommand = new AsyncRelayCommand(BrowseTemplatePathAsync);
            ValidateSettingsCommand = new AsyncRelayCommand(ValidateSettingsAsync);

            // Subscribe to property changes
            PropertyChanged += OnSettingsPropertyChanged;

            // Load initial settings
            _ = LoadSettingsAsync();
        }

        private async Task LoadSettingsAsync()
        {
            try
            {
                IsLoading = true;
                _notificationService.UpdateStatusBar("Loading settings...", true);

                MaxConcurrentEvents = _appSettings.MaxConcurrentEvents;
                EventGenerationBatchSize = _appSettings.EventGenerationBatchSize;
                EnableDetailedLogging = _appSettings.EnableDetailedLogging;
                EnablePerformanceMonitoring = _appSettings.EnablePerformanceMonitoring;
                TemplateStoragePath = _appSettings.TemplateStoragePath;

                HasUnsavedChanges = false;
                await ValidateSettingsAsync();
            }
            catch (Exception ex)
            {
                await _notificationService.ShowNotification(
                    "Failed to load settings: " + ex.Message,
                    NotificationType.Error);
            }
            finally
            {
                IsLoading = false;
                _notificationService.UpdateStatusBar("Settings loaded", false);
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                _notificationService.UpdateStatusBar("Saving settings...", true);

                var validationResult = await ValidateSettingsAsync();
                if (!validationResult)
                {
                    return;
                }

                // Update AppSettings with current values
                _appSettings.MaxConcurrentEvents = MaxConcurrentEvents;
                _appSettings.EventGenerationBatchSize = EventGenerationBatchSize;
                _appSettings.EnableDetailedLogging = EnableDetailedLogging;
                _appSettings.EnablePerformanceMonitoring = EnablePerformanceMonitoring;
                _appSettings.TemplateStoragePath = TemplateStoragePath;

                // Validate and save configuration
                if (_appSettings.Validate())
                {
                    HasUnsavedChanges = false;
                    await _notificationService.ShowNotification(
                        "Settings saved successfully",
                        NotificationType.Success);
                }
            }
            catch (Exception ex)
            {
                await _notificationService.ShowNotification(
                    "Failed to save settings: " + ex.Message,
                    NotificationType.Error);
            }
            finally
            {
                _notificationService.UpdateStatusBar("Settings saved", false);
            }
        }

        private async Task<bool> ValidateSettingsAsync()
        {
            if (MaxConcurrentEvents < 1 || MaxConcurrentEvents > 10000)
            {
                await _notificationService.ShowNotification(
                    "Max concurrent events must be between 1 and 10000",
                    NotificationType.Warning);
                return false;
            }

            if (EventGenerationBatchSize < 1 || EventGenerationBatchSize > MaxConcurrentEvents)
            {
                await _notificationService.ShowNotification(
                    "Batch size must be between 1 and max concurrent events",
                    NotificationType.Warning);
                return false;
            }

            if (string.IsNullOrEmpty(TemplateStoragePath) || !Directory.Exists(TemplateStoragePath))
            {
                await _notificationService.ShowNotification(
                    "Template storage path must be a valid directory",
                    NotificationType.Warning);
                return false;
            }

            return true;
        }

        private async Task ResetToDefaultAsync()
        {
            var result = await _dialogService.ShowConfirmationDialog(
                "Are you sure you want to reset all settings to default values?",
                "Reset Settings");

            if (result)
            {
                _appSettings = new AppSettings();
                await LoadSettingsAsync();
                await _notificationService.ShowNotification(
                    "Settings reset to defaults",
                    NotificationType.Information);
            }
        }

        private async Task BrowseTemplatePathAsync()
        {
            var path = await _dialogService.ShowOpenFileDialog(
                "All Files (*.*)|*.*",
                "Select Template Storage Directory");

            if (!string.IsNullOrEmpty(path))
            {
                TemplateStoragePath = Path.GetDirectoryName(path);
                HasUnsavedChanges = true;
            }
        }

        private bool CanSaveSettings()
        {
            return HasUnsavedChanges && !IsLoading;
        }

        private void OnSettingsPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(HasUnsavedChanges) && 
                e.PropertyName != nameof(IsLoading))
            {
                HasUnsavedChanges = true;
            }
        }
    }
}