using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Results;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventSimulator.Core.Interfaces;
using EventSimulator.Core.Models;
using EventSimulator.Core.Constants;

namespace EventSimulator.UI.ViewModels
{
    /// <summary>
    /// ViewModel for managing Windows Event Log templates with comprehensive CRUD operations,
    /// validation, filtering, and MITRE ATT&CK mapping support.
    /// </summary>
    public sealed class TemplateManagerViewModel : ObservableObject
    {
        private readonly ITemplateManager _templateManager;
        private readonly IDialogService _dialogService;
        private readonly INotificationService _notificationService;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _isBusy;
        private string _searchText;
        private string _statusMessage;
        private EventTemplate _selectedTemplate;
        private bool _isEditing;
        private int _currentPage = 1;
        private const int PageSize = 50;

        public ObservableCollection<EventTemplate> Templates { get; } = new();
        public ObservableCollection<EventTemplate> FilteredTemplates { get; } = new();
        public ObservableCollection<string> ValidationErrors { get; } = new();

        public ICommand CreateTemplateCommand { get; }
        public ICommand EditTemplateCommand { get; }
        public ICommand DeleteTemplateCommand { get; }
        public ICommand ImportTemplatesCommand { get; }
        public ICommand ExportTemplatesCommand { get; }
        public ICommand ValidateTemplateCommand { get; }
        public ICommand SaveTemplateCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PreviousPageCommand { get; }

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _ = FilterTemplatesAsync();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        public EventTemplate SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value))
                {
                    ValidationErrors.Clear();
                }
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            private set => SetProperty(ref _isEditing, value);
        }

        /// <summary>
        /// Initializes a new instance of the TemplateManagerViewModel with required services.
        /// </summary>
        public TemplateManagerViewModel(
            ITemplateManager templateManager,
            IDialogService dialogService,
            INotificationService notificationService)
        {
            _templateManager = templateManager ?? throw new ArgumentNullException(nameof(templateManager));
            _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _cancellationTokenSource = new CancellationTokenSource();

            // Initialize commands
            CreateTemplateCommand = new AsyncRelayCommand(CreateTemplateAsync, () => !IsBusy);
            EditTemplateCommand = new AsyncRelayCommand(EditTemplateAsync, () => !IsBusy && SelectedTemplate != null);
            DeleteTemplateCommand = new AsyncRelayCommand(DeleteTemplateAsync, () => !IsBusy && SelectedTemplate != null);
            ImportTemplatesCommand = new AsyncRelayCommand(ImportTemplatesAsync, () => !IsBusy);
            ExportTemplatesCommand = new AsyncRelayCommand(ExportTemplatesAsync, () => !IsBusy && FilteredTemplates.Any());
            ValidateTemplateCommand = new AsyncRelayCommand(ValidateTemplateAsync, () => !IsBusy && SelectedTemplate != null);
            SaveTemplateCommand = new AsyncRelayCommand(SaveTemplateAsync, () => !IsBusy && IsEditing);
            CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditing);
            RefreshCommand = new AsyncRelayCommand(RefreshTemplatesAsync, () => !IsBusy);
            NextPageCommand = new AsyncRelayCommand(NextPageAsync, () => !IsBusy);
            PreviousPageCommand = new AsyncRelayCommand(PreviousPageAsync, () => !IsBusy && _currentPage > 1);

            // Load initial data
            _ = LoadTemplatesAsync();
        }

        private async Task LoadTemplatesAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Loading templates...";

                var searchCriteria = new TemplateSearchCriteria
                {
                    Name = SearchText
                };

                var result = await _templateManager.SearchTemplatesAsync(
                    searchCriteria,
                    _currentPage,
                    PageSize,
                    _cancellationTokenSource.Token);

                if (result.IsSuccess)
                {
                    Templates.Clear();
                    foreach (var template in result.Value.Templates)
                    {
                        Templates.Add(template);
                    }
                    await FilterTemplatesAsync();
                    StatusMessage = $"Loaded {result.Value.TotalCount} templates";
                }
                else
                {
                    StatusMessage = "Failed to load templates";
                    _notificationService.ShowError("Error loading templates", result.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading templates";
                _notificationService.ShowError("Unexpected error", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task FilterTemplatesAsync()
        {
            FilteredTemplates.Clear();
            var filtered = Templates.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                filtered = filtered.Where(t =>
                    t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    t.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                    t.MitreAttackTechnique.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var template in filtered)
            {
                FilteredTemplates.Add(template);
            }

            StatusMessage = $"Displaying {FilteredTemplates.Count} templates";
        }

        private async Task CreateTemplateAsync()
        {
            try
            {
                var template = new EventTemplate
                {
                    Name = "New Template",
                    Description = "Enter description",
                    Channel = EventLogChannels.Security,
                    Level = EventLogLevels.Information
                };

                SelectedTemplate = template;
                IsEditing = true;
                StatusMessage = "Creating new template";
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("Error creating template", ex.Message);
            }
        }

        private async Task SaveTemplateAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Saving template...";

                var result = SelectedTemplate.Id == 0 ?
                    await _templateManager.CreateTemplateAsync(SelectedTemplate, _cancellationTokenSource.Token) :
                    await _templateManager.UpdateTemplateAsync(SelectedTemplate, _cancellationTokenSource.Token);

                if (result.IsSuccess)
                {
                    IsEditing = false;
                    await LoadTemplatesAsync();
                    _notificationService.ShowSuccess("Template saved successfully");
                }
                else
                {
                    ValidationErrors.Clear();
                    foreach (var error in result.Error.Split('\n'))
                    {
                        ValidationErrors.Add(error);
                    }
                    StatusMessage = "Failed to save template";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error saving template";
                _notificationService.ShowError("Unexpected error", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteTemplateAsync()
        {
            try
            {
                if (await _dialogService.ShowConfirmationAsync("Delete Template",
                    $"Are you sure you want to delete template '{SelectedTemplate.Name}'?"))
                {
                    IsBusy = true;
                    StatusMessage = "Deleting template...";

                    var result = await _templateManager.DeleteTemplateAsync(
                        SelectedTemplate.Id,
                        _cancellationTokenSource.Token);

                    if (result.IsSuccess)
                    {
                        await LoadTemplatesAsync();
                        SelectedTemplate = null;
                        _notificationService.ShowSuccess("Template deleted successfully");
                    }
                    else
                    {
                        StatusMessage = "Failed to delete template";
                        _notificationService.ShowError("Error deleting template", result.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error deleting template";
                _notificationService.ShowError("Unexpected error", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ImportTemplatesAsync()
        {
            try
            {
                var filePath = await _dialogService.ShowOpenFileDialogAsync(
                    "Import Templates",
                    "Template Files (*.json)|*.json|All Files (*.*)|*.*");

                if (string.IsNullOrEmpty(filePath)) return;

                IsBusy = true;
                StatusMessage = "Importing templates...";

                // Implementation of template import logic
                var templates = await LoadTemplatesFromFileAsync(filePath);
                var result = await _templateManager.ImportTemplatesAsync(templates, _cancellationTokenSource.Token);

                if (result.IsSuccess)
                {
                    await LoadTemplatesAsync();
                    _notificationService.ShowSuccess($"Successfully imported {result.Value.Count()} templates");
                }
                else
                {
                    StatusMessage = "Failed to import templates";
                    _notificationService.ShowError("Import error", result.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error importing templates";
                _notificationService.ShowError("Unexpected error", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ExportTemplatesAsync()
        {
            try
            {
                var filePath = await _dialogService.ShowSaveFileDialogAsync(
                    "Export Templates",
                    "Template Files (*.json)|*.json|All Files (*.*)|*.*",
                    "templates.json");

                if (string.IsNullOrEmpty(filePath)) return;

                IsBusy = true;
                StatusMessage = "Exporting templates...";

                var templateIds = FilteredTemplates.Select(t => t.Id).ToList();
                var result = await _templateManager.ExportTemplatesAsync(templateIds, _cancellationTokenSource.Token);

                if (result.IsSuccess)
                {
                    await SaveTemplatesToFileAsync(filePath, result.Value);
                    _notificationService.ShowSuccess($"Successfully exported {result.Value.Count()} templates");
                }
                else
                {
                    StatusMessage = "Failed to export templates";
                    _notificationService.ShowError("Export error", result.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error exporting templates";
                _notificationService.ShowError("Unexpected error", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ValidateTemplateAsync()
        {
            try
            {
                IsBusy = true;
                StatusMessage = "Validating template...";

                ValidationErrors.Clear();
                var templates = new[] { SelectedTemplate };
                var result = await _templateManager.ValidateTemplatesAsync(templates, _cancellationTokenSource.Token);

                if (result.IsSuccess)
                {
                    var validationResult = result.Value[SelectedTemplate.Id];
                    if (validationResult.IsValid)
                    {
                        _notificationService.ShowSuccess("Template validation successful");
                    }
                    else
                    {
                        foreach (var error in validationResult.Errors)
                        {
                            ValidationErrors.Add(error);
                        }
                        StatusMessage = "Template validation failed";
                    }
                }
                else
                {
                    StatusMessage = "Failed to validate template";
                    _notificationService.ShowError("Validation error", result.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error validating template";
                _notificationService.ShowError("Unexpected error", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void CancelEdit()
        {
            IsEditing = false;
            if (SelectedTemplate?.Id == 0)
            {
                SelectedTemplate = null;
            }
            ValidationErrors.Clear();
            StatusMessage = "Edit cancelled";
        }

        private async Task RefreshTemplatesAsync()
        {
            await LoadTemplatesAsync();
        }

        private async Task NextPageAsync()
        {
            _currentPage++;
            await LoadTemplatesAsync();
        }

        private async Task PreviousPageAsync()
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await LoadTemplatesAsync();
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
        }

        // Helper methods for file operations would be implemented here
        private Task<IEnumerable<EventTemplate>> LoadTemplatesFromFileAsync(string filePath)
        {
            // Implementation of file loading logic
            throw new NotImplementedException();
        }

        private Task SaveTemplatesToFileAsync(string filePath, IEnumerable<EventTemplate> templates)
        {
            // Implementation of file saving logic
            throw new NotImplementedException();
        }
    }
}