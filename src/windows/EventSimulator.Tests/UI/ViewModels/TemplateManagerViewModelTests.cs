using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Results;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;
using EventSimulator.Core.Constants;
using EventSimulator.UI.ViewModels;

namespace EventSimulator.Tests.UI.ViewModels
{
    [TestClass]
    public class TemplateManagerViewModelTests
    {
        private Mock<ITemplateManager> _templateManagerMock;
        private Mock<IDialogService> _dialogServiceMock;
        private Mock<INotificationService> _notificationServiceMock;
        private TemplateManagerViewModel _viewModel;
        private CancellationTokenSource _cancellationTokenSource;

        [TestInitialize]
        public void TestInitialize()
        {
            _templateManagerMock = new Mock<ITemplateManager>();
            _dialogServiceMock = new Mock<IDialogService>();
            _notificationServiceMock = new Mock<INotificationService>();
            _cancellationTokenSource = new CancellationTokenSource();

            _viewModel = new TemplateManagerViewModel(
                _templateManagerMock.Object,
                _dialogServiceMock.Object,
                _notificationServiceMock.Object);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _viewModel.Dispose();
        }

        [TestMethod]
        public async Task LoadTemplates_WithPagination_Success()
        {
            // Arrange
            var templates = new List<EventTemplate>
            {
                new EventTemplate { Id = 1, Name = "Template 1", Version = "1.0.0" },
                new EventTemplate { Id = 2, Name = "Template 2", Version = "1.0.0" }
            };

            var searchResult = (Templates: templates.AsEnumerable(), TotalCount: 10);
            _templateManagerMock.Setup(x => x.SearchTemplatesAsync(
                It.IsAny<TemplateSearchCriteria>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(searchResult));

            // Act
            await _viewModel.RefreshCommand.ExecuteAsync(null);

            // Assert
            Assert.AreEqual(2, _viewModel.Templates.Count);
            Assert.IsTrue(_viewModel.NextPageCommand.CanExecute(null));
            _templateManagerMock.Verify(x => x.SearchTemplatesAsync(
                It.IsAny<TemplateSearchCriteria>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task CreateTemplate_Success()
        {
            // Arrange
            var template = new EventTemplate
            {
                Name = "New Template",
                Description = "Test Description",
                Channel = EventLogChannels.Security,
                Level = EventLogLevels.Information
            };

            _templateManagerMock.Setup(x => x.CreateTemplateAsync(
                It.IsAny<EventTemplate>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(template));

            // Act
            await _viewModel.CreateTemplateCommand.ExecuteAsync(null);
            _viewModel.SelectedTemplate.Name = template.Name;
            _viewModel.SelectedTemplate.Description = template.Description;
            await _viewModel.SaveTemplateCommand.ExecuteAsync(null);

            // Assert
            Assert.IsFalse(_viewModel.IsEditing);
            _templateManagerMock.Verify(x => x.CreateTemplateAsync(
                It.Is<EventTemplate>(t => t.Name == template.Name),
                It.IsAny<CancellationToken>()), Times.Once);
            _notificationServiceMock.Verify(x => x.ShowSuccess(It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ValidateMitreMapping_Success()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Name = "Test Template",
                MitreAttackTechnique = "T1234.001"
            };

            var validationResults = new Dictionary<int, ValidationResult>
            {
                { 1, new ValidationResult { IsValid = true } }
            };

            _templateManagerMock.Setup(x => x.ValidateTemplatesAsync(
                It.IsAny<IEnumerable<EventTemplate>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success<IDictionary<int, ValidationResult>>(validationResults));

            // Act
            _viewModel.SelectedTemplate = template;
            await _viewModel.ValidateTemplateCommand.ExecuteAsync(null);

            // Assert
            Assert.AreEqual(0, _viewModel.ValidationErrors.Count);
            _notificationServiceMock.Verify(x => x.ShowSuccess(It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task SaveTemplate_WithVersioning_Success()
        {
            // Arrange
            var template = new EventTemplate
            {
                Id = 1,
                Name = "Test Template",
                Version = "1.0.0"
            };

            var updatedTemplate = template.Clone();
            updatedTemplate.Version = "1.1.0";

            _templateManagerMock.Setup(x => x.UpdateTemplateAsync(
                It.IsAny<EventTemplate>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(updatedTemplate));

            // Act
            _viewModel.SelectedTemplate = template;
            _viewModel.IsEditing = true;
            await _viewModel.SaveTemplateCommand.ExecuteAsync(null);

            // Assert
            _templateManagerMock.Verify(x => x.UpdateTemplateAsync(
                It.Is<EventTemplate>(t => t.Id == template.Id),
                It.IsAny<CancellationToken>()), Times.Once);
            _notificationServiceMock.Verify(x => x.ShowSuccess(It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task DeleteTemplate_WithConfirmation_Success()
        {
            // Arrange
            var template = new EventTemplate { Id = 1, Name = "Test Template" };
            _viewModel.SelectedTemplate = template;

            _dialogServiceMock.Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ReturnsAsync(true);

            _templateManagerMock.Setup(x => x.DeleteTemplateAsync(
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success());

            // Act
            await _viewModel.DeleteTemplateCommand.ExecuteAsync(null);

            // Assert
            Assert.IsNull(_viewModel.SelectedTemplate);
            _templateManagerMock.Verify(x => x.DeleteTemplateAsync(
                It.Is<int>(id => id == template.Id),
                It.IsAny<CancellationToken>()), Times.Once);
            _notificationServiceMock.Verify(x => x.ShowSuccess(It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task ImportTemplates_Success()
        {
            // Arrange
            var filePath = "templates.json";
            var templates = new List<EventTemplate>
            {
                new EventTemplate { Id = 1, Name = "Imported Template 1" },
                new EventTemplate { Id = 2, Name = "Imported Template 2" }
            };

            _dialogServiceMock.Setup(x => x.ShowOpenFileDialogAsync(
                It.IsAny<string>(),
                It.IsAny<string>()))
                .ReturnsAsync(filePath);

            _templateManagerMock.Setup(x => x.ImportTemplatesAsync(
                It.IsAny<IEnumerable<EventTemplate>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(templates.AsEnumerable()));

            // Act
            await _viewModel.ImportTemplatesCommand.ExecuteAsync(null);

            // Assert
            _templateManagerMock.Verify(x => x.ImportTemplatesAsync(
                It.IsAny<IEnumerable<EventTemplate>>(),
                It.IsAny<CancellationToken>()), Times.Once);
            _notificationServiceMock.Verify(x => x.ShowSuccess(It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task FilterTemplates_BySearchText_Success()
        {
            // Arrange
            var templates = new List<EventTemplate>
            {
                new EventTemplate { Id = 1, Name = "Security Template", MitreAttackTechnique = "T1234" },
                new EventTemplate { Id = 2, Name = "System Template", MitreAttackTechnique = "T5678" }
            };

            var searchResult = (Templates: templates.AsEnumerable(), TotalCount: 2);
            _templateManagerMock.Setup(x => x.SearchTemplatesAsync(
                It.IsAny<TemplateSearchCriteria>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(searchResult));

            // Act
            await _viewModel.RefreshCommand.ExecuteAsync(null);
            _viewModel.SearchText = "Security";

            // Assert
            Assert.AreEqual(1, _viewModel.FilteredTemplates.Count);
            Assert.AreEqual("Security Template", _viewModel.FilteredTemplates.First().Name);
        }

        [TestMethod]
        public void CancelEdit_ResetsState()
        {
            // Arrange
            var template = new EventTemplate { Id = 0, Name = "New Template" };
            _viewModel.SelectedTemplate = template;
            _viewModel.IsEditing = true;
            _viewModel.ValidationErrors.Add("Test Error");

            // Act
            _viewModel.CancelEditCommand.Execute(null);

            // Assert
            Assert.IsFalse(_viewModel.IsEditing);
            Assert.IsNull(_viewModel.SelectedTemplate);
            Assert.AreEqual(0, _viewModel.ValidationErrors.Count);
        }
    }
}