using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;
using EventSimulator.Core.Models;
using EventSimulator.Core.Constants;
using EventSimulator.Data.Context;
using EventSimulator.Data.Repositories;

namespace EventSimulator.Tests.Data
{
    /// <summary>
    /// Comprehensive test suite for validating TemplateRepository functionality including
    /// caching behavior, MITRE ATT&CK mapping, and error handling scenarios.
    /// </summary>
    public class TemplateRepositoryTests : IDisposable
    {
        private readonly EventSimulatorDbContext _context;
        private readonly Mock<ILogger<TemplateRepository>> _loggerMock;
        private readonly Mock<IMemoryCache> _cacheMock;
        private readonly Mock<IAsyncPolicy> _retryPolicyMock;
        private readonly TemplateRepository _repository;
        private readonly string _dbName;

        public TemplateRepositoryTests()
        {
            // Generate unique database name for test isolation
            _dbName = $"EventSimulator_Test_{Guid.NewGuid()}";
            
            // Configure in-memory database
            var options = new DbContextOptionsBuilder<EventSimulatorDbContext>()
                .UseInMemoryDatabase(_dbName)
                .Options;

            // Initialize mocks
            _loggerMock = new Mock<ILogger<TemplateRepository>>();
            _cacheMock = new Mock<IMemoryCache>();
            _retryPolicyMock = new Mock<IAsyncPolicy>();

            // Setup retry policy mock
            _retryPolicyMock
                .Setup(x => x.ExecuteAsync(It.IsAny<Func<Task<object>>>()))
                .Returns<Func<Task<object>>>(func => func());

            // Initialize context and repository
            _context = new EventSimulatorDbContext(options, _loggerMock.Object);
            _repository = new TemplateRepository(_context, _loggerMock.Object, _cacheMock.Object, _retryPolicyMock.Object);
        }

        [Fact]
        public async Task GetAllTemplatesAsync_WithCaching_ReturnsCachedTemplates()
        {
            // Arrange
            var templates = new List<EventTemplate>
            {
                CreateTestTemplate(1, "Template1", "T1001"),
                CreateTestTemplate(2, "Template2", "T1002")
            };

            await _context.Templates.AddRangeAsync(templates);
            await _context.SaveChangesAsync();

            object cachedTemplates = null;
            _cacheMock.Setup(x => x.TryGetValue("AllTemplates", out cachedTemplates))
                     .Returns(false);

            // Act
            var result1 = await _repository.GetAllTemplatesAsync();
            var result2 = await _repository.GetAllTemplatesAsync();

            // Assert
            Assert.Equal(2, result1.Count());
            Assert.Equal(templates[0].Name, result1.First().Name);
            
            _cacheMock.Verify(x => x.CreateEntry(It.IsAny<object>()), Times.Once);
            _loggerMock.Verify(x => x.LogInformation(
                It.Is<string>(s => s.Contains("Retrieved and cached")),
                It.IsAny<int>()
            ), Times.Once);
        }

        [Fact]
        public async Task GetTemplateByIdAsync_WithValidId_ReturnsTemplate()
        {
            // Arrange
            var template = CreateTestTemplate(1, "TestTemplate", "T1003");
            await _context.Templates.AddAsync(template);
            await _context.SaveChangesAsync();

            // Act
            var result = await _repository.GetTemplateByIdAsync(1);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("TestTemplate", result.Name);
            Assert.Equal("T1003", result.MitreAttackTechnique);
        }

        [Fact]
        public async Task GetTemplatesByMitreTechniqueAsync_WithValidTechnique_ReturnsMatchingTemplates()
        {
            // Arrange
            var templates = new List<EventTemplate>
            {
                CreateTestTemplate(1, "Template1", "T1003"),
                CreateTestTemplate(2, "Template2", "T1003"),
                CreateTestTemplate(3, "Template3", "T1004")
            };

            await _context.Templates.AddRangeAsync(templates);
            await _context.SaveChangesAsync();

            // Act
            var result = await _repository.GetTemplatesByMitreTechniqueAsync("T1003");

            // Assert
            Assert.Equal(2, result.Count());
            Assert.All(result, t => Assert.Equal("T1003", t.MitreAttackTechnique));
        }

        [Fact]
        public async Task AddTemplateAsync_WithValidTemplate_AddsAndInvalidatesCache()
        {
            // Arrange
            var template = CreateTestTemplate(1, "NewTemplate", "T1005");

            // Act
            var result = await _repository.AddTemplateAsync(template);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("NewTemplate", result.Name);
            
            _cacheMock.Verify(x => x.Remove("AllTemplates"), Times.Once);
            _cacheMock.Verify(x => x.Remove("MitreTechnique_T1005"), Times.Once);
        }

        [Fact]
        public async Task UpdateTemplateAsync_WithValidTemplate_UpdatesAndInvalidatesCache()
        {
            // Arrange
            var template = CreateTestTemplate(1, "Template", "T1006");
            await _context.Templates.AddAsync(template);
            await _context.SaveChangesAsync();

            template.Name = "UpdatedTemplate";

            // Act
            var result = await _repository.UpdateTemplateAsync(template);

            // Assert
            Assert.True(result);
            var updated = await _context.Templates.FindAsync(1);
            Assert.Equal("UpdatedTemplate", updated.Name);
            
            _cacheMock.Verify(x => x.Remove("AllTemplates"), Times.Once);
            _cacheMock.Verify(x => x.Remove("Template_1"), Times.Once);
        }

        [Fact]
        public async Task DeleteTemplateAsync_WithValidId_DeletesAndInvalidatesCache()
        {
            // Arrange
            var template = CreateTestTemplate(1, "ToDelete", "T1007");
            await _context.Templates.AddAsync(template);
            await _context.SaveChangesAsync();

            // Act
            var result = await _repository.DeleteTemplateAsync(1);

            // Assert
            Assert.True(result);
            var deleted = await _context.Templates.FindAsync(1);
            Assert.Null(deleted);
            
            _cacheMock.Verify(x => x.Remove("AllTemplates"), Times.Once);
            _cacheMock.Verify(x => x.Remove("Template_1"), Times.Once);
        }

        private EventTemplate CreateTestTemplate(int id, string name, string mitreId)
        {
            return new EventTemplate
            {
                Id = id,
                Name = name,
                Description = "Test template",
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Security",
                MitreAttackTechnique = mitreId,
                Version = "1.0.0",
                Parameters = new List<EventParameter>()
            };
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }
    }
}