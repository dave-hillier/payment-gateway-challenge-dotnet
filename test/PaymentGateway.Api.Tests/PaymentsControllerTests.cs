using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Tests.Mocks;

namespace PaymentGateway.Api.Tests;

public class PaymentsControllerTests
{
    private readonly Random _random = new();
    
    [Fact]
    public async Task RetrievesAPaymentSuccessfully()
    {
        // Arrange
        var payment = new PostPaymentResponse
        {
            Id = Guid.NewGuid(),
            ExpiryYear = _random.Next(2026, 2030),
            ExpiryMonth = _random.Next(1, 12),
            Amount = _random.Next(1, 10000),
            CardNumberLastFour = _random.Next(1000, 9999).ToString(),
            Currency = "GBP"
        };

        var paymentsRepository = new PaymentsRepository();
        paymentsRepository.Add(payment);

        var webApplicationFactory = new WebApplicationFactory<PaymentsController>();
        var client = webApplicationFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => ((ServiceCollection)services)
                .AddSingleton(paymentsRepository)))
            .CreateClient();

        // Act
        var response = await client.GetAsync($"/api/Payments/{payment.Id}");
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();
        
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
}