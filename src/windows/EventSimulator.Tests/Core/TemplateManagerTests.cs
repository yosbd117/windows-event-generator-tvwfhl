using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventSimulator.Core.Services;
using EventSimulator.Core.Models;
using EventSimulator.Core.Constants;

namespace EventSimulator.Tests.Core
{
    [TestClass]
    public class TemplateManagerTests
    {
        private Mock<ILogger<TemplateManagerService>> _loggerMock;
        private Mock<ITemplateRepository> _repositoryMock;
        private TemplateManagerService _templateManager;
        private CancellationTokenSource _cancellationTokenSource;

        [TestInitialize]
        public void Initialize()
        {
            _loggerMock = new Mock<ILogger<TemplateManagerService>>();
            _repositoryMock = new Mock<ITemplateRepository>>();
            _cancellationTokenSource = new CancellationTokenSource();
            _templateManager = new TemplateManagerService(_loggerMock.Object, _repositoryMock.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _cancellationTokenSource?.Dispose();
        }

        [TestMethod]
        [Description("Verifies that template with valid MITRE ATT&CK technique passes validation")]
        public async Task TestMitreAttackValidation_ValidTechnique_Success()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Name = "Test Template",
                Description = "Test Description",
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Security",
                MitreAttackTechnique = "T1078.002"
            };

            _repositoryMock.Setup(r => r.ValidateMitreAttackTechniqueAsync(
                It.IsAny<string>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _templateManager.ValidateMitreAttackMappingAsync(
                template, 
                _cancellationTokenSource.Token);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.Value.IsValid);
            Assert.AreEqual(0, result.Value.Errors.Count);
        }

        [TestMethod]
        [Description("Tests thread safety of template operations under concurrent access")]
        public async Task TestConcurrentOperations_MultipleCalls_ThreadSafe()
        {
            // Arrange
            var templates = new List<EventTemplate>();
            for (int i = 0; i < 10; i++)
            {
                templates.Add(new EventTemplate
                {
                    Id = i,
                    Name = $"Template {i}",
                    Description = $"Description {i}",
                    Channel = EventLogChannels.Security,
                    EventId = 4624,
                    Level = EventLogLevels.Information,
                    Source = "Security"
                });
            }

            _repositoryMock.Setup(r => r.CreateAsync(
                It.IsAny<EventTemplate>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((EventTemplate t, CancellationToken ct) => t);

            // Act
            var tasks = new List<Task<Result<EventTemplate>>>();
            foreach (var template in templates)
            {
                tasks.Add(_templateManager.CreateTemplateAsync(template, _cancellationTokenSource.Token));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.AreEqual(templates.Count, results.Length);
            foreach (var result in results)
            {
                Assert.IsTrue(result.IsSuccess);
            }
        }

        [TestMethod]
        [Description("Verifies template version is incremented on update")]
        public async Task TestTemplateVersioning_UpdateTemplate_VersionIncremented()
        {
            // Arrange
            var originalTemplate = new EventTemplate
            {
                Id = 1,
                Name = "Test Template",
                Description = "Test Description",
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Security",
                Version = "1.0.0"
            };

            _repositoryMock.Setup(r => r.GetByIdAsync(
                It.IsAny<int>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(originalTemplate);

            _repositoryMock.Setup(r => r.UpdateAsync(
                It.IsAny<EventTemplate>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync((EventTemplate t, CancellationToken ct) => t);

            // Act
            var updatedTemplate = originalTemplate.Clone();
            updatedTemplate.Description = "Updated Description";
            var result = await _templateManager.UpdateTemplateAsync(updatedTemplate, _cancellationTokenSource.Token);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.AreNotEqual(originalTemplate.Version, result.Value.Version);
            var versionParts = result.Value.Version.Split('.');
            Assert.AreEqual("1.1.0", result.Value.Version);
        }

        [TestMethod]
        [Description("Tests error handling for invalid JSON during template import")]
        public async Task TestImportTemplates_InvalidJson_ThrowsException()
        {
            // Arrange
            var invalidTemplates = new List<EventTemplate>
            {
                new EventTemplate
                {
                    Id = 1,
                    // Missing required Name field
                    Channel = EventLogChannels.Security,
                    EventId = 4624,
                    Level = EventLogLevels.Information,
                    Source = "Security"
                }
            };

            // Act
            var result = await _templateManager.ImportTemplatesAsync(invalidTemplates, _cancellationTokenSource.Token);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [TestMethod]
        [Description("Verifies export functionality with large template datasets")]
        public async Task TestExportTemplates_LargeDataset_HandlesCorrectly()
        {
            // Arrange
            var templateIds = new List<int>();
            var templates = new List<EventTemplate>();
            for (int i = 0; i < 1000; i++)
            {
                templateIds.Add(i);
                templates.Add(new EventTemplate
                {
                    Id = i,
                    Name = $"Template {i}",
                    Description = $"Description {i}",
                    Channel = EventLogChannels.Security,
                    EventId = 4624,
                    Level = EventLogLevels.Information,
                    Source = "Security",
                    Version = "1.0.0"
                });
            }

            _repositoryMock.Setup(r => r.GetByIdsAsync(
                It.IsAny<IEnumerable<int>>(), 
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(templates);

            // Act
            var result = await _templateManager.ExportTemplatesAsync(templateIds, _cancellationTokenSource.Token);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(1000, result.Value.Count());
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.AtLeastOnce);
        }
    }
}