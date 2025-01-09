using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MaterialDesignThemes.Wpf;
using EventSimulator.Common.Configuration;
using EventSimulator.UI.ViewModels;
using EventSimulator.UI.Services;
using EventSimulator.Core.Interfaces;

namespace EventSimulator.UI
{
    /// <summary>
    /// Main application class for the Windows Event Simulator that handles lifecycle management,
    /// dependency injection, theme configuration, and global exception handling.
    /// </summary>
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AppSettings _settings;
        private readonly PaletteHelper _paletteHelper;

        /// <summary>
        /// Initializes a new instance of the App class with service configuration and error handling.
        /// </summary>
        public App()
        {
            _settings = new AppSettings();
            _paletteHelper = new PaletteHelper();
            
            // Configure services during construction
            _serviceProvider = ConfigureServices();

            // Set up global exception handling
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // Configure thread culture for consistency
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        }

        /// <summary>
        /// Handles application startup with initialization of services, themes, and main window.
        /// </summary>
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Load and validate configuration
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{_settings.Environment}.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                _settings.LoadConfiguration(configuration);

                // Configure logging
                ConfigureLogging();

                // Configure application theme
                ConfigureTheme();

                // Initialize performance monitoring if enabled
                if (_settings.EnablePerformanceMonitoring)
                {
                    InitializePerformanceMonitoring();
                }

                // Create and show main window
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();

                // Initialize background services
                await InitializeBackgroundServicesAsync();

                Log.Information("Application started successfully");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application failed to start");
                MessageBox.Show(
                    $"Failed to start application: {ex.Message}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                
                Current.Shutdown(-1);
            }
        }

        /// <summary>
        /// Configures dependency injection services and application components.
        /// </summary>
        private IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // Register configuration
            services.AddSingleton(_settings);

            // Register logging services
            services.AddLogging(builder => builder.AddSerilog());

            // Register core services
            services.AddSingleton<IEventGenerator, EventGenerator>();
            services.AddSingleton<ITemplateManager, TemplateManager>();
            services.AddSingleton<IScenarioManager, ScenarioManager>();

            // Register UI services
            services.AddSingleton<INotificationService, NotificationService>();
            services.AddSingleton<IDialogService, DialogService>();

            // Register ViewModels
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<EventGeneratorViewModel>();
            services.AddSingleton<ScenarioBuilderViewModel>();
            services.AddSingleton<TemplateManagerViewModel>();

            // Register main window
            services.AddSingleton<MainWindow>();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Configures application-wide logging with Serilog.
        /// </summary>
        private void ConfigureLogging()
        {
            var logConfig = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day)
                .WriteTo.Debug();

            if (_settings.EnableDetailedLogging)
            {
                logConfig.MinimumLevel.Debug();
            }

            Log.Logger = logConfig.CreateLogger();
        }

        /// <summary>
        /// Configures the application theme using Material Design.
        /// </summary>
        private void ConfigureTheme()
        {
            var theme = _paletteHelper.GetTheme();
            theme.SetBaseTheme(Theme.Dark);
            theme.SetPrimaryColor(System.Windows.Media.Colors.Blue);
            theme.SetSecondaryColor(System.Windows.Media.Colors.LightBlue);
            _paletteHelper.SetTheme(theme);
        }

        /// <summary>
        /// Initializes performance monitoring counters and metrics.
        /// </summary>
        private void InitializePerformanceMonitoring()
        {
            if (!System.Diagnostics.PerformanceCounterCategory.Exists("Windows Event Simulator"))
            {
                try
                {
                    System.Diagnostics.PerformanceCounterCategory.Create(
                        "Windows Event Simulator",
                        "Performance metrics for Windows Event Simulator",
                        System.Diagnostics.PerformanceCounterCategoryType.MultiInstance,
                        new System.Diagnostics.CounterCreationDataCollection
                        {
                            new System.Diagnostics.CounterCreationData(
                                "Events Generated/sec",
                                "Number of events generated per second",
                                System.Diagnostics.PerformanceCounterType.RateOfCountsPerSecond32),
                            new System.Diagnostics.CounterCreationData(
                                "Memory Usage (MB)",
                                "Memory usage in megabytes",
                                System.Diagnostics.PerformanceCounterType.NumberOfItems64)
                        });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to create performance counters");
                }
            }
        }

        /// <summary>
        /// Initializes background services and tasks.
        /// </summary>
        private async Task InitializeBackgroundServicesAsync()
        {
            // Initialize any background services that need to start with the application
            await Task.CompletedTask;
        }

        /// <summary>
        /// Handles unhandled exceptions in the dispatcher.
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unhandled dispatcher exception");
            HandleException("Application Error", e.Exception);
            e.Handled = true;
        }

        /// <summary>
        /// Handles unobserved task exceptions.
        /// </summary>
        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unobserved task exception");
            HandleException("Background Task Error", e.Exception);
            e.SetObserved();
        }

        /// <summary>
        /// Handles unhandled application domain exceptions.
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            Log.Fatal(exception, "Unhandled application exception");
            HandleException("Critical Error", exception);
        }

        /// <summary>
        /// Common exception handling logic.
        /// </summary>
        private void HandleException(string title, Exception exception)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    $"{exception.Message}\n\nPlease check the application logs for more details.",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }

        /// <summary>
        /// Handles application shutdown with cleanup.
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // Dispose services
                if (_serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                // Flush logging
                Log.CloseAndFlush();
            }
            catch (Exception ex)
            {
                // Log but don't throw during shutdown
                Log.Error(ex, "Error during application shutdown");
            }
            finally
            {
                base.OnExit(e);
            }
        }
    }
}