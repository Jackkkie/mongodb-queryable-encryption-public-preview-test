using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MongoQEDemo.Services
{
    public class MongoDbInitializationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MongoDbInitializationService> _logger;

        public static bool IsInitialized { get; private set; } = false;
        public static string? InitializationStatus { get; private set; } = "Initializing...";
        public static Exception? InitializationError { get; private set; }

        public MongoDbInitializationService(IServiceProvider serviceProvider, ILogger<MongoDbInitializationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("🔄 Starting MongoDB initialization in background...");
                InitializationStatus = "Connecting to MongoDB...";
                var startTime = DateTime.UtcNow;

                // Add a small delay to let the application start up
                await Task.Delay(1000, stoppingToken);

                // Create a scope to resolve scoped services
                using var scope = _serviceProvider.CreateScope();
                var mongoDbService = scope.ServiceProvider.GetRequiredService<MongoDbService>();

                InitializationStatus = "Initializing encryption keys...";

                // The MongoDbService constructor will handle all the initialization
                // We just need to trigger it by accessing the service
                var patientCount = await mongoDbService.GetPatientCountAsync();

                var elapsed = DateTime.UtcNow - startTime;
                IsInitialized = true;
                InitializationStatus = $"Ready ({patientCount} patients, initialized in {elapsed.TotalMilliseconds:F0}ms)";
                InitializationError = null;

                _logger.LogInformation($"MongoDB initialization completed successfully in {elapsed.TotalMilliseconds:F0}ms");
                _logger.LogInformation($"Database contains {patientCount} patient records");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("MongoDB initialization cancelled due to application shutdown");
                InitializationStatus = "Initialization cancelled";
            }
            catch (Exception ex)
            {
                IsInitialized = false;
                InitializationError = ex;
                InitializationStatus = $"Failed: {ex.Message}";
                _logger.LogError(ex, "Failed to initialize MongoDB in background. Connection will be established on first request.");
            }
        }
    }
}