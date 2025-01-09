using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Windows.Automation;

namespace EventSimulator.UI.Services
{
    /// <summary>
    /// Interface defining dialog service operations with enhanced error handling and accessibility support
    /// </summary>
    public interface IDialogService : IDisposable
    {
        Task<bool> ShowConfirmationDialog(string message, string title, CancellationToken cancellationToken = default);
        Task<string> ShowOpenFileDialog(string filter, string title, CancellationToken cancellationToken = default);
        Task<string> ShowSaveFileDialog(string filter, string defaultFileName, CancellationToken cancellationToken = default);
        Task<string> ShowInputDialog(string message, string title, string defaultValue = "", CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Implementation of IDialogService providing dialog functionality with enhanced features
    /// </summary>
    public class DialogService : IDialogService
    {
        private readonly Window _ownerWindow;
        private readonly INotificationService _notificationService;
        private readonly ILogger<DialogService> _logger;
        private readonly ConcurrentDictionary<string, MaterialDialog> _dialogCache;
        private bool _isDisposed;

        /// <summary>
        /// Initializes a new instance of DialogService with enhanced configuration
        /// </summary>
        public DialogService(
            Window ownerWindow,
            INotificationService notificationService,
            ILogger<DialogService> logger)
        {
            _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dialogCache = new ConcurrentDictionary<string, MaterialDialog>();

            ConfigureDialogDefaults();
        }

        /// <summary>
        /// Shows a confirmation dialog with enhanced error handling and accessibility
        /// </summary>
        public async Task<bool> ShowConfirmationDialog(string message, string title, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Showing confirmation dialog: {Title}", title);

                if (string.IsNullOrWhiteSpace(message))
                    throw new ArgumentException("Message cannot be empty", nameof(message));

                var dialog = GetOrCreateDialog("confirmation");
                ConfigureDialogAccessibility(dialog, title, message);

                var dialogContent = new MaterialDialogContent
                {
                    Message = message,
                    Title = title,
                    IsCancellable = true
                };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30)); // Timeout after 30 seconds

                var result = await _ownerWindow.Dispatcher.InvokeAsync(async () =>
                {
                    var dialogResult = await dialog.ShowAsync(dialogContent, cts.Token);
                    return dialogResult == MaterialDialogResult.Confirmed;
                });

                _logger.LogInformation("Confirmation dialog result: {Result}", result);
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Confirmation dialog cancelled");
                await _notificationService.ShowNotification("Dialog operation cancelled", NotificationType.Warning);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing confirmation dialog");
                await _notificationService.ShowNotification("Error showing dialog", NotificationType.Error);
                return false;
            }
        }

        /// <summary>
        /// Shows a file open dialog with enhanced error handling and accessibility
        /// </summary>
        public async Task<string> ShowOpenFileDialog(string filter, string title, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Showing open file dialog: {Title}", title);

                var dialog = new OpenFileDialog
                {
                    Filter = filter,
                    Title = title,
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                ConfigureFileDialogAccessibility(dialog, title);

                var result = await _ownerWindow.Dispatcher.InvokeAsync(() =>
                {
                    return dialog.ShowDialog(_ownerWindow) == true ? dialog.FileName : null;
                });

                _logger.LogInformation("Open file dialog result: {Result}", result ?? "cancelled");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing open file dialog");
                await _notificationService.ShowNotification("Error showing file dialog", NotificationType.Error);
                return null;
            }
        }

        /// <summary>
        /// Shows a file save dialog with enhanced error handling and accessibility
        /// </summary>
        public async Task<string> ShowSaveFileDialog(string filter, string defaultFileName, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Showing save file dialog with default name: {FileName}", defaultFileName);

                var dialog = new SaveFileDialog
                {
                    Filter = filter,
                    FileName = defaultFileName,
                    OverwritePrompt = true,
                    ValidateNames = true
                };

                ConfigureFileDialogAccessibility(dialog, "Save File");

                var result = await _ownerWindow.Dispatcher.InvokeAsync(() =>
                {
                    return dialog.ShowDialog(_ownerWindow) == true ? dialog.FileName : null;
                });

                _logger.LogInformation("Save file dialog result: {Result}", result ?? "cancelled");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing save file dialog");
                await _notificationService.ShowNotification("Error showing save dialog", NotificationType.Error);
                return null;
            }
        }

        /// <summary>
        /// Shows an input dialog with enhanced error handling and accessibility
        /// </summary>
        public async Task<string> ShowInputDialog(string message, string title, string defaultValue = "", CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Showing input dialog: {Title}", title);

                var dialog = GetOrCreateDialog("input");
                ConfigureDialogAccessibility(dialog, title, message);

                var dialogContent = new MaterialDialogContent
                {
                    Message = message,
                    Title = title,
                    DefaultInput = defaultValue,
                    IsCancellable = true
                };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(60)); // Timeout after 60 seconds

                var result = await _ownerWindow.Dispatcher.InvokeAsync(async () =>
                {
                    var dialogResult = await dialog.ShowInputAsync(dialogContent, cts.Token);
                    return dialogResult?.Result;
                });

                _logger.LogInformation("Input dialog completed");
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Input dialog cancelled");
                await _notificationService.ShowNotification("Dialog operation cancelled", NotificationType.Warning);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing input dialog");
                await _notificationService.ShowNotification("Error showing input dialog", NotificationType.Error);
                return null;
            }
        }

        private MaterialDialog GetOrCreateDialog(string key)
        {
            return _dialogCache.GetOrAdd(key, _ => new MaterialDialog());
        }

        private void ConfigureDialogDefaults()
        {
            var defaultDialog = new MaterialDialog
            {
                Background = System.Windows.Media.Brushes.White,
                Foreground = System.Windows.Media.Brushes.Black,
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                MinWidth = 300,
                MinHeight = 150
            };

            _dialogCache.TryAdd("default", defaultDialog);
        }

        private void ConfigureDialogAccessibility(MaterialDialog dialog, string title, string message)
        {
            AutomationProperties.SetName(dialog, title);
            AutomationProperties.SetHelpText(dialog, message);
            AutomationProperties.SetIsDialog(dialog, true);
            AutomationProperties.SetLiveSetting(dialog, AutomationLiveSetting.Assertive);
        }

        private void ConfigureFileDialogAccessibility(FileDialog dialog, string title)
        {
            if (dialog is Window window)
            {
                AutomationProperties.SetName(window, title);
                AutomationProperties.SetIsDialog(window, true);
                AutomationProperties.SetLiveSetting(window, AutomationLiveSetting.Assertive);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                foreach (var dialog in _dialogCache.Values)
                {
                    dialog.Dispose();
                }
                _dialogCache.Clear();
            }

            _isDisposed = true;
        }
    }
}