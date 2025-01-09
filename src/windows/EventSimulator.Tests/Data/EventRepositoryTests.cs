// External package versions:
// Microsoft.EntityFrameworkCore v6.0.0
// Microsoft.EntityFrameworkCore.InMemory v6.0.0
// Microsoft.Extensions.Logging v6.0.0
// Moq v4.18.0
// xunit v2.4.2
// BenchmarkDotNet v0.13.5

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using EventSimulator.Core.Models;
using EventSimulator.Core.Constants;
using EventSimulator.Data.Context;
using EventSimulator.Data.Repositories;

namespace EventSimulator.Tests.Data
{
    /// <summary>
    /// Comprehensive test suite for EventRepository class validating data access operations,
    /// performance benchmarks, and partition handling capabilities.
    /// </summary>
    public class EventRepositoryTests : IDisposable
    {
        private readonly EventSimulatorDbContext _context;
        private readonly EventRepository _repository;
        private readonly Mock<ILogger<EventRepository>> _loggerMock;
        private readonly ITestOutputHelper _output;
        private readonly string _databaseName;

        public EventRepositoryTests(ITestOutputHelper outputHelper)
        {
            _output = outputHelper;
            _databaseName = $"EventSimulator_{Guid.NewGuid()}";

            // Configure in-memory database
            var options = new DbContextOptionsBuilder<EventSimulatorDbContext>()
                .UseInMemoryDatabase(_databaseName)
                .EnableSensitiveDataLogging()
                .Options;

            _context = new EventSimulatorDbContext(options);
            _loggerMock = new Mock<ILogger<EventRepository>>();
            _repository = new EventRepository(_context, _loggerMock.Object);
        }

        [Fact]
        public async Task GetEventByIdAsync_ExistingEvent_ReturnsCorrectEvent()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            await _context.Events.AddAsync(testEvent);
            await _context.SaveChangesAsync();

            // Act
            var result = await _repository.GetEventByIdAsync(testEvent.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(testEvent.Id, result.Id);
            Assert.Equal(testEvent.EventId, result.EventId);
            Assert.Equal(testEvent.Channel, result.Channel);
            VerifyLogging(LogLevel.Debug, Times.Once());
        }

        [Fact]
        public async Task GetEventsByDateRangeAsync_ValidRange_ReturnsCorrectEvents()
        {
            // Arrange
            var startDate = DateTime.UtcNow.AddDays(-1);
            var endDate = DateTime.UtcNow.AddDays(1);
            await SeedTestEvents(10);

            // Act
            var results = await _repository.GetEventsByDateRangeAsync(startDate, endDate);

            // Assert
            Assert.NotEmpty(results);
            Assert.All(results, e => 
                Assert.True(e.Timestamp >= startDate && e.Timestamp <= endDate));
            VerifyLogging(LogLevel.Information, Times.Once());
        }

        [Fact]
        public async Task AddEventAsync_ValidEvent_AddsSuccessfully()
        {
            // Arrange
            var testEvent = CreateTestEvent();

            // Act
            var result = await _repository.AddEventAsync(testEvent);

            // Assert
            Assert.NotNull(result);
            Assert.NotEqual(0, result.Id);
            var savedEvent = await _context.Events.FindAsync(result.Id);
            Assert.NotNull(savedEvent);
            VerifyLogging(LogLevel.Information, Times.Once());
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        public async Task AddEventsAsync_BatchOperation_PerformsEfficiently(int eventCount)
        {
            // Arrange
            var events = Enumerable.Range(0, eventCount)
                .Select(_ => CreateTestEvent())
                .ToList();

            // Act
            var startTime = DateTime.UtcNow;
            var addedCount = await _repository.AddEventsAsync(events);
            var duration = DateTime.UtcNow - startTime;

            // Assert
            Assert.Equal(eventCount, addedCount);
            Assert.True(duration.TotalSeconds < eventCount / 1000.0); // Verify 1000+ events/second
            _output.WriteLine($"Batch insert of {eventCount} events took {duration.TotalSeconds:F2} seconds");
            VerifyLogging(LogLevel.Information, Times.AtLeast(2));
        }

        [Fact]
        public async Task UpdateEventStatusAsync_ValidEvent_UpdatesSuccessfully()
        {
            // Arrange
            var testEvent = CreateTestEvent();
            await _context.Events.AddAsync(testEvent);
            await _context.SaveChangesAsync();
            var newStatus = "Completed";

            // Act
            var result = await _repository.UpdateEventStatusAsync(testEvent.Id, newStatus);

            // Assert
            Assert.True(result);
            var updatedEvent = await _context.Events.FindAsync(testEvent.Id);
            Assert.Equal(newStatus, updatedEvent.Status);
            VerifyLogging(LogLevel.Information, Times.Once());
        }

        [Fact]
        public async Task RemoveOldEventsAsync_ExceedsRetentionPeriod_RemovesEvents()
        {
            // Arrange
            var oldEvent = CreateTestEvent();
            oldEvent.Timestamp = DateTime.UtcNow.AddDays(-91);
            await _context.Events.AddAsync(oldEvent);
            await _context.SaveChangesAsync();

            // Act
            var removedCount = await _repository.RemoveOldEventsAsync(90);

            // Assert
            Assert.Equal(1, removedCount);
            var remainingEvent = await _context.Events.FindAsync(oldEvent.Id);
            Assert.Null(remainingEvent);
            VerifyLogging(LogLevel.Information, Times.AtLeast(2));
        }

        [Benchmark]
        public async Task BatchOperationPerformanceBenchmark()
        {
            // Arrange
            const int batchSize = 10000;
            var events = Enumerable.Range(0, batchSize)
                .Select(_ => CreateTestEvent())
                .ToList();

            // Act
            var startTime = DateTime.UtcNow;
            await _repository.AddEventsAsync(events);
            var duration = DateTime.UtcNow - startTime;

            // Assert & Log
            var eventsPerSecond = batchSize / duration.TotalSeconds;
            _output.WriteLine($"Performance: {eventsPerSecond:F2} events/second");
            Assert.True(eventsPerSecond >= 1000, 
                $"Performance below requirement: {eventsPerSecond:F2} events/second");
        }

        private EventInstance CreateTestEvent()
        {
            return new EventInstance
            {
                Channel = EventLogChannels.Security,
                EventId = 4624, // Successful logon
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing",
                MachineName = Environment.MachineName,
                UserName = "SYSTEM",
                Timestamp = DateTime.UtcNow,
                Status = "Pending",
                Parameters = new System.Collections.Concurrent.ConcurrentBag<EventParameter>()
            };
        }

        private async Task SeedTestEvents(int count)
        {
            var events = Enumerable.Range(0, count)
                .Select(_ => CreateTestEvent())
                .ToList();

            await _context.Events.AddRangeAsync(events);
            await _context.SaveChangesAsync();
        }

        private void VerifyLogging(LogLevel level, Times times)
        {
            _loggerMock.Verify(
                x => x.Log(
                    It.Is<LogLevel>(l => l == level),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()),
                times);
        }

        public void Dispose()
        {
            _context.Database.EnsureDeleted();
            _context.Dispose();
        }
    }
}