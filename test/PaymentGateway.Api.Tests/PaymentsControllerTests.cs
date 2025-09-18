using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Data.Entities;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.Mocks;
using System.Data.Common;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    private readonly Random _random = new();
    
    [Fact]
    public async Task RetrievesAPaymentSuccessfully()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var payment = new PaymentRequest
        {
            Id = paymentId,
            ExpiryYear = _random.Next(2026, 2030),
            ExpiryMonth = _random.Next(1, 12),
            Amount = _random.Next(1, 10000),
            CardNumber = "4242424242424242",
            CardNumberLastFour = "4242",
            Currency = "GBP",
            CVV = "123",
            Status = PaymentStatus.Authorized,
            CreatedAt = DateTime.UtcNow
        };

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                // Remove existing DbContext registration
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<PaymentGatewayDbContext>));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // Add in-memory database for testing
                var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
                connection.Open();
                services.AddSingleton<DbConnection>(connection);

                services.AddDbContext<PaymentGatewayDbContext>((container, options) =>
                {
                    var conn = container.GetRequiredService<DbConnection>();
                    options.UseSqlite(conn);
                });

                // Seed test data
                var serviceProvider = services.BuildServiceProvider();
                using var scope = serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();
                dbContext.Database.EnsureCreated();
                dbContext.PaymentRequests.Add(payment);
                dbContext.SaveChanges();
            }))
            .CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{paymentId}");
        var paymentResponse = await response.Content.ReadFromJsonAsync<GetPaymentResponse>();
        
        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
    }

    [Fact]
    public async Task Returns404IfPaymentNotFound()
    {
        // Arrange
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProcessesPaymentSuccessfully()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        var mockHandler = MockHttpMessageHandler.CreateBankSimulator();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri("http://localhost:8080");
                }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.NotEqual(Guid.Empty, paymentResponse!.Id);
        Assert.Equal("1111", paymentResponse.CardNumberLastFour);
        Assert.Equal(paymentRequest.ExpiryMonth, paymentResponse.ExpiryMonth);
        Assert.Equal(paymentRequest.ExpiryYear, paymentResponse.ExpiryYear);
        Assert.Equal(paymentRequest.Currency, paymentResponse.Currency);
        Assert.Equal(paymentRequest.Amount, paymentResponse.Amount);
        Assert.Equal(PaymentStatus.Authorized, paymentResponse.Status);
    }

    [Fact]
    public async Task RejectsPaymentWithInvalidCardNumber()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "0123", // Too short e.g. accidentally sent last 4 digits
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        var mockHandler = MockHttpMessageHandler.CreateBankSimulator();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri("http://localhost:8080");
                }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Rejected, paymentResponse!.Status);
        Assert.NotEqual(Guid.Empty, paymentResponse.Id);
    }

    [Fact]
    public async Task RejectsPaymentWithInvalidLuhnCardNumber()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111112", // Luhn invalid
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        var mockHandler = MockHttpMessageHandler.CreateBankSimulator();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri("http://localhost:8080");
                }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Rejected, paymentResponse!.Status);
    }

    [Fact]
    public async Task RejectsPaymentWithPastExpiryDate()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2020, // Past year
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        var mockHandler = MockHttpMessageHandler.CreateBankSimulator();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri("http://localhost:8080");
                }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Rejected, paymentResponse!.Status);
    }

    [Fact]
    public async Task RejectsPaymentWithUnsupportedCurrency()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "JPY", // Not supported
            Amount = 1000,
            Cvv = "123"
        };

        var mockHandler = MockHttpMessageHandler.CreateBankSimulator();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri("http://localhost:8080");
                }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Rejected, paymentResponse!.Status);
    }

    [Fact]
    public async Task ProcessesBankDeclinedPayment()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4000000000000002", // Card ending in even number (2) should be declined
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        var mockHandler = MockHttpMessageHandler.CreateBankSimulator();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri("http://localhost:8080");
                }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.NotEqual(Guid.Empty, paymentResponse!.Id);
        Assert.Equal("0002", paymentResponse.CardNumberLastFour);
        Assert.Equal(PaymentStatus.Declined, paymentResponse.Status);
    }

    [Fact]
    public async Task HandlesBankServiceUnavailable()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4000000000000010", // Card ending in 0 should cause 503 error (Luhn valid)
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "GBP",
            Amount = 1000,
            Cvv = "123"
        };

        var mockHandler = MockHttpMessageHandler.CreateBankSimulator();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri("http://localhost:8080");
                }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);
            }))
            .CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/Payments", paymentRequest);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task PostPayment_WithIdempotencyKey_ReturnsSameResponseOnSecondRequest()
    {
        // Arrange
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 4,
            ExpiryYear = 2026,
            Currency = "GBP",
            Amount = 100,
            Cvv = "123"
        };

        var idempotencyKey = Guid.NewGuid().ToString();

        var mockHandler = MockHttpMessageHandler.CreateBankSimulator();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri("http://localhost:8080");
                }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);
            }))
            .CreateClient();

        // Act - First request
        using var httpRequestMessage1 = new HttpRequestMessage(HttpMethod.Post, "/api/payments");
        httpRequestMessage1.Headers.Add("Cko-Idempotency-Key", idempotencyKey);
        httpRequestMessage1.Content = JsonContent.Create(request);

        var response1 = await client.SendAsync(httpRequestMessage1);
        var payment1 = await response1.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Act - Second request with same idempotency key
        using var httpRequestMessage2 = new HttpRequestMessage(HttpMethod.Post, "/api/payments");
        httpRequestMessage2.Headers.Add("Cko-Idempotency-Key", idempotencyKey);
        httpRequestMessage2.Content = JsonContent.Create(request);

        var response2 = await client.SendAsync(httpRequestMessage2);
        var payment2 = await response2.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(response1.StatusCode, response2.StatusCode);
        Assert.Equal(payment1?.Id, payment2?.Id);
        Assert.Equal(payment1?.Status, payment2?.Status);
        Assert.Equal(payment1?.CardNumberLastFour, payment2?.CardNumberLastFour);
    }

    [Fact]
    public async Task PostPayment_WithoutIdempotencyKey_CreatesNewPaymentEachTime()
    {
        // Arrange
        var request = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 4,
            ExpiryYear = 2026,
            Currency = "GBP",
            Amount = 100,
            Cvv = "123"
        };

        var mockHandler = MockHttpMessageHandler.CreateBankSimulator();
        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient<IAcquirerClient, AcquiringBankClient>(httpClient =>
                {
                    httpClient.BaseAddress = new Uri("http://localhost:8080");
                }).ConfigurePrimaryHttpMessageHandler(() => mockHandler);
            }))
            .CreateClient();

        // Act - First request without idempotency key
        var response1 = await client.PostAsJsonAsync("/api/payments", request);
        var payment1 = await response1.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Act - Second request without idempotency key
        var response2 = await client.PostAsJsonAsync("/api/payments", request);
        var payment2 = await response2.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.NotEqual(payment1?.Id, payment2?.Id);
    }
}