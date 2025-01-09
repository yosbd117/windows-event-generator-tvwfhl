// Microsoft.EntityFrameworkCore v6.0.0
// Microsoft.VisualStudio.TestTools.UnitTesting v2.2.0
// Moq v4.18.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using EventSimulator.Core.Models;
using EventSimulator.Data.Context;
using EventSimulator.Data.Repositories;

namespace EventSimulator.Tests.Data
{
    [TestClass]
    public class ScenarioRepositoryTests
    {
        private EventSimulatorDbContext _context;
        private ScenarioRepository _repository;
        private DbContextOptions<EventSimulatorDbContext> _options;
        private Mock<ILogger<ScenarioRepository>> _loggerMock;

        [TestInitialize]
        public void TestInitialize()
        {
            // Create unique database name for test isolation
            var databaseName = $"EventSimulator_Test_{Guid.NewGuid()}";
            _options = new DbContextOptionsBuilder<EventSimulatorDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options;

            // Initialize context and repository
            _context = new EventSimulatorDbContext(null, _options, null);
            _loggerMock = new Mock<ILogger<ScenarioRepository>>();
            _repository = new ScenarioRepository(_context, _loggerMock.Object, null);

            // Seed test data
            SeedTestData();
        }

        [TestMethod]
        public async Task TestCreateScenario_WithValidData_ShouldSucceed()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                Name = "Test Scenario",
                Description = "Test Description",
                Category = "Security",
                MitreAttackReference = "T1078",
                Version = new Version(1, 0),
                IsActive = true
            };

            // Act
            await _repository.CreateScenarioAsync(scenario);
            var result = await _context.Scenarios.FirstOrDefaultAsync(s => s.Name == "Test Scenario");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Test Scenario", result.Name);
            Assert.AreEqual("T1078", result.MitreAttackReference);
            _loggerMock.Verify(l => l.LogInformation(It.IsAny<string>(), It.IsAny<object[]>()), Times.Once);
        }

        [TestMethod]
        public async Task TestValidateMitreAttackTechniques_WithValidTechnique_ShouldPass()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                Name = "MITRE Test Scenario",
                Description = "Testing MITRE validation",
                MitreAttackReference = "T1078.002",
                Events = new System.Collections.Concurrent.ConcurrentBag<ScenarioEvent>
                {
                    new ScenarioEvent
                    {
                        TemplateId = 1,
                        Sequence = 1
                    }
                }
            };

            // Act
            var result = await _repository.ValidateMitreAttackTechniquesAsync(scenario);

            // Assert
            Assert.IsTrue(result.IsValid);
            Assert.AreEqual(0, result.Errors.Count);
        }

        [TestMethod]
        public async Task TestTimelineSequencing_WithCyclicDependency_ShouldFail()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                Name = "Cyclic Timeline Test",
                Description = "Testing cyclic dependencies",
                Events = new System.Collections.Concurrent.ConcurrentBag<ScenarioEvent>
                {
                    new ScenarioEvent { ScenarioEventId = 1, Sequence = 1, DependsOnEvents = new List<int> { 2 } },
                    new ScenarioEvent { ScenarioEventId = 2, Sequence = 2, DependsOnEvents = new List<int> { 1 } }
                }
            };

            // Act
            var result = await _repository.ValidateTimelineSequenceAsync(scenario);

            // Assert
            Assert.IsFalse(result.IsValid);
            Assert.IsTrue(result.Errors.Any(e => e.Contains("cyclic dependency")));
        }

        [TestMethod]
        public async Task TestConditionalTriggers_WithValidConditions_ShouldPass()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                Name = "Conditional Test",
                Description = "Testing conditional triggers",
                Events = new System.Collections.Concurrent.ConcurrentBag<ScenarioEvent>
                {
                    new ScenarioEvent
                    {
                        ScenarioEventId = 1,
                        Sequence = 1,
                        Conditions = System.Text.Json.JsonDocument.Parse(@"
                        {
                            'operator': 'and',
                            'conditions': [
                                {
                                    'operator': 'equals',
                                    'field': 'status',
                                    'value': 'active'
                                }
                            ]
                        }")
                    }
                }
            };

            // Act
            var result = await _repository.ValidateConditionalTriggersAsync(scenario);

            // Assert
            Assert.IsTrue(result.IsValid);
        }

        [TestMethod]
        public async Task TestConcurrentScenarioOperations_ShouldHandleConcurrency()
        {
            // Arrange
            var scenario = new ScenarioDefinition
            {
                Name = "Concurrency Test",
                Description = "Testing concurrent operations",
                Version = new Version(1, 0)
            };
            await _repository.CreateScenarioAsync(scenario);

            // Act
            var tasks = new List<Task>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    scenario.Description = $"Updated {Guid.NewGuid()}";
                    await _repository.UpdateScenarioAsync(scenario);
                }));
            }

            // Assert
            await Task.WhenAll(tasks);
            var updatedScenario = await _context.Scenarios.FindAsync(scenario.ScenarioId);
            Assert.IsNotNull(updatedScenario);
            Assert.AreNotEqual("Testing concurrent operations", updatedScenario.Description);
        }

        private void SeedTestData()
        {
            // Add test scenarios
            var testScenarios = new[]
            {
                new ScenarioDefinition
                {
                    Name = "Seed Scenario 1",
                    Description = "First test scenario",
                    Category = "Security",
                    MitreAttackReference = "T1078.001",
                    Version = new Version(1, 0),
                    IsActive = true
                },
                new ScenarioDefinition
                {
                    Name = "Seed Scenario 2",
                    Description = "Second test scenario",
                    Category = "System",
                    MitreAttackReference = "T1078.002",
                    Version = new Version(1, 0),
                    IsActive = true
                }
            };

            _context.Scenarios.AddRange(testScenarios);
            _context.SaveChanges();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _context.Dispose();
        }
    }
}