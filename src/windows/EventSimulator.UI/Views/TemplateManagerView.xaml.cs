using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using EventSimulator.UI.ViewModels;

namespace EventSimulator.UI.Views
{
    /// <summary>
    /// Interaction logic for TemplateManagerView.xaml with enhanced validation,
    /// accessibility support, and Material Design integration.
    /// </summary>
    public partial class TemplateManagerView : UserControl
    {
        private CancellationTokenSource _validationCancellation;
        private const int ValidationDebounceMs = 500;
        private bool _isValidating;

        /// <summary>
        /// Gets the view model associated with this view.
        /// </summary>
        public TemplateManagerViewModel ViewModel => DataContext as TemplateManagerViewModel;

        /// <summary>
        /// Gets or sets whether template validation is in progress.
        /// </summary>
        public bool IsValidating
        {
            get => _isValidating;
            private set
            {
                _isValidating = value;
                if (ViewModel != null)
                {
                    ViewModel.IsBusy = value;
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the TemplateManagerView.
        /// </summary>
        public TemplateManagerView()
        {
            InitializeComponent();
            _validationCancellation = new CancellationTokenSource();

            // Set up accessibility properties
            AutomationProperties.SetName(this, "Template Manager");
            AutomationProperties.SetHelpText(this, "Manage Windows Event Log templates");

            // Register event handlers
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        /// <summary>
        /// Handles the view's loaded event.
        /// </summary>
        protected override void OnLoaded(object sender, RoutedEventArgs e)
        {
            base.OnLoaded(sender, e);

            // Configure Material Design theme
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(Theme.Dark);
            paletteHelper.SetTheme(theme);

            // Initialize view model if needed
            if (ViewModel == null)
            {
                throw new InvalidOperationException("TemplateManagerViewModel not found in DataContext");
            }

            // Set up template selection change handler
            if (templateListBox != null)
            {
                templateListBox.SelectionChanged += OnTemplateSelectionChanged;
            }

            // Set up search box handlers
            if (searchBox != null)
            {
                searchBox.TextChanged += OnSearchTextChanged;
                AutomationProperties.SetName(searchBox, "Search Templates");
            }

            // Configure validation feedback
            if (validationPanel != null)
            {
                AutomationProperties.SetName(validationPanel, "Validation Messages");
            }
        }

        /// <summary>
        /// Handles the view's unloaded event.
        /// </summary>
        protected override void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Clean up resources
            if (_validationCancellation != null)
            {
                _validationCancellation.Cancel();
                _validationCancellation.Dispose();
                _validationCancellation = null;
            }

            // Unregister event handlers
            if (templateListBox != null)
            {
                templateListBox.SelectionChanged -= OnTemplateSelectionChanged;
            }

            if (searchBox != null)
            {
                searchBox.TextChanged -= OnSearchTextChanged;
            }

            base.OnUnloaded(sender, e);
        }

        /// <summary>
        /// Handles template selection changes with validation.
        /// </summary>
        private async void OnTemplateSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel?.SelectedTemplate == null) return;

            // Cancel any ongoing validation
            _validationCancellation.Cancel();
            _validationCancellation = new CancellationTokenSource();

            try
            {
                await HandleTemplateValidation(ViewModel.SelectedTemplate, _validationCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Validation was cancelled, ignore
            }
            catch (Exception ex)
            {
                ShowError("Template Validation Error", ex.Message);
            }
        }

        /// <summary>
        /// Handles search text changes with debouncing.
        /// </summary>
        private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (ViewModel == null) return;

            // Debounce search input
            await Task.Delay(ValidationDebounceMs);
            
            if (sender is TextBox searchBox)
            {
                ViewModel.SearchText = searchBox.Text;
            }
        }

        /// <summary>
        /// Handles template validation with error feedback.
        /// </summary>
        private async Task<bool> HandleTemplateValidation(EventTemplate template, CancellationToken cancellationToken)
        {
            if (template == null) return false;

            IsValidating = true;
            ViewModel.ValidationErrors.Clear();

            try
            {
                // Validate template
                var templates = new[] { template };
                var result = await ViewModel.ValidateTemplateCommand.ExecuteAsync(null);

                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                // Update validation feedback
                if (validationPanel != null)
                {
                    validationPanel.Visibility = ViewModel.ValidationErrors.Count > 0 ? 
                        Visibility.Visible : Visibility.Collapsed;

                    // Update screen reader feedback
                    AutomationProperties.SetHelpText(validationPanel, 
                        ViewModel.ValidationErrors.Count > 0 ? 
                        "Template has validation errors" : "Template is valid");
                }

                return ViewModel.ValidationErrors.Count == 0;
            }
            finally
            {
                IsValidating = false;
            }
        }

        /// <summary>
        /// Shows an error message using Material Design dialog.
        /// </summary>
        private async void ShowError(string title, string message)
        {
            var dialog = new DialogHost();
            var content = new StackPanel
            {
                Margin = new Thickness(16)
            };

            content.Children.Add(new TextBlock
            {
                Text = title,
                Style = FindResource("MaterialDesignHeadline6TextBlock") as Style
            });

            content.Children.Add(new TextBlock
            {
                Text = message,
                Style = FindResource("MaterialDesignBody1TextBlock") as Style,
                Margin = new Thickness(0, 8, 0, 0)
            });

            var button = new Button
            {
                Content = "OK",
                Style = FindResource("MaterialDesignFlatButton") as Style,
                Command = DialogHost.CloseDialogCommand
            };

            content.Children.Add(button);

            await DialogHost.Show(content, "RootDialog");
        }
    }
}