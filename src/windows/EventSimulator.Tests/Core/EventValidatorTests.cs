using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using EventSimulator.Core.Constants;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;
using EventSimulator.Core.Services;

namespace EventSimulator.Tests.Core
{
    [TestClass]
    public class EventValidatorTests
    {
        private Mock<ILogger<EventValidatorService>> _loggerMock;
        private IEventValidator _validator;

        [TestInitialize]
        public void Initialize()
        {
            _loggerMock = new Mock<ILogger<EventValidatorService>>();
            _validator = new EventValidatorService(_loggerMock.Object);
        }

        [TestMethod]
        [TestCategory("Template Validation")]
        public async Task ValidateEventTemplate_WithValidTemplate_ReturnsTrue()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Name = "Valid Security Login",
                Description = "Template for successful login events",
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing",
                Version = "1.0.0",
                Parameters = new ConcurrentBag<EventParameter>
                {
                    new EventParameter 
                    { 
                        Name = "SubjectUserName",
                        DataType = "string",
                        IsRequired = true
                    }
                }
            };

            // Act
            var result = await _validator.ValidateEventTemplate(template);

            // Assert
            Assert.IsTrue(result);
            _loggerMock.Verify(x => x.LogInformation(It.IsAny<string>(), It.IsAny<object[]>()), Times.AtLeast(2));
        }

        [TestMethod]
        [TestCategory("Template Validation")]
        public async Task ValidateEventTemplate_WithInvalidChannel_ReturnsFalse()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Name = "Invalid Channel Template",
                Description = "Template with invalid channel",
                Channel = "InvalidChannel",
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing",
                Version = "1.0.0"
            };

            // Act
            var result = await _validator.ValidateEventTemplate(template);

            // Assert
            Assert.IsFalse(result);
            _loggerMock.Verify(x => x.LogError(It.Is<string>(s => s.Contains("Invalid channel")), It.IsAny<object[]>()), Times.Once);
        }

        [TestMethod]
        [TestCategory("Template Validation")]
        public async Task ValidateEventTemplate_WithInvalidEventId_ReturnsFalse()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Name = "Invalid EventId Template",
                Description = "Template with invalid event ID",
                Channel = EventLogChannels.Security,
                EventId = 70000, // Invalid: > 65535
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing",
                Version = "1.0.0"
            };

            // Act
            var result = await _validator.ValidateEventTemplate(template);

            // Assert
            Assert.IsFalse(result);
            _loggerMock.Verify(x => x.LogError(It.Is<string>(s => s.Contains("Invalid EventId")), It.IsAny<object[]>()), Times.Once);
        }

        [TestMethod]
        [TestCategory("Instance Validation")]
        public async Task ValidateEventInstance_WithValidInstance_ReturnsTrue()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing"
            };

            var instance = new EventInstance(template, _loggerMock.Object)
            {
                Id = 1,
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                Timestamp = DateTime.UtcNow
            };

            // Act
            var result = await _validator.ValidateEventInstance(instance);

            // Assert
            Assert.IsTrue(result);
            _loggerMock.Verify(x => x.LogInformation(It.IsAny<string>(), It.IsAny<object[]>()), Times.AtLeast(2));
        }

        [TestMethod]
        [TestCategory("Instance Validation")]
        public async Task ValidateEventInstance_WithFutureTimestamp_ReturnsFalse()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing"
            };

            var instance = new EventInstance(template, _loggerMock.Object)
            {
                Id = 1,
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                Timestamp = DateTime.UtcNow.AddDays(1) // Future timestamp
            };

            // Act
            var result = await _validator.ValidateEventInstance(instance);

            // Assert
            Assert.IsFalse(result);
            _loggerMock.Verify(x => x.LogError(It.Is<string>(s => s.Contains("Invalid timestamp")), It.IsAny<object[]>()), Times.Once);
        }

        [TestMethod]
        [TestCategory("Parameter Validation")]
        public async Task ValidateEventParameters_WithValidParameters_ReturnsTrue()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Parameters = new ConcurrentBag<EventParameter>
                {
                    new EventParameter
                    {
                        Name = "SubjectUserName",
                        DataType = "string",
                        IsRequired = true
                    }
                }
            };

            var instance = new EventInstance(template, _loggerMock.Object);
            instance.Parameters.Add(new EventParameter
            {
                Name = "SubjectUserName",
                DataType = "string",
                Value = "TestUser"
            });

            // Act
            var result = await _validator.ValidateEventParameters(instance, template);

            // Assert
            Assert.IsTrue(result);
            _loggerMock.Verify(x => x.LogInformation(It.IsAny<string>(), It.IsAny<object[]>()), Times.AtLeast(2));
        }

        [TestMethod]
        [TestCategory("Parameter Validation")]
        public async Task ValidateEventParameters_WithMissingRequiredParameter_ReturnsFalse()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Parameters = new ConcurrentBag<EventParameter>
                {
                    new EventParameter
                    {
                        Name = "SubjectUserName",
                        DataType = "string",
                        IsRequired = true
                    }
                }
            };

            var instance = new EventInstance(template, _loggerMock.Object);
            // Required parameter not added to instance

            // Act
            var result = await _validator.ValidateEventParameters(instance, template);

            // Assert
            Assert.IsFalse(result);
            _loggerMock.Verify(x => x.LogError(It.Is<string>(s => s.Contains("Missing required parameter")), It.IsAny<object[]>()), Times.Once);
        }

        [TestMethod]
        [TestCategory("Performance")]
        public async Task ValidateEventTemplate_Performance_HandlesLargeTemplate()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Name = "Large Template",
                Description = "Template with many parameters",
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing",
                Version = "1.0.0"
            };

            // Add 100 parameters to test performance
            for (int i = 0; i < 100; i++)
            {
                template.Parameters.Add(new EventParameter
                {
                    Name = $"Parameter{i}",
                    DataType = "string",
                    IsRequired = true,
                    Value = $"Value{i}"
                });
            }

            // Act
            var startTime = DateTime.UtcNow;
            var result = await _validator.ValidateEventTemplate(template);
            var duration = DateTime.UtcNow - startTime;

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(duration.TotalMilliseconds < 1000); // Should validate within 1 second
        }

        [TestMethod]
        [TestCategory("Thread Safety")]
        public async Task ValidateEventTemplate_ThreadSafety_HandlesParallelValidation()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Name = "Thread Safety Test",
                Description = "Template for parallel validation",
                Channel = EventLogChannels.Security,
                EventId = 4624,
                Level = EventLogLevels.Information,
                Source = "Microsoft-Windows-Security-Auditing",
                Version = "1.0.0"
            };

            // Act
            var tasks = new Task<bool>[10];
            for (int i = 0; i < 10; i++)
            {
                tasks[i] = _validator.ValidateEventTemplate(template);
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            Assert.IsTrue(Array.TrueForAll(results, r => r));
        }
    }
}