using PaymentGateway.Api.Services;
using PaymentGateway.Api.Middleware;

using Orleans.Hosting;

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

// Configure Orleans
builder.Host.UseOrleans(silo =>
{
    silo
        .UseLocalhostClustering()
        .AddMemoryGrainStorage("paymentStore")
        .AddMemoryGrainStorage("acquirerStore")
        .AddMemoryGrainStorage("routeStore")
        .ConfigureLogging(logging => logging.AddConsole());
});

builder.Services.AddTransient<CardValidationService>();
builder.Services.AddHostedService<AcquirerRouteRegistrationService>();

// Configure HttpClients for each acquirer
builder.Services.AddHttpClient("Acquirer_simulator", client =>
{
    client.BaseAddress = new Uri("http://localhost:8080");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "PaymentGateway/1.0");
});

builder.Services.AddHttpClient("Acquirer_jpy-acquirer", client =>
{
    client.BaseAddress = new Uri("http://localhost:8081");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "PaymentGateway/1.0");
});

builder.Services.AddHttpClient("Acquirer_eur-acquirer", client =>
{
    client.BaseAddress = new Uri("http://localhost:8082");
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "PaymentGateway/1.0");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Make Program class accessible for testing
public partial class Program { }
