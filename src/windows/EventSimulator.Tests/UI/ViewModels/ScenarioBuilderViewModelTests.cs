// External package versions:
// xunit v2.4.2
// Moq v4.18.0
// FluentAssertions v6.8.0

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;
using EventSimulator.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace EventSimulator.Tests.UI.ViewModels
{
    /// <summary>
    /// Comprehensive unit tests for the ScenarioBuilderViewModel covering scenario management,
    /// MITRE ATT&CK compliance, execution control, and progress tracking.
    /// </summary>
    public class ScenarioBuilderViewModelTests : IDisposable
    {
        private readonly Mock<IScenarioManager> _scenarioManagerMock;
        private readonly Mock<IDialogService> _dialogServiceMock;
        private readonly Mock<INotificationService> _notificationServiceMock;
        private readonly ScenarioBuilderViewModel _viewModel;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public ScenarioBuilderViewModelTests()
        {
            _scenarioManagerMock = new Mock<IScenarioManager>();
            _dialogServiceMock = new Mock<IDialogService>();
            _notificationServiceMock = new Mock<INotificationService>();
            _cancellationTokenSource = new CancellationTokenSource();

            _viewModel = new ScenarioBuilderViewModel(
                _scenarioManagerMock.Object,
                _dialogServiceMock.Object,
                _notificationServiceMock.Object);
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Dispose();
        }

        [Fact]
        public async Task CreateScenario_WithValidMitreReference_CreatesAndValidates()
        {
            // Arrange
            var newScenario = new ScenarioDefinition
            {
                Name = "Test Scenario",
                Description = "Test Description",
                MitreAttackReference = "T1078.002",
                IsActive = false
            };

            _scenarioManagerMock.Setup(x => x.CreateScenarioAsync(
                It.IsAny<ScenarioDefinition>(),
                It.IsAny<ValidationOptions>()))
                .ReturnsAsync(newScenario);

            // Act
            await _viewModel.CreateScenarioCommand.ExecuteAsync(null);

            // Assert
            _scenarioManagerMock.Verify(x => x.CreateScenarioAsync(
                It.Is<ScenarioDefinition>(s => s.Name == "New Scenario"),
                It.Is<ValidationOptions>(v => v.ValidateMitreReferences)), Times.Once);

            _viewModel.Scenarios.Should().Contain(newScenario);
            _viewModel.SelectedScenario.Should().Be(newScenario);
            _viewModel.StatusMessage.Should().Be("New scenario created");
        }

        [Fact]
        public async Task ExecuteScenario_WithProgressTracking_UpdatesProgressAndNotifies()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                ScenarioId = 1,
                Name = "Test Scenario",
                IsActive = true
            };

            var executionResult = new ExecutionResult
            {
                Success = true,
                EventsGenerated = 100,
                ExecutionDuration = TimeSpan.FromSeconds(10)
            };

            _viewModel.SelectedScenario = scenario;

            _scenarioManagerMock.Setup(x => x.ValidateScenarioAsync(
                It.IsAny<ScenarioDefinition>(),
                It.IsAny<ValidationOptions>()))
                .Returns(Task.CompletedTask);

            _scenarioManagerMock.Setup(x => x.ExecuteScenarioAsync(
                It.IsAny<int>(),
                It.IsAny<ExecutionOptions>(),
                It.IsAny<IProgress<ScenarioProgress>>(),
                It.IsAny<CancellationToken>()))
                .Callback<int, ExecutionOptions, IProgress<ScenarioProgress>, CancellationToken>(
                    (_, _, progress, _) =>
                    {
                        progress.Report(new ScenarioProgress
                        {
                            EventsCompleted = 50,
                            TotalEvents = 100,
                            CurrentPhase = "Processing"
                        });
                    })
                .ReturnsAsync(executionResult);

            // Act
            await _viewModel.ExecuteScenarioCommand.ExecuteAsync(null);

            // Assert
            _viewModel.ExecutionProgress.Should().Be(50);
            _viewModel.IsExecuting.Should().BeFalse();
            _viewModel.StatusMessage.Should().Be("Execution completed: 100 events generated");

            _scenarioManagerMock.Verify(x => x.ExecuteScenarioAsync(
                scenario.ScenarioId,
                It.Is<ExecutionOptions>(o => o.ValidateBeforeExecution),
                It.IsAny<IProgress<ScenarioProgress>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveScenario_WithMitreCompliance_ValidatesAndSaves()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                ScenarioId = 1,
                Name = "Test Scenario",
                MitreAttackReference = "T1078.002",
                IsActive = true
            };

            _viewModel.SelectedScenario = scenario;

            _scenarioManagerMock.Setup(x => x.UpdateScenarioAsync(
                It.IsAny<ScenarioDefinition>(),
                It.IsAny<ValidationOptions>()))
                .ReturnsAsync(scenario);

            // Act
            await _viewModel.SaveScenarioCommand.ExecuteAsync(null);

            // Assert
            _scenarioManagerMock.Verify(x => x.UpdateScenarioAsync(
                scenario,
                It.Is<ValidationOptions>(v => v.ValidateMitreReferences)), Times.Once);

            _viewModel.StatusMessage.Should().Be("Scenario saved successfully");
            _viewModel.IsSaving.Should().BeFalse();
        }

        [Fact]
        public async Task DeleteScenario_WithConfirmation_RemovesScenario()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                ScenarioId = 1,
                Name = "Test Scenario"
            };

            _viewModel.Scenarios.Add(scenario);
            _viewModel.SelectedScenario = scenario;

            _dialogServiceMock.Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ReturnsAsync(true);

            _scenarioManagerMock.Setup(x => x.DeleteScenarioAsync(
                It.IsAny<int>(),
                It.IsAny<bool>()))
                .ReturnsAsync(true);

            // Act
            await _viewModel.DeleteScenarioCommand.ExecuteAsync(null);

            // Assert
            _viewModel.Scenarios.Should().NotContain(scenario);
            _viewModel.SelectedScenario.Should().BeNull();
            _viewModel.StatusMessage.Should().Be("Scenario deleted successfully");

            _scenarioManagerMock.Verify(x => x.DeleteScenarioAsync(
                scenario.ScenarioId,
                false), Times.Once);
        }

        [Fact]
        public async Task CancelExecution_DuringProgress_StopsGracefully()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                ScenarioId = 1,
                Name = "Test Scenario"
            };

            _viewModel.SelectedScenario = scenario;

            var executionStarted = new TaskCompletionSource<bool>();
            var cancellationRequested = new TaskCompletionSource<bool>();

            _scenarioManagerMock.Setup(x => x.ExecuteScenarioAsync(
                It.IsAny<int>(),
                It.IsAny<ExecutionOptions>(),
                It.IsAny<IProgress<ScenarioProgress>>(),
                It.IsAny<CancellationToken>()))
                .Callback<int, ExecutionOptions, IProgress<ScenarioProgress>, CancellationToken>(
                    async (_, _, _, ct) =>
                    {
                        executionStarted.SetResult(true);
                        await Task.Delay(1000, ct);
                        cancellationRequested.SetResult(true);
                    })
                .Returns<int, ExecutionOptions, IProgress<ScenarioProgress>, CancellationToken>(
                    async (_, _, _, ct) =>
                    {
                        await Task.Delay(1000, ct);
                        return new ExecutionResult { Success = false };
                    });

            // Act
            var executionTask = _viewModel.ExecuteScenarioCommand.ExecuteAsync(null);
            await executionStarted.Task;
            _viewModel.CancelExecutionCommand.Execute(null);
            await cancellationRequested.Task;

            // Assert
            _viewModel.IsExecuting.Should().BeFalse();
            _viewModel.StatusMessage.Should().Be("Execution cancelled");
        }

        [Fact]
        public async Task ValidateScenario_WithInvalidMitreReference_ShowsError()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                ScenarioId = 1,
                Name = "Test Scenario",
                MitreAttackReference = "Invalid"
            };

            _viewModel.SelectedScenario = scenario;

            _scenarioManagerMock.Setup(x => x.ValidateScenarioAsync(
                It.IsAny<ScenarioDefinition>(),
                It.IsAny<ValidationOptions>()))
                .ThrowsAsync(new ValidationException("Invalid MITRE ATT&CK reference"));

            // Act
            await _viewModel.ValidateScenarioCommand.ExecuteAsync(null);

            // Assert
            _viewModel.StatusMessage.Should().Be("Validation failed");
            _notificationServiceMock.Verify(x => x.ShowErrorAsync(
                "Validation Error",
                "Invalid MITRE ATT&CK reference"), Times.Once);
        }
    }
}