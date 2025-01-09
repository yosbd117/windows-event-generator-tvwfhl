// External package versions:
// Microsoft.Toolkit.Mvvm.ComponentModel v8.0.0
// Microsoft.Toolkit.Mvvm.Input v8.0.0
// System.Collections.ObjectModel v6.0.0

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;

namespace EventSimulator.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Scenario Builder interface that enables users to create, edit,
    /// and execute complex Windows Event simulation scenarios with timeline-based sequencing,
    /// conditional triggers, and comprehensive progress tracking.
    /// </summary>
    public class ScenarioBuilderViewModel : ObservableObject
    {
        private readonly IScenarioManager _scenarioManager;
        private readonly IDialogService _dialogService;
        private readonly INotificationService _notificationService;
        private CancellationTokenSource _cancellationTokenSource;
        private IProgress<ScenarioProgress> _progressReporter;

        private ObservableCollection<ScenarioDefinition> _scenarios;
        private ScenarioDefinition _selectedScenario;
        private bool _isExecuting;
        private bool _isSaving;
        private double _executionProgress;
        private string _statusMessage;

        /// <summary>
        /// Gets the collection of available scenarios.
        /// </summary>
        public ObservableCollection<ScenarioDefinition> Scenarios
        {
            get => _scenarios;
            private set => SetProperty(ref _scenarios, value);
        }

        /// <summary>
        /// Gets or sets the currently selected scenario.
        /// </summary>
        public ScenarioDefinition SelectedScenario
        {
            get => _selectedScenario;
            set
            {
                if (SetProperty(ref _selectedScenario, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether a scenario is currently executing.
        /// </summary>
        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (SetProperty(ref _isExecuting, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the scenario is being saved.
        /// </summary>
        public bool IsSaving
        {
            get => _isSaving;
            private set
            {
                if (SetProperty(ref _isSaving, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        /// <summary>
        /// Gets or sets the current execution progress (0-100).
        /// </summary>
        public double ExecutionProgress
        {
            get => _executionProgress;
            private set => SetProperty(ref _executionProgress, value);
        }

        /// <summary>
        /// Gets or sets the current status message.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        // Commands
        public IAsyncRelayCommand CreateScenarioCommand { get; }
        public IAsyncRelayCommand SaveScenarioCommand { get; }
        public IAsyncRelayCommand DeleteScenarioCommand { get; }
        public IAsyncRelayCommand ExecuteScenarioCommand { get; }
        public IRelayCommand CancelExecutionCommand { get; }
        public IAsyncRelayCommand ValidateScenarioCommand { get; }

        /// <summary>
        /// Initializes a new instance of the ScenarioBuilderViewModel class.
        /// </summary>
        public ScenarioBuilderViewModel(
            IScenarioManager scenarioManager,
            IDialogService dialogService,
            INotificationService notificationService)
        {
            _scenarioManager = scenarioManager ?? throw new ArgumentNullException(nameof(scenarioManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            // Initialize collections
            _scenarios = new ObservableCollection<ScenarioDefinition>();

            // Initialize commands
            CreateScenarioCommand = new AsyncRelayCommand(CreateScenarioAsync, CanCreateScenario);
            SaveScenarioCommand = new AsyncRelayCommand(SaveScenarioAsync, CanSaveScenario);
            DeleteScenarioCommand = new AsyncRelayCommand(DeleteScenarioAsync, CanDeleteScenario);
            ExecuteScenarioCommand = new AsyncRelayCommand(ExecuteScenarioAsync, CanExecuteScenario);
            CancelExecutionCommand = new RelayCommand(CancelExecution, CanCancelExecution);
            ValidateScenarioCommand = new AsyncRelayCommand(ValidateScenarioAsync, CanValidateScenario);

            // Initialize progress reporting
            _progressReporter = new Progress<ScenarioProgress>(UpdateProgress);

            // Load initial data
            _ = LoadScenariosAsync();
        }

        private async Task LoadScenariosAsync()
        {
            try
            {
                StatusMessage = "Loading scenarios...";
                var scenarios = await _scenarioManager.GetScenariosAsync();
                Scenarios.Clear();
                foreach (var scenario in scenarios)
                {
                    Scenarios.Add(scenario);
                }
                StatusMessage = $"Loaded {scenarios.Count} scenarios";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading scenarios";
                await _notificationService.ShowErrorAsync("Failed to load scenarios", ex.Message);
            }
        }

        private async Task CreateScenarioAsync()
        {
            try
            {
                IsSaving = true;
                StatusMessage = "Creating new scenario...";

                var newScenario = new ScenarioDefinition
                {
                    Name = "New Scenario",
                    Description = "Enter scenario description",
                    IsActive = false
                };

                var result = await _scenarioManager.CreateScenarioAsync(newScenario);
                Scenarios.Add(result);
                SelectedScenario = result;
                StatusMessage = "New scenario created";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error creating scenario";
                await _notificationService.ShowErrorAsync("Failed to create scenario", ex.Message);
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task SaveScenarioAsync()
        {
            if (SelectedScenario == null) return;

            try
            {
                IsSaving = true;
                StatusMessage = "Saving scenario...";

                var result = await _scenarioManager.UpdateScenarioAsync(SelectedScenario);
                var index = Scenarios.IndexOf(SelectedScenario);
                Scenarios[index] = result;
                SelectedScenario = result;
                StatusMessage = "Scenario saved successfully";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error saving scenario";
                await _notificationService.ShowErrorAsync("Failed to save scenario", ex.Message);
            }
            finally
            {
                IsSaving = false;
            }
        }

        private async Task DeleteScenarioAsync()
        {
            if (SelectedScenario == null) return;

            try
            {
                var confirm = await _dialogService.ShowConfirmationAsync(
                    "Delete Scenario",
                    $"Are you sure you want to delete '{SelectedScenario.Name}'?");

                if (!confirm) return;

                StatusMessage = "Deleting scenario...";
                await _scenarioManager.DeleteScenarioAsync(SelectedScenario.ScenarioId);
                Scenarios.Remove(SelectedScenario);
                SelectedScenario = null;
                StatusMessage = "Scenario deleted successfully";
            }
            catch (Exception ex)
            {
                StatusMessage = "Error deleting scenario";
                await _notificationService.ShowErrorAsync("Failed to delete scenario", ex.Message);
            }
        }

        private async Task ExecuteScenarioAsync()
        {
            if (SelectedScenario == null) return;

            try
            {
                IsExecuting = true;
                ExecutionProgress = 0;
                StatusMessage = "Validating scenario...";

                var validationResult = await ValidateScenarioAsync();
                if (!validationResult)
                {
                    return;
                }

                _cancellationTokenSource = new CancellationTokenSource();
                StatusMessage = "Executing scenario...";

                var options = new ExecutionOptions
                {
                    ValidateBeforeExecution = true,
                    ContinueOnError = false
                };

                var result = await _scenarioManager.ExecuteScenarioAsync(
                    SelectedScenario.ScenarioId,
                    options,
                    _progressReporter,
                    _cancellationTokenSource.Token);

                if (result.Success)
                {
                    StatusMessage = $"Execution completed: {result.EventsGenerated} events generated";
                }
                else
                {
                    StatusMessage = $"Execution failed: {result.ErrorMessage}";
                    await _notificationService.ShowErrorAsync("Execution Failed", result.ErrorMessage);
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Execution cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = "Execution error";
                await _notificationService.ShowErrorAsync("Execution Error", ex.Message);
            }
            finally
            {
                IsExecuting = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private void CancelExecution()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling execution...";
        }

        private async Task<bool> ValidateScenarioAsync()
        {
            if (SelectedScenario == null) return false;

            try
            {
                StatusMessage = "Validating scenario...";
                var validationOptions = new ValidationOptions
                {
                    ValidateDependencies = true,
                    ValidateMitreReferences = true,
                    ValidateParameters = true,
                    StrictTemplateValidation = true
                };

                await _scenarioManager.ValidateScenarioAsync(SelectedScenario, validationOptions);
                StatusMessage = "Validation successful";
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = "Validation failed";
                await _notificationService.ShowErrorAsync("Validation Error", ex.Message);
                return false;
            }
        }

        private void UpdateProgress(ScenarioProgress progress)
        {
            ExecutionProgress = (double)progress.EventsCompleted / progress.TotalEvents * 100;
            StatusMessage = $"Executing: {progress.CurrentPhase} ({progress.EventsCompleted}/{progress.TotalEvents})";
            
            if (!string.IsNullOrEmpty(progress.LastError))
            {
                _notificationService.ShowWarning("Event Generation Warning", progress.LastError);
            }
        }

        private void UpdateCommandStates()
        {
            ((AsyncRelayCommand)CreateScenarioCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)SaveScenarioCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)DeleteScenarioCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ExecuteScenarioCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelExecutionCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)ValidateScenarioCommand).NotifyCanExecuteChanged();
        }

        private bool CanCreateScenario() => !IsExecuting && !IsSaving;
        private bool CanSaveScenario() => SelectedScenario != null && !IsExecuting && !IsSaving;
        private bool CanDeleteScenario() => SelectedScenario != null && !IsExecuting && !IsSaving;
        private bool CanExecuteScenario() => SelectedScenario != null && !IsExecuting && !IsSaving;
        private bool CanCancelExecution() => IsExecuting && _cancellationTokenSource != null;
        private bool CanValidateScenario() => SelectedScenario != null && !IsExecuting && !IsSaving;
    }
}