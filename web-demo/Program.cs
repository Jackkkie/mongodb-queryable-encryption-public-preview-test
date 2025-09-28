using MongoQEDemo.Services;
using MongoDB.Driver;
using DotNetEnv;
using Serilog;

ThreadPool.SetMinThreads(32, 32);
ThreadPool.SetMaxThreads(128, 128);

try
{
    Env.Load();
}
catch
{
}

var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "logs");
Directory.CreateDirectory(logDirectory);

var currentLogPath = Path.Combine(logDirectory, "app.log");
if (File.Exists(currentLogPath))
{
    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    var archivedLogPath = Path.Combine(logDirectory, $"app_{timestamp}.log");
    File.Move(currentLogPath, archivedLogPath);
}

var port = Environment.GetEnvironmentVariable("PORT") ?? "7845";
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? $"http://localhost:{port}";

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(urls);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(currentLogPath,
        rollingInterval: RollingInterval.Infinite,
        fileSizeLimitBytes: 10 * 1024 * 1024,
        retainedFileCountLimit: 10)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddRazorPages();
builder.Services.AddControllers();

var connectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("MongoDB");

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "MongoDB connection string not found. Please set MONGODB_CONNECTION_STRING environment variable or add MongoDB connection string to appsettings.json");
}
builder.Services.AddSingleton<MongoDbService>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<MongoDbService>>();
    return new MongoDbService(connectionString, logger);
});

builder.Services.AddHostedService<MongoDbInitializationService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseCors();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();


try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
