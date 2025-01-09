using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Mvvm.Collections;
using EventSimulator.Core.Models;
using EventSimulator.Core.Interfaces;
using EventSimulator.UI.Services;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace EventSimulator.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Log Viewer component providing comprehensive event log viewing and management capabilities
    /// with advanced filtering, virtual scrolling, and conformance validation features.
    /// </summary>
    public class LogViewerViewModel : INotifyPropertyChanged
    {
        private readonly IEventGenerator _eventGenerator;
        private readonly INotificationService _notificationService;
        private readonly BackgroundWorker _backgroundWorker;
        private VirtualizingCollection<EventInstance> _events;
        private EventInstance _selectedEvent;
        private string _searchText;
        private bool _isLoading;
        private int _totalEvents;
        private bool _showExtendedData;
        private double _conformanceRate;
        private bool _isFilterActive;
        private string _statusMessage;

        public event PropertyChangedEventHandler PropertyChanged;

        #region Properties

        public VirtualizingCollection<EventInstance> Events
        {
            get => _events;
            private set
            {
                _events = value;
                OnPropertyChanged(nameof(Events));
            }
        }

        public EventInstance SelectedEvent
        {
            get => _selectedEvent;
            set
            {
                _selectedEvent = value;
                OnPropertyChanged(nameof(SelectedEvent));
                ShowExtendedData = false;
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                FilterEventsCommand.Execute(null);
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public int TotalEvents
        {
            get => _totalEvents;
            set
            {
                _totalEvents = value;
                OnPropertyChanged(nameof(TotalEvents));
            }
        }

        public bool ShowExtendedData
        {
            get => _showExtendedData;
            set
            {
                _showExtendedData = value;
                OnPropertyChanged(nameof(ShowExtendedData));
            }
        }

        public ObservableCollection<string> FilterPresets { get; }

        public double ConformanceRate
        {
            get => _conformanceRate;
            private set
            {
                _conformanceRate = value;
                OnPropertyChanged(nameof(ConformanceRate));
            }
        }

        public bool IsFilterActive
        {
            get => _isFilterActive;
            private set
            {
                _isFilterActive = value;
                OnPropertyChanged(nameof(IsFilterActive));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        #endregion

        #region Commands

        public ICommand RefreshEventsCommand { get; }
        public ICommand FilterEventsCommand { get; }
        public ICommand ExportEventsCommand { get; }
        public ICommand ClearFiltersCommand { get; }
        public ICommand ValidateEventCommand { get; }

        #endregion

        /// <summary>
        /// Initializes a new instance of LogViewerViewModel with required dependencies
        /// </summary>
        public LogViewerViewModel(IEventGenerator eventGenerator, INotificationService notificationService)
        {
            _eventGenerator = eventGenerator ?? throw new ArgumentNullException(nameof(eventGenerator));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            // Initialize commands
            RefreshEventsCommand = new RelayCommand(async () => await RefreshEvents());
            FilterEventsCommand = new RelayCommand(async () => await FilterEvents());
            ExportEventsCommand = new RelayCommand(async () => await ExportEvents());
            ClearFiltersCommand = new RelayCommand(ClearFilters);
            ValidateEventCommand = new RelayCommand(async () => await ValidateSelectedEvent());

            // Initialize collections
            FilterPresets = new ObservableCollection<string>
            {
                "Security Events Only",
                "Critical and Error Events",
                "Last Hour Events",
                "Failed Validations",
                "Custom Filter..."
            };

            // Initialize background worker
            _backgroundWorker = new BackgroundWorker
            {
                WorkerReportsProgress = true,
                WorkerSupportsCancellation = true
            };
            _backgroundWorker.DoWork += BackgroundWorker_DoWork;
            _backgroundWorker.ProgressChanged += BackgroundWorker_ProgressChanged;
            _backgroundWorker.RunWorkerCompleted += BackgroundWorker_RunWorkerCompleted;

            // Initialize virtual collection
            InitializeVirtualCollection();
        }

        /// <summary>
        /// Refreshes the event log view with optimized loading and validation
        /// </summary>
        public async Task RefreshEvents()
        {
            try
            {
                IsLoading = true;
                _notificationService.UpdateStatusBar("Refreshing events...", true);

                await Task.Run(() =>
                {
                    Events.Refresh();
                    CalculateConformanceRate();
                });

                _notificationService.ShowNotification("Events refreshed successfully", NotificationType.Success);
            }
            catch (Exception ex)
            {
                _notificationService.ShowNotification($"Error refreshing events: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                IsLoading = false;
                _notificationService.UpdateStatusBar("Ready", false);
            }
        }

        /// <summary>
        /// Filters events based on search criteria with advanced filtering capabilities
        /// </summary>
        public async Task FilterEvents()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                IsFilterActive = false;
                await RefreshEvents();
                return;
            }

            try
            {
                IsLoading = true;
                IsFilterActive = true;
                _notificationService.UpdateStatusBar("Applying filters...", true);

                var filterRegex = new Regex(SearchText, RegexOptions.IgnoreCase);
                
                await Task.Run(() =>
                {
                    Events = new VirtualizingCollection<EventInstance>(
                        Events.Where(e => 
                            filterRegex.IsMatch(e.Source) ||
                            filterRegex.IsMatch(e.EventId.ToString()) ||
                            filterRegex.IsMatch(e.Channel) ||
                            filterRegex.IsMatch(e.Status)
                        ).ToList(),
                        50
                    );
                });

                UpdateFilterStatus();
            }
            catch (Exception ex)
            {
                _notificationService.ShowNotification($"Error applying filter: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                IsLoading = false;
                _notificationService.UpdateStatusBar("Filter applied", false);
            }
        }

        /// <summary>
        /// Exports filtered events to various formats with progress tracking
        /// </summary>
        public async Task ExportEvents()
        {
            try
            {
                IsLoading = true;
                _notificationService.UpdateStatusBar("Exporting events...", true);

                // Export implementation would go here
                await Task.Delay(100); // Placeholder for actual export logic

                _notificationService.ShowNotification("Events exported successfully", NotificationType.Success);
            }
            catch (Exception ex)
            {
                _notificationService.ShowNotification($"Export failed: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                IsLoading = false;
                _notificationService.UpdateStatusBar("Ready", false);
            }
        }

        /// <summary>
        /// Validates the selected event against Windows Event specifications
        /// </summary>
        private async Task ValidateSelectedEvent()
        {
            if (SelectedEvent == null) return;

            try
            {
                IsLoading = true;
                _notificationService.UpdateStatusBar("Validating event...", true);

                var validationResult = await Task.Run(() => SelectedEvent.Validate());
                
                if (validationResult == null)
                {
                    _notificationService.ShowNotification("Event validation successful", NotificationType.Success);
                }
                else
                {
                    _notificationService.ShowNotification($"Validation failed: {validationResult.ErrorMessage}", NotificationType.Warning);
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowNotification($"Validation error: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                IsLoading = false;
                _notificationService.UpdateStatusBar("Ready", false);
            }
        }

        private void InitializeVirtualCollection()
        {
            Events = new VirtualizingCollection<EventInstance>(new List<EventInstance>(), 50)
            {
                AllowMultiplePages = true,
                PageSize = 50
            };
        }

        private void CalculateConformanceRate()
        {
            if (Events == null || !Events.Any())
            {
                ConformanceRate = 100.0;
                return;
            }

            var validEvents = Events.Count(e => e.Validate() == null);
            ConformanceRate = (double)validEvents / Events.Count() * 100.0;
        }

        private void UpdateFilterStatus()
        {
            StatusMessage = IsFilterActive
                ? $"Showing {Events.Count()} filtered events"
                : $"Showing all {Events.Count()} events";
        }

        private void ClearFilters()
        {
            SearchText = string.Empty;
            IsFilterActive = false;
            RefreshEventsCommand.Execute(null);
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region Background Worker Event Handlers

        private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
        {
            // Background processing implementation
        }

        private void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // Progress update implementation
        }

        private void BackgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // Completion handling implementation
        }

        #endregion
    }
}