using PaymentGateway.Api.Services;
using PaymentGateway.Api.Middleware;
using PaymentGateway.Api.Grains;
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
builder.Services.AddHostedService<RouteInitializationService>();

// Configure HttpClientFactory for acquirer grains
builder.Services.AddHttpClient("AcquirerClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:8080");
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
