using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using EventSimulator.UI.ViewModels;

namespace EventSimulator.UI
{
    /// <summary>
    /// Main window class for the Windows Event Simulator application with DPI awareness
    /// and Material Design integration.
    /// </summary>
    public class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel;
        private bool _isClosing;
        private readonly PaletteHelper _paletteHelper;
        private double _lastDpiScale = 1.0;

        /// <summary>
        /// Initializes a new instance of MainWindow with proper DPI awareness and Material Design setup.
        /// </summary>
        /// <param name="viewModel">The main window view model.</param>
        public MainWindow(MainWindowViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _paletteHelper = new PaletteHelper();

            // Configure window properties
            Title = "Windows Event Simulator";
            MinWidth = 1024;
            MinHeight = 768;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Initialize window components
            InitializeComponent();
            DataContext = _viewModel;

            // Configure DPI awareness
            SourceInitialized += (s, e) =>
            {
                var presentationSource = PresentationSource.FromVisual(this);
                if (presentationSource?.CompositionTarget != null)
                {
                    _lastDpiScale = presentationSource.CompositionTarget.TransformToDevice.M11;
                }
            };

            // Register event handlers
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        /// <summary>
        /// Handles window loaded event with proper error handling and initialization.
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Configure Material Design theme
                var theme = _paletteHelper.GetTheme();
                theme.SetBaseTheme(Theme.Dark);
                theme.SetPrimaryColor(Colors.Blue);
                theme.SetSecondaryColor(Colors.LightBlue);
                _paletteHelper.SetTheme(theme);

                // Set up global exception handling
                Application.Current.DispatcherUnhandledException += (s, ex) =>
                {
                    MessageBox.Show(
                        $"An unexpected error occurred: {ex.Exception.Message}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    ex.Handled = true;
                };

                // Load window state if available
                if (Application.Current.Properties.Contains("WindowState"))
                {
                    var state = (WindowState)Application.Current.Properties["WindowState"];
                    WindowState = state;
                }

                if (Application.Current.Properties.Contains("WindowPosition"))
                {
                    var position = (Point)Application.Current.Properties["WindowPosition"];
                    Left = position.X;
                    Top = position.Y;
                }

                if (Application.Current.Properties.Contains("WindowSize"))
                {
                    var size = (Size)Application.Current.Properties["WindowSize"];
                    Width = size.Width;
                    Height = size.Height;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error initializing application: {ex.Message}",
                    "Initialization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handles window closing event with unsaved changes check and cleanup.
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_isClosing) return;
            _isClosing = true;

            try
            {
                // Check for unsaved changes
                if (_viewModel.HasUnsavedChanges)
                {
                    var result = MessageBox.Show(
                        "There are unsaved changes. Do you want to save before closing?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    switch (result)
                    {
                        case MessageBoxResult.Cancel:
                            e.Cancel = true;
                            _isClosing = false;
                            return;
                        case MessageBoxResult.Yes:
                            // Handle save operation
                            break;
                    }
                }

                // Save window state
                Application.Current.Properties["WindowState"] = WindowState;
                Application.Current.Properties["WindowPosition"] = new Point(Left, Top);
                Application.Current.Properties["WindowSize"] = new Size(Width, Height);

                // Cleanup resources
                _viewModel.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error during application shutdown: {ex.Message}",
                    "Shutdown Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handles DPI change events to maintain proper scaling.
        /// </summary>
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);

            try
            {
                // Calculate scaling factors
                var scaleFactor = newDpi.DpiScaleX / _lastDpiScale;
                _lastDpiScale = newDpi.DpiScaleX;

                // Update window dimensions
                Width *= scaleFactor;
                Height *= scaleFactor;
                Left *= scaleFactor;
                Top *= scaleFactor;

                // Update Material Design resources
                var theme = _paletteHelper.GetTheme();
                theme.SetResponsiveFont(newDpi.PixelsPerDip);
                _paletteHelper.SetTheme(theme);

                // Force layout update
                InvalidateVisual();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error handling DPI change: {ex.Message}",
                    "DPI Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}