using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using FluentAssertions;
using EventSimulator.Core.Services;
using EventSimulator.Core.Models;
using EventSimulator.Core.Utils;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Constants;

namespace EventSimulator.Tests.Core
{
    [TestClass]
    public class EventGeneratorTests
    {
        private Mock<IEventValidator> _mockEventValidator;
        private Mock<WindowsEventLogApi> _mockEventLogApi;
        private Mock<ILogger<EventGeneratorService>> _mockLogger;
        private EventGeneratorService _eventGenerator;
        private CancellationTokenSource _cancellationTokenSource;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockEventValidator = new Mock<IEventValidator>(MockBehavior.Strict);
            _mockEventLogApi = new Mock<WindowsEventLogApi>(MockBehavior.Strict, new object[] { Mock.Of<ILogger<WindowsEventLogApi>>() });
            _mockLogger = new Mock<ILogger<EventGeneratorService>>();
            _cancellationTokenSource = new CancellationTokenSource();

            _eventGenerator = new EventGeneratorService(
                _mockEventValidator.Object,
                _mockEventLogApi.Object,
                _mockLogger.Object
            );
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _cancellationTokenSource?.Dispose();
            _eventGenerator?.Dispose();
            _mockEventValidator.Reset();
            _mockEventLogApi.Reset();
            _mockLogger.Reset();
        }

        [TestMethod]
        [Description("Verifies successful generation of a single valid event")]
        public async Task GenerateEventAsync_ValidEvent_ReturnsTrue()
        {
            // Arrange
            var eventInstance = new EventInstance(new EventTemplate(), _mockLogger.Object)
            {
                Id = 1,
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Security-Auditing",
                Parameters = { new EventParameter { Name = "SubjectUserName", Value = "TestUser" } }
            };

            _mockEventValidator
                .Setup(v => v.ValidateEventInstance(It.IsAny<EventInstance>()))
                .ReturnsAsync(true);

            _mockEventLogApi
                .Setup(a => a.WriteEvent(It.IsAny<EventInstance>()))
                .ReturnsAsync(true);

            // Act
            var result = await _eventGenerator.GenerateEventAsync(eventInstance, _cancellationTokenSource.Token);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.GeneratedEvent.Should().Be(eventInstance);
            result.GenerationTimeMs.Should().BeGreaterThan(0);

            _mockEventValidator.Verify(v => v.ValidateEventInstance(eventInstance), Times.Once);
            _mockEventLogApi.Verify(a => a.WriteEvent(eventInstance), Times.Once);
        }

        [TestMethod]
        [Description("Validates batch event generation meets 1000+ events/second requirement")]
        [Timeout(10000)] // 10 second timeout
        public async Task GenerateEventsAsync_BatchEvents_MeetsPerformanceRequirement()
        {
            // Arrange
            const int eventCount = 1000;
            var events = new List<EventInstance>();
            var template = new EventTemplate 
            { 
                Id = 1,
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Security-Auditing"
            };

            for (int i = 0; i < eventCount; i++)
            {
                events.Add(new EventInstance(template, _mockLogger.Object)
                {
                    Id = i + 1,
                    Parameters = { new EventParameter { Name = "SubjectUserName", Value = $"User{i}" } }
                });
            }

            var batchOptions = new BatchOptions
            {
                BatchSize = 100,
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                ContinueOnError = true
            };

            _mockEventValidator
                .Setup(v => v.ValidateEventInstance(It.IsAny<EventInstance>()))
                .ReturnsAsync(true);

            _mockEventLogApi
                .Setup(a => a.WriteEvent(It.IsAny<EventInstance>()))
                .ReturnsAsync(true);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await _eventGenerator.GenerateEventsAsync(events, batchOptions, _cancellationTokenSource.Token);
            stopwatch.Stop();

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.EventResults.Count.Should().Be(eventCount);
            result.SuccessCount.Should().Be(eventCount);
            result.EventsPerSecond.Should().BeGreaterOrEqualTo(1000, "Performance requirement of 1000+ events/second not met");

            _mockEventValidator.Verify(v => v.ValidateEventInstance(It.IsAny<EventInstance>()), Times.Exactly(eventCount));
            _mockEventLogApi.Verify(a => a.WriteEvent(It.IsAny<EventInstance>()), Times.Exactly(eventCount));
        }

        [TestMethod]
        [Description("Verifies thread-safe parallel event generation")]
        public async Task GenerateEventsAsync_ParallelExecution_ThreadSafe()
        {
            // Arrange
            const int batchCount = 4;
            const int eventsPerBatch = 250;
            var allTasks = new List<Task<BatchGenerationResult>>();
            var template = new EventTemplate
            {
                Id = 1,
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Security-Auditing"
            };

            _mockEventValidator
                .Setup(v => v.ValidateEventInstance(It.IsAny<EventInstance>()))
                .ReturnsAsync(true);

            _mockEventLogApi
                .Setup(a => a.WriteEvent(It.IsAny<EventInstance>()))
                .ReturnsAsync(true);

            var batchOptions = new BatchOptions
            {
                BatchSize = 50,
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                ContinueOnError = true
            };

            // Act
            for (int batch = 0; batch < batchCount; batch++)
            {
                var events = Enumerable.Range(1, eventsPerBatch).Select(i => new EventInstance(template, _mockLogger.Object)
                {
                    Id = (batch * eventsPerBatch) + i,
                    Parameters = { new EventParameter { Name = "BatchId", Value = batch.ToString() } }
                }).ToList();

                allTasks.Add(_eventGenerator.GenerateEventsAsync(events, batchOptions, _cancellationTokenSource.Token));
            }

            var results = await Task.WhenAll(allTasks);

            // Assert
            results.Should().HaveCount(batchCount);
            results.All(r => r.Success).Should().BeTrue();
            results.Sum(r => r.SuccessCount).Should().Be(batchCount * eventsPerBatch);
            
            _mockEventValidator.Verify(v => v.ValidateEventInstance(It.IsAny<EventInstance>()), 
                Times.Exactly(batchCount * eventsPerBatch));
            _mockEventLogApi.Verify(a => a.WriteEvent(It.IsAny<EventInstance>()), 
                Times.Exactly(batchCount * eventsPerBatch));
        }

        [TestMethod]
        [Description("Validates event generation accuracy from template")]
        public async Task GenerateFromTemplateAsync_ValidTemplate_GeneratesAccurateEvent()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Name = "Test Login Success",
                Description = "Successful login event template",
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Security-Auditing",
                Parameters = new List<EventParameter>
                {
                    new EventParameter { Name = "SubjectUserName", DataType = "string", IsRequired = true },
                    new EventParameter { Name = "LogonType", DataType = "int", IsRequired = true },
                    new EventParameter { Name = "WorkstationName", DataType = "string", IsRequired = false }
                }
            };

            var parameters = new Dictionary<string, object>
            {
                { "SubjectUserName", "TestUser" },
                { "LogonType", 2 },
                { "WorkstationName", "WORKSTATION01" }
            };

            _mockEventValidator
                .Setup(v => v.ValidateEventTemplate(template))
                .ReturnsAsync(true);

            _mockEventValidator
                .Setup(v => v.ValidateEventInstance(It.IsAny<EventInstance>()))
                .ReturnsAsync(true);

            _mockEventValidator
                .Setup(v => v.ValidateEventParameters(It.IsAny<EventInstance>(), template))
                .ReturnsAsync(true);

            _mockEventLogApi
                .Setup(a => a.WriteEvent(It.IsAny<EventInstance>()))
                .ReturnsAsync(true);

            // Act
            var result = await _eventGenerator.GenerateFromTemplateAsync(template, parameters, _cancellationTokenSource.Token);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.GeneratedEvent.Should().NotBeNull();
            result.GeneratedEvent.EventId.Should().Be(template.EventId);
            result.GeneratedEvent.Channel.Should().Be(template.Channel);
            result.GeneratedEvent.Level.Should().Be(template.Level);
            result.GeneratedEvent.Source.Should().Be(template.Source);
            result.AppliedParameters.Should().HaveCount(3);
            result.ProcessingTimeMs.Should().BeGreaterThan(0);

            _mockEventValidator.Verify(v => v.ValidateEventTemplate(template), Times.Once);
            _mockEventValidator.Verify(v => v.ValidateEventParameters(It.IsAny<EventInstance>(), template), Times.Once);
            _mockEventLogApi.Verify(a => a.WriteEvent(It.IsAny<EventInstance>()), Times.Once);
        }
    }
}