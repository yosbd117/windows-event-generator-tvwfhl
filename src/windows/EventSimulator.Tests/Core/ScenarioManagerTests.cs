using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using FluentAssertions;
using EventSimulator.Core.Services;
using EventSimulator.Core.Models;
using EventSimulator.Core.Interfaces;

namespace EventSimulator.Tests.Core
{
    [TestClass]
    public class ScenarioManagerTests
    {
        private Mock<ILogger<ScenarioManagerService>> _loggerMock;
        private Mock<IEventGenerator> _eventGeneratorMock;
        private Mock<IEventValidator> _validatorMock;
        private Mock<IMemoryCache> _cacheMock;
        private ScenarioManagerService _scenarioManager;
        private TestContext _testContext;

        public ScenarioManagerTests(TestContext testContext)
        {
            _testContext = testContext ?? throw new ArgumentNullException(nameof(testContext));
        }

        [TestInitialize]
        public void TestInitialize()
        {
            _loggerMock = new Mock<ILogger<ScenarioManagerService>>();
            _eventGeneratorMock = new Mock<IEventGenerator>();
            _validatorMock = new Mock<IEventValidator>();
            _cacheMock = new Mock<IMemoryCache>();

            _scenarioManager = new ScenarioManagerService(
                _eventGeneratorMock.Object,
                _cacheMock.Object,
                _loggerMock.Object);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _scenarioManager.Dispose();
        }

        [TestMethod]
        [Description("Validates successful scenario creation with valid input")]
        public async Task CreateScenarioAsync_ValidScenario_Success()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                Name = "Test Scenario",
                Description = "Test Description",
                Category = "Security",
                MitreAttackReference = "T1078",
                Events = new ConcurrentBag<ScenarioEvent>
                {
                    new ScenarioEvent
                    {
                        ScenarioEventId = 1,
                        TemplateId = 1,
                        Sequence = 0,
                        DelayMilliseconds = 1000
                    }
                }
            };

            // Act
            var result = await _scenarioManager.CreateScenarioAsync(scenario);

            // Assert
            result.Should().NotBeNull();
            result.Name.Should().Be(scenario.Name);
            result.IsActive.Should().BeTrue();
            result.Version.Should().Be(new Version(1, 0));
            result.CreatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [TestMethod]
        [Description("Validates scenario update with version increment")]
        public async Task UpdateScenarioAsync_ValidUpdate_VersionIncremented()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                ScenarioId = 1,
                Name = "Test Scenario",
                Version = new Version(1, 0),
                Events = new ConcurrentBag<ScenarioEvent>()
            };

            // Act
            var result = await _scenarioManager.UpdateScenarioAsync(scenario);

            // Assert
            result.Version.Should().Be(new Version(1, 1));
            result.ModifiedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        }

        [TestMethod]
        [Description("Validates scenario deletion with active execution")]
        public async Task DeleteScenarioAsync_ActiveExecution_ThrowsException()
        {
            // Arrange
            int scenarioId = 1;
            var executionOptions = new ExecutionOptions();
            
            // Start execution to create active state
            _ = _scenarioManager.ExecuteScenarioAsync(scenarioId, executionOptions);

            // Act & Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await _scenarioManager.DeleteScenarioAsync(scenarioId, false));
        }

        [TestMethod]
        [Description("Validates successful scenario execution")]
        public async Task ExecuteScenarioAsync_ValidScenario_Success()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                ScenarioId = 1,
                Name = "Test Execution",
                Events = new ConcurrentBag<ScenarioEvent>
                {
                    new ScenarioEvent { ScenarioEventId = 1, Sequence = 0 },
                    new ScenarioEvent { ScenarioEventId = 2, Sequence = 1 }
                }
            };

            var options = new ExecutionOptions
            {
                ValidateBeforeExecution = true,
                ContinueOnError = false
            };

            _eventGeneratorMock.Setup(x => x.GenerateEventAsync(
                It.IsAny<EventInstance>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EventGenerationResult { Success = true });

            // Act
            var result = await _scenarioManager.ExecuteScenarioAsync(
                scenario.ScenarioId, 
                options,
                new Progress<ScenarioProgress>());

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.EventsGenerated.Should().Be(2);
        }

        [TestMethod]
        [Description("Validates performance requirements for batch execution")]
        [Timeout(5000)] // 5 second timeout
        public async Task BatchExecuteScenarioAsync_Performance_MeetsRequirements()
        {
            // Arrange
            const int eventCount = 1000; // Test 1000 events/second requirement
            var events = new List<ScenarioEvent>();
            for (int i = 0; i < eventCount; i++)
            {
                events.Add(new ScenarioEvent
                {
                    ScenarioEventId = i,
                    Sequence = i,
                    DelayMilliseconds = 0
                });
            }

            var scenario = new ScenarioDefinition
            {
                ScenarioId = 1,
                Name = "Performance Test",
                Events = new ConcurrentBag<ScenarioEvent>(events)
            };

            var options = new ExecutionOptions
            {
                ValidateBeforeExecution = true,
                ContinueOnError = true,
                DelayMultiplier = 0.1 // Speed up for testing
            };

            _eventGeneratorMock.Setup(x => x.GenerateEventAsync(
                It.IsAny<EventInstance>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new EventGenerationResult { Success = true });

            // Act
            var startTime = DateTime.UtcNow;
            var result = await _scenarioManager.ExecuteScenarioAsync(
                scenario.ScenarioId,
                options,
                new Progress<ScenarioProgress>());
            var duration = DateTime.UtcNow - startTime;

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.EventsGenerated.Should().Be(eventCount);
            
            // Verify performance requirement: 1000+ events per second
            var eventsPerSecond = eventCount / duration.TotalSeconds;
            eventsPerSecond.Should().BeGreaterOrEqualTo(1000);
        }

        [TestMethod]
        [Description("Validates cyclic dependency detection")]
        public async Task ValidateScenarioAsync_CyclicDependency_ValidationFails()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                Name = "Cyclic Test",
                Events = new ConcurrentBag<ScenarioEvent>
                {
                    new ScenarioEvent 
                    { 
                        ScenarioEventId = 1, 
                        Sequence = 0,
                        DependsOnEvents = new List<int> { 2 }
                    },
                    new ScenarioEvent 
                    { 
                        ScenarioEventId = 2, 
                        Sequence = 1,
                        DependsOnEvents = new List<int> { 1 }
                    }
                }
            };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ValidationException>(
                async () => await _scenarioManager.CreateScenarioAsync(scenario));
        }

        [TestMethod]
        [Description("Validates MITRE ATT&CK reference format")]
        public async Task CreateScenarioAsync_InvalidMitreReference_ValidationFails()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                Name = "MITRE Test",
                MitreAttackReference = "Invalid",
                Events = new ConcurrentBag<ScenarioEvent>()
            };

            // Act & Assert
            await Assert.ThrowsExceptionAsync<ValidationException>(
                async () => await _scenarioManager.CreateScenarioAsync(scenario));
        }

        [TestMethod]
        [Description("Validates concurrent execution limits")]
        public async Task ExecuteScenarioAsync_ExceedConcurrentLimit_ThrowsException()
        {
            // Arrange
            const int maxConcurrent = 5;
            var tasks = new List<Task>();
            var options = new ExecutionOptions();

            // Act
            for (int i = 0; i < maxConcurrent + 1; i++)
            {
                tasks.Add(_scenarioManager.ExecuteScenarioAsync(i, options));
            }

            // Assert
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                async () => await Task.WhenAll(tasks));
        }

        [TestMethod]
        [Description("Validates scenario execution cancellation")]
        public async Task ExecuteScenarioAsync_Cancellation_StopsExecution()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                ScenarioId = 1,
                Events = new ConcurrentBag<ScenarioEvent>
                {
                    new ScenarioEvent { ScenarioEventId = 1, DelayMilliseconds = 5000 }
                }
            };

            var options = new ExecutionOptions();
            var cts = new CancellationTokenSource();

            // Act
            var executionTask = _scenarioManager.ExecuteScenarioAsync(
                scenario.ScenarioId,
                options,
                null,
                cts.Token);

            cts.Cancel();

            // Assert
            var result = await executionTask;
            result.Success.Should().BeFalse();
            result.ErrorMessage.Should().Contain("cancelled");
        }
    }
}