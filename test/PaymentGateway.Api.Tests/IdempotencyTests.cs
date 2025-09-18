using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using PaymentGateway.Api.Controllers;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Tests;

public class IdempotencyTests
{
    private readonly WebApplicationFactory<PaymentsController> _factory = new();

    private HttpClient CreateClient() => _factory.CreateClient();

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

        // Act - First request
        using var client = CreateClient();

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

        // Act - First request without idempotency key
        using var client = CreateClient();
        var response1 = await client.PostAsJsonAsync("/api/payments", request);
        var payment1 = await response1.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Act - Second request without idempotency key
        var response2 = await client.PostAsJsonAsync("/api/payments", request);
        var payment2 = await response2.Content.ReadFromJsonAsync<PostPaymentResponse>();

        // Assert
        Assert.NotEqual(payment1?.Id, payment2?.Id);
    }
}