using PaymentGateway.Api.Services;
using PaymentGateway.Api.Middleware;
using PaymentGateway.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.SuppressModelStateInvalidFilter = true;
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Entity Framework with SQLite
// Note: Using in-memory database requires keeping connection open
builder.Services.AddSingleton<DbConnection>(container =>
{
    var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
    connection.Open();
    return connection;
});

builder.Services.AddDbContext<PaymentGatewayDbContext>((container, options) =>
{
    var connection = container.GetRequiredService<DbConnection>();
    options.UseSqlite(connection);
});
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<CardValidationService>();

// Configure Redis
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConnectionString))
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
    {
        var logger = provider.GetRequiredService<ILogger<Program>>();
        try
        {
            var redis = ConnectionMultiplexer.Connect(redisConnectionString);
            logger.LogInformation("Connected to Redis at {ConnectionString}", redisConnectionString);
            return redis;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect to Redis at {ConnectionString}", redisConnectionString);
            throw;
        }
    });

    builder.Services.AddSingleton<IPaymentCompletionService, RedisPaymentCompletionService>();
}
else
{
    builder.Services.AddSingleton<IPaymentCompletionService, InMemoryPaymentCompletionService>();
}

// Configure HttpClient for acquiring bank communication
builder.Services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(client =>
{
    client.BaseAddress = new Uri("http://localhost:8080");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "PaymentGateway/1.0");
});

// Register the background service for processing payments
builder.Services.AddHostedService<PaymentProcessorService>();

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();
    dbContext.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<IdempotencyMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Make Program class accessible for testing
public partial class Program { }
