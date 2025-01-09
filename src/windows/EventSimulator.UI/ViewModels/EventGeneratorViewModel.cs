using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Extensions.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;
using EventSimulator.UI.Services;
using EventSimulator.Core.Constants;

namespace EventSimulator.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Event Generator view that implements the MVVM pattern to manage event generation operations,
    /// template selection, parameter configuration, and user feedback with high-performance capabilities.
    /// </summary>
    public class EventGeneratorViewModel : ObservableObject
    {
        private readonly IEventGenerator _eventGenerator;
        private readonly INotificationService _notificationService;
        private readonly IPerformanceMonitor _performanceMonitor;
        private CancellationTokenSource _cancellationTokenSource;

        private EventTemplate _selectedTemplate;
        private bool _isGenerating;
        private int _eventCount;
        private bool _isBatchMode;
        private int _batchSize = 1000;
        private double _generationRate;
        private string _statusMessage;
        private bool _hasErrors;

        /// <summary>
        /// Collection of available event templates
        /// </summary>
        public ObservableCollection<EventTemplate> Templates { get; } = new();

        /// <summary>
        /// Collection of event parameters for the selected template
        /// </summary>
        public ObservableCollection<EventParameter> Parameters { get; } = new();

        /// <summary>
        /// Currently selected event template
        /// </summary>
        public EventTemplate SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value))
                {
                    OnTemplateSelected();
                }
            }
        }

        /// <summary>
        /// Indicates if event generation is in progress
        /// </summary>
        public bool IsGenerating
        {
            get => _isGenerating;
            private set => SetProperty(ref _isGenerating, value);
        }

        /// <summary>
        /// Number of events generated in current session
        /// </summary>
        public int EventCount
        {
            get => _eventCount;
            private set => SetProperty(ref _eventCount, value);
        }

        /// <summary>
        /// Indicates if batch mode is enabled
        /// </summary>
        public bool IsBatchMode
        {
            get => _isBatchMode;
            set => SetProperty(ref _isBatchMode, value);
        }

        /// <summary>
        /// Batch size for bulk event generation
        /// </summary>
        public int BatchSize
        {
            get => _batchSize;
            set => SetProperty(ref _batchSize, value);
        }

        /// <summary>
        /// Current event generation rate (events/second)
        /// </summary>
        public double GenerationRate
        {
            get => _generationRate;
            private set => SetProperty(ref _generationRate, value);
        }

        /// <summary>
        /// Current status message
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Indicates if there are validation errors
        /// </summary>
        public bool HasErrors
        {
            get => _hasErrors;
            private set => SetProperty(ref _hasErrors, value);
        }

        /// <summary>
        /// Command to generate events
        /// </summary>
        public IAsyncRelayCommand GenerateCommand { get; }

        /// <summary>
        /// Command to cancel event generation
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Command to validate current configuration
        /// </summary>
        public IAsyncRelayCommand ValidateCommand { get; }

        /// <summary>
        /// Initializes a new instance of EventGeneratorViewModel
        /// </summary>
        public EventGeneratorViewModel(
            IEventGenerator eventGenerator,
            INotificationService notificationService,
            IPerformanceMonitor performanceMonitor)
        {
            _eventGenerator = eventGenerator ?? throw new ArgumentNullException(nameof(eventGenerator));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));

            GenerateCommand = new AsyncRelayCommand(GenerateEventAsync, CanGenerate);
            CancelCommand = new RelayCommand(CancelGeneration, () => IsGenerating);
            ValidateCommand = new AsyncRelayCommand(ValidateTemplateAsync);

            InitializeTemplates();
        }

        private async Task GenerateEventAsync()
        {
            if (SelectedTemplate == null || !await ValidateTemplateAsync())
            {
                return;
            }

            try
            {
                IsGenerating = true;
                _cancellationTokenSource = new CancellationTokenSource();
                EventCount = 0;
                GenerationRate = 0;

                using var perfCounter = _performanceMonitor.BeginOperation("EventGeneration");

                if (IsBatchMode)
                {
                    await GenerateBatchEventsAsync(_cancellationTokenSource.Token);
                }
                else
                {
                    await GenerateSingleEventAsync(_cancellationTokenSource.Token);
                }

                perfCounter.SetSuccess();
                await _notificationService.ShowNotification(
                    $"Successfully generated {EventCount} events", 
                    NotificationType.Success);
            }
            catch (OperationCanceledException)
            {
                await _notificationService.ShowNotification(
                    "Event generation cancelled", 
                    NotificationType.Information);
            }
            catch (Exception ex)
            {
                HasErrors = true;
                await _notificationService.ShowNotification(
                    $"Error generating events: {ex.Message}", 
                    NotificationType.Error);
            }
            finally
            {
                IsGenerating = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                UpdateStatus("Ready");
            }
        }

        private async Task GenerateSingleEventAsync(CancellationToken cancellationToken)
        {
            var parameters = Parameters.ToDictionary(p => p.Name, p => (object)p.Value);
            var result = await _eventGenerator.GenerateFromTemplateAsync(
                SelectedTemplate, 
                parameters, 
                cancellationToken);

            if (result.Success)
            {
                EventCount++;
                GenerationRate = 1;
                UpdateStatus($"Generated event with ID {result.GeneratedEvent.EventId}");
            }
            else
            {
                HasErrors = true;
                throw new InvalidOperationException(
                    $"Failed to generate event: {string.Join(", ", result.Messages)}");
            }
        }

        private async Task GenerateBatchEventsAsync(CancellationToken cancellationToken)
        {
            var batchOptions = new BatchOptions
            {
                BatchSize = BatchSize,
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                ContinueOnError = true
            };

            var parameters = Parameters.ToDictionary(p => p.Name, p => (object)p.Value);
            var eventInstances = new List<EventInstance>();

            for (int i = 0; i < BatchSize; i++)
            {
                var instance = new EventInstance(SelectedTemplate, null)
                {
                    Parameters = new System.Collections.Concurrent.ConcurrentBag<EventParameter>(Parameters)
                };
                eventInstances.Add(instance);
            }

            var result = await _eventGenerator.GenerateEventsAsync(
                eventInstances, 
                batchOptions, 
                cancellationToken);

            EventCount += result.SuccessCount;
            GenerationRate = result.EventsPerSecond;

            if (result.FailureCount > 0)
            {
                HasErrors = true;
                throw new InvalidOperationException(
                    $"Batch generation completed with {result.FailureCount} failures");
            }

            UpdateStatus($"Generated {result.SuccessCount} events at {result.EventsPerSecond:F1} events/second");
        }

        private void CancelGeneration()
        {
            _cancellationTokenSource?.Cancel();
            UpdateStatus("Cancelling event generation...");
        }

        private async Task<bool> ValidateTemplateAsync()
        {
            if (SelectedTemplate == null)
            {
                await _notificationService.ShowNotification(
                    "Please select a template", 
                    NotificationType.Warning);
                return false;
            }

            var validationErrors = Parameters
                .Where(p => p.IsRequired && string.IsNullOrWhiteSpace(p.Value))
                .Select(p => p.Name)
                .ToList();

            if (validationErrors.Any())
            {
                HasErrors = true;
                await _notificationService.ShowNotification(
                    $"Required parameters missing: {string.Join(", ", validationErrors)}", 
                    NotificationType.Error);
                return false;
            }

            HasErrors = false;
            return true;
        }

        private void OnTemplateSelected()
        {
            if (SelectedTemplate == null)
            {
                Parameters.Clear();
                return;
            }

            Parameters.Clear();
            foreach (var parameter in SelectedTemplate.Parameters)
            {
                Parameters.Add(parameter.Clone());
            }

            UpdateStatus($"Selected template: {SelectedTemplate.Name}");
        }

        private bool CanGenerate()
        {
            return SelectedTemplate != null && !IsGenerating && !HasErrors;
        }

        private void UpdateStatus(string message)
        {
            StatusMessage = message;
            _notificationService.UpdateStatusBar(message, IsGenerating);
        }

        private void InitializeTemplates()
        {
            Templates.Add(new EventTemplate
            {
                Name = "Security Login Success",
                Description = "Successful user login event",
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing",
                MitreAttackTechnique = "T1078"
            });

            // Add more default templates as needed
        }
    }
}