using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventSimulator.Core.Constants;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;
using EventSimulator.UI.Services;
using EventSimulator.UI.ViewModels;
using FluentAssertions;
using Moq;
using Xunit;

namespace EventSimulator.Tests.UI.ViewModels
{
    /// <summary>
    /// Comprehensive test suite for EventGeneratorViewModel validating UI operations,
    /// event generation, and user feedback with high test coverage.
    /// </summary>
    public class EventGeneratorViewModelTests : IDisposable
    {
        private readonly Mock<IEventGenerator> _eventGeneratorMock;
        private readonly Mock<INotificationService> _notificationServiceMock;
        private readonly Mock<IPerformanceMonitor> _performanceMonitorMock;
        private readonly EventGeneratorViewModel _viewModel;
        private CancellationTokenSource _cancellationTokenSource;

        public EventGeneratorViewModelTests()
        {
            _eventGeneratorMock = new Mock<IEventGenerator>(MockBehavior.Strict);
            _notificationServiceMock = new Mock<INotificationService>(MockBehavior.Strict);
            _performanceMonitorMock = new Mock<IPerformanceMonitor>(MockBehavior.Strict);
            _cancellationTokenSource = new CancellationTokenSource();

            _viewModel = new EventGeneratorViewModel(
                _eventGeneratorMock.Object,
                _notificationServiceMock.Object,
                _performanceMonitorMock.Object);
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Dispose();
        }

        [Fact]
        public void Constructor_InitializesPropertiesCorrectly()
        {
            // Assert
            _viewModel.Templates.Should().NotBeNull();
            _viewModel.Parameters.Should().NotBeNull();
            _viewModel.GenerateCommand.Should().NotBeNull();
            _viewModel.CancelCommand.Should().NotBeNull();
            _viewModel.ValidateCommand.Should().NotBeNull();
            _viewModel.IsGenerating.Should().BeFalse();
            _viewModel.BatchSize.Should().Be(1000);
            _viewModel.IsBatchMode.Should().BeFalse();
            _viewModel.HasErrors.Should().BeFalse();
        }

        [Fact]
        public async Task GenerateEvent_WithValidTemplate_ShouldSucceed()
        {
            // Arrange
            var template = CreateValidTemplate();
            var parameters = new Dictionary<string, object>
            {
                { "UserName", "TestUser" },
                { "ProcessId", "1234" }
            };

            _viewModel.SelectedTemplate = template;
            SetupParameterValues(parameters);

            var result = new TemplateGenerationResult
            {
                Success = true,
                GeneratedEvent = new EventInstance(template, null),
                Messages = new List<string> { "Success" }
            };

            _eventGeneratorMock
                .Setup(x => x.GenerateFromTemplateAsync(
                    template,
                    It.IsAny<IDictionary<string, object>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);

            _notificationServiceMock
                .Setup(x => x.ShowNotification(
                    It.IsAny<string>(),
                    NotificationType.Success,
                    It.IsAny<int>()))
                .Returns(Task.CompletedTask);

            // Act
            await _viewModel.GenerateCommand.ExecuteAsync(null);

            // Assert
            _viewModel.EventCount.Should().Be(1);
            _viewModel.IsGenerating.Should().BeFalse();
            _viewModel.HasErrors.Should().BeFalse();

            _eventGeneratorMock.Verify(
                x => x.GenerateFromTemplateAsync(
                    template,
                    It.IsAny<IDictionary<string, object>>(),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task GenerateEvents_BatchMode_ShouldGenerateMultipleEvents()
        {
            // Arrange
            var template = CreateValidTemplate();
            _viewModel.SelectedTemplate = template;
            _viewModel.IsBatchMode = true;
            _viewModel.BatchSize = 100;

            var batchResult = new BatchGenerationResult
            {
                Success = true,
                SuccessCount = 100,
                FailureCount = 0,
                EventsPerSecond = 1000,
                EventResults = new List<EventGenerationResult>()
            };

            _eventGeneratorMock
                .Setup(x => x.GenerateEventsAsync(
                    It.IsAny<IEnumerable<EventInstance>>(),
                    It.IsAny<BatchOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(batchResult);

            _notificationServiceMock
                .Setup(x => x.ShowNotification(
                    It.IsAny<string>(),
                    NotificationType.Success,
                    It.IsAny<int>()))
                .Returns(Task.CompletedTask);

            // Act
            await _viewModel.GenerateCommand.ExecuteAsync(null);

            // Assert
            _viewModel.EventCount.Should().Be(100);
            _viewModel.GenerationRate.Should().Be(1000);
            _viewModel.IsGenerating.Should().BeFalse();
        }

        [Fact]
        public async Task CancelCommand_DuringGeneration_ShouldStopGeneration()
        {
            // Arrange
            var template = CreateValidTemplate();
            _viewModel.SelectedTemplate = template;
            _viewModel.IsBatchMode = true;

            var generationTask = Task.Delay(1000); // Simulate long-running generation

            _eventGeneratorMock
                .Setup(x => x.GenerateEventsAsync(
                    It.IsAny<IEnumerable<EventInstance>>(),
                    It.IsAny<BatchOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await generationTask;
                    throw new OperationCanceledException();
                });

            _notificationServiceMock
                .Setup(x => x.ShowNotification(
                    It.IsAny<string>(),
                    NotificationType.Information,
                    It.IsAny<int>()))
                .Returns(Task.CompletedTask);

            // Act
            var generateTask = _viewModel.GenerateCommand.ExecuteAsync(null);
            await Task.Delay(100); // Allow generation to start
            _viewModel.CancelCommand.Execute(null);
            await generateTask;

            // Assert
            _viewModel.IsGenerating.Should().BeFalse();
            _notificationServiceMock.Verify(
                x => x.ShowNotification(
                    "Event generation cancelled",
                    NotificationType.Information,
                    It.IsAny<int>()),
                Times.Once);
        }

        [Fact]
        public async Task ValidateTemplate_WithMissingRequiredParameters_ShouldFail()
        {
            // Arrange
            var template = CreateValidTemplate();
            template.Parameters.First().IsRequired = true;
            template.Parameters.First().Value = string.Empty;
            _viewModel.SelectedTemplate = template;

            _notificationServiceMock
                .Setup(x => x.ShowNotification(
                    It.IsAny<string>(),
                    NotificationType.Error,
                    It.IsAny<int>()))
                .Returns(Task.CompletedTask);

            // Act
            await _viewModel.ValidateCommand.ExecuteAsync(null);

            // Assert
            _viewModel.HasErrors.Should().BeTrue();
            _viewModel.GenerateCommand.CanExecute(null).Should().BeFalse();
        }

        private static EventTemplate CreateValidTemplate()
        {
            return new EventTemplate
            {
                Id = 1,
                Name = "Security Login Success",
                Description = "Successful user login event",
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing",
                Parameters = new[]
                {
                    new EventParameter
                    {
                        Name = "UserName",
                        DataType = "string",
                        IsRequired = true
                    },
                    new EventParameter
                    {
                        Name = "ProcessId",
                        DataType = "int",
                        IsRequired = true
                    }
                }
            };
        }

        private void SetupParameterValues(IDictionary<string, object> parameters)
        {
            foreach (var parameter in _viewModel.Parameters)
            {
                if (parameters.TryGetValue(parameter.Name, out var value))
                {
                    parameter.Value = value.ToString();
                }
            }
        }
    }
}