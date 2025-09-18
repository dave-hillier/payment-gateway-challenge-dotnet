using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Tests.TestHelpers;

namespace PaymentGateway.Api.Tests;

[Trait("Category", "E2E")]
public class PaymentE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public PaymentE2ETests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task ProcessPayment_WithOddCardNumber_ReturnsAuthorized()
    {
        // Arrange
        var paymentRequest = PaymentRequestBuilder.Create()
            .WithOddCardNumber()
            .WithAmount(1000)
            .Build();

        // Act
        var response = await _client.PostAsJsonAsync("/api/payments", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Authorized, paymentResponse.Status);
        Assert.NotEqual(Guid.Empty, paymentResponse.Id);
        Assert.Equal("1111", paymentResponse.CardNumberLastFour);
    }

    [Fact]
    public async Task ProcessPayment_WithEvenCardNumber_ReturnsDeclined()
    {
        // Arrange
        var paymentRequest = PaymentRequestBuilder.Create()
            .WithEvenCardNumber()
            .WithAmount(1000)
            .Build();

        // Act
        var response = await _client.PostAsJsonAsync("/api/payments", paymentRequest);
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(PaymentStatus.Declined, paymentResponse.Status);
        Assert.NotEqual(Guid.Empty, paymentResponse.Id);
        Assert.Equal("0002", paymentResponse.CardNumberLastFour);
    }

    [Fact]
    public async Task ProcessPayment_WithInvalidCardNumber_ReturnsValidationError()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "123", // Invalid card number
            ExpiryMonth = 12,
            ExpiryYear = 2025,
            Currency = "USD",
            Amount = 1000,
            Cvv = "123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/payments", paymentRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProcessPayment_WithPastExpiryDate_ReturnsValidationError()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2020, // Past expiry year
            Currency = "USD",
            Amount = 1000,
            Cvv = "123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/payments", paymentRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProcessPayment_WithCardEndingInZero_ReturnsBankFailure()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4000000000000010", // Ends in 0 (bank failure)
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "USD",
            Amount = 1000,
            Cvv = "123"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/payments", paymentRequest);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GetPayment_WithValidId_ReturnsPaymentDetails()
    {
        // Arrange - First create a payment
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "USD",
            Amount = 1500,
            Cvv = "456"
        };

        var createResponse = await _client.PostAsJsonAsync("/api/payments", paymentRequest);
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Act
        var response = await _client.GetAsync($"/api/payments/{createdPayment!.Id}");
        var paymentResponse = await response.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(paymentResponse);
        Assert.Equal(createdPayment.Id, paymentResponse.Id);
        Assert.Equal("1111", paymentResponse.CardNumberLastFour);
        Assert.Equal(1500, paymentResponse.Amount);
    }

    [Fact]
    public async Task GetPayment_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentId = Guid.Empty;

        // Act
        var response = await _client.GetAsync($"/api/payments/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProcessPayment_WithSameIdempotencyKey_ReturnsSameResponse()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid().ToString();
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "USD",
            Amount = 3000,
            Cvv = "123"
        };

        // Act - First request
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(paymentRequest)
        };
        request1.Headers.Add("Cko-Idempotency-Key", idempotencyKey);

        var response1 = await _client.SendAsync(request1);
        var payment1 = await response1.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Act - Second request with same idempotency key
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(paymentRequest)
        };
        request2.Headers.Add("Cko-Idempotency-Key", idempotencyKey);

        var response2 = await _client.SendAsync(request2);
        var payment2 = await response2.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(response1.StatusCode, response2.StatusCode);
        Assert.NotNull(payment1);
        Assert.NotNull(payment2);
        Assert.Equal(payment1.Id, payment2.Id);

        var payment1Json = JsonSerializer.Serialize(payment1);
        var payment2Json = JsonSerializer.Serialize(payment2);
        Assert.Equal(payment1Json, payment2Json);
    }

    [Fact]
    public async Task ProcessPayment_WithDifferentIdempotencyKeys_CreatesDifferentPayments()
    {
        // Arrange
        var idempotencyKey1 = Guid.NewGuid().ToString();
        var idempotencyKey2 = Guid.NewGuid().ToString();
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "USD",
            Amount = 4000,
            Cvv = "456"
        };

        // Act - First request
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(paymentRequest)
        };
        request1.Headers.Add("Cko-Idempotency-Key", idempotencyKey1);

        var response1 = await _client.SendAsync(request1);
        var payment1 = await response1.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Act - Second request with different idempotency key
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(paymentRequest)
        };
        request2.Headers.Add("Cko-Idempotency-Key", idempotencyKey2);

        var response2 = await _client.SendAsync(request2);
        var payment2 = await response2.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.NotNull(payment1);
        Assert.NotNull(payment2);
        Assert.NotEqual(payment1.Id, payment2.Id);
    }

    [Fact]
    public async Task ProcessPayment_WithoutIdempotencyKey_CreatesNewPaymentsEachTime()
    {
        // Arrange
        var paymentRequest = new PostPaymentRequest
        {
            CardNumber = "4111111111111111",
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "USD",
            Amount = 5000,
            Cvv = "789"
        };

        // Act - First request without idempotency key
        var response1 = await _client.PostAsJsonAsync("/api/payments", paymentRequest);
        var payment1 = await response1.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Act - Second request without idempotency key
        var response2 = await _client.PostAsJsonAsync("/api/payments", paymentRequest);
        var payment2 = await response2.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.NotNull(payment1);
        Assert.NotNull(payment2);
        Assert.NotEqual(payment1.Id, payment2.Id);
    }
}