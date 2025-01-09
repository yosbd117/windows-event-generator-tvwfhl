using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Automation;
using System.Windows.Media;

namespace EventSimulator.UI.Services
{
    /// <summary>
    /// Defines the type of notification to be displayed
    /// </summary>
    public enum NotificationType
    {
        Information,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Interface defining the contract for notification and status management operations
    /// </summary>
    public interface INotificationService
    {
        Task ShowNotification(string message, NotificationType type, int durationMs = 3000);
        void UpdateStatusBar(string message, bool isProcessing);
        void ClearNotifications();
    }

    /// <summary>
    /// Provides comprehensive notification and status management functionality for the Windows Event Simulator
    /// Implements Material Design-based notifications with accessibility and internationalization support
    /// </summary>
    public class NotificationService : INotificationService, INotifyPropertyChanged
    {
        private readonly Snackbar _snackbar;
        private readonly Window _ownerWindow;
        private readonly DispatcherTimer _autoCloseTimer;
        private readonly ConcurrentQueue<NotificationMessage> _notificationQueue;
        private readonly SemaphoreSlim _notificationSemaphore;
        
        private string _currentStatusMessage;
        private bool _isProcessing;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Initializes a new instance of the NotificationService
        /// </summary>
        /// <param name="ownerWindow">The main window that owns this service</param>
        /// <param name="snackbar">The Material Design snackbar control for displaying notifications</param>
        /// <exception cref="ArgumentNullException">Thrown when required dependencies are null</exception>
        public NotificationService(Window ownerWindow, Snackbar snackbar)
        {
            _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
            _snackbar = snackbar ?? throw new ArgumentNullException(nameof(snackbar));
            
            _autoCloseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(3000)
            };
            _autoCloseTimer.Tick += AutoCloseTimer_Tick;

            _notificationQueue = new ConcurrentQueue<NotificationMessage>();
            _notificationSemaphore = new SemaphoreSlim(1, 1);

            ConfigureAccessibility();
        }

        /// <summary>
        /// Displays a notification with the specified message, type, and duration
        /// </summary>
        /// <param name="message">The message to display</param>
        /// <param name="type">The type of notification</param>
        /// <param name="durationMs">Duration in milliseconds to display the notification</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task ShowNotification(string message, NotificationType type, int durationMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(message))
                throw new ArgumentException("Message cannot be empty", nameof(message));

            var notificationMessage = new NotificationMessage
            {
                Message = message,
                Type = type,
                Duration = durationMs
            };

            _notificationQueue.Enqueue(notificationMessage);
            await ProcessNotificationQueue();
        }

        /// <summary>
        /// Updates the status bar with the specified message and processing state
        /// </summary>
        /// <param name="message">The status message to display</param>
        /// <param name="isProcessing">Indicates if the application is processing</param>
        public void UpdateStatusBar(string message, bool isProcessing)
        {
            _currentStatusMessage = message ?? string.Empty;
            _isProcessing = isProcessing;

            _ownerWindow.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(_currentStatusMessage));
                OnPropertyChanged(nameof(_isProcessing));
                UpdateStatusBarAccessibility();
            }, DispatcherPriority.Normal);
        }

        /// <summary>
        /// Clears all active notifications and resets the status
        /// </summary>
        public void ClearNotifications()
        {
            _ownerWindow.Dispatcher.InvokeAsync(() =>
            {
                _snackbar.MessageQueue?.Clear();
                _autoCloseTimer.Stop();
                
                while (_notificationQueue.TryDequeue(out _)) { }
                
                _notificationSemaphore.Release();
                UpdateStatusBar(string.Empty, false);
            }, DispatcherPriority.Normal);
        }

        private async Task ProcessNotificationQueue()
        {
            if (!await _notificationSemaphore.WaitAsync(0))
                return;

            try
            {
                while (_notificationQueue.TryDequeue(out var notification))
                {
                    await ShowNotificationInternal(notification);
                }
            }
            finally
            {
                _notificationSemaphore.Release();
            }
        }

        private async Task ShowNotificationInternal(NotificationMessage notification)
        {
            var snackbarMessage = new MaterialSnackbarMessage
            {
                Content = notification.Message,
                Background = GetNotificationBackground(notification.Type)
            };

            AutomationProperties.SetName(snackbarMessage, notification.Message);
            AutomationProperties.SetItemType(snackbarMessage, $"{notification.Type} Notification");

            _autoCloseTimer.Interval = TimeSpan.FromMilliseconds(notification.Duration);
            
            await _ownerWindow.Dispatcher.InvokeAsync(() =>
            {
                _snackbar.MessageQueue?.Enqueue(
                    snackbarMessage,
                    null,
                    null,
                    null,
                    false,
                    true,
                    TimeSpan.FromMilliseconds(notification.Duration)
                );
            }, DispatcherPriority.Normal);
        }

        private void AutoCloseTimer_Tick(object sender, EventArgs e)
        {
            _autoCloseTimer.Stop();
            _snackbar.MessageQueue?.Clear();
        }

        private void ConfigureAccessibility()
        {
            AutomationProperties.SetName(_snackbar, "Notification Area");
            AutomationProperties.SetItemType(_snackbar, "Notification Container");
            AutomationProperties.SetIsDialog(_snackbar, true);
        }

        private void UpdateStatusBarAccessibility()
        {
            var statusMessage = _isProcessing
                ? $"Processing: {_currentStatusMessage}"
                : _currentStatusMessage;

            AutomationProperties.SetName(_ownerWindow, statusMessage);
            AutomationProperties.SetHelpText(_ownerWindow, statusMessage);
        }

        private static SolidColorBrush GetNotificationBackground(NotificationType type)
        {
            return type switch
            {
                NotificationType.Success => new SolidColorBrush(Colors.Green),
                NotificationType.Warning => new SolidColorBrush(Colors.Orange),
                NotificationType.Error => new SolidColorBrush(Colors.Red),
                _ => new SolidColorBrush(Colors.Blue)
            };
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private class NotificationMessage
        {
            public string Message { get; set; }
            public NotificationType Type { get; set; }
            public int Duration { get; set; }
        }
    }
}