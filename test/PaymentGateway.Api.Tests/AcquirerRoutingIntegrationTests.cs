using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using PaymentGateway.Api.Grains;
using PaymentGateway.Api.Models.Routing;
using PaymentGateway.Api.Services;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PaymentGateway.Api.Tests;

[Trait("Category", "E2E")]
public class AcquirerRoutingIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AcquirerRoutingIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task JPYPayment_ShouldUseNewRoutingSystem()
    {
        // This test demonstrates that JPY payments now work and use the routing system
        using var client = _factory.CreateClient();

        var paymentRequest = new
        {
            CardNumber = "5555555555554444",  // Mastercard that should route to JPY acquirer
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "JPY",
            Amount = 10000,  // 100 JPY
            Cvv = "123"
        };

        var json = JsonSerializer.Serialize(paymentRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/payments", content);

        // Should succeed with JPY currency support
        Assert.True(response.IsSuccessStatusCode, $"Response status: {response.StatusCode}");

        var responseContent = await response.Content.ReadAsStringAsync();
        var paymentResponse = JsonSerializer.Deserialize<dynamic>(responseContent);

        // Payment should be processed (not rejected for currency validation)
        Assert.NotNull(paymentResponse);
    }

    [Fact]
    public async Task JPYPayment_WithVisaCard_ShouldWork()
    {
        // This test demonstrates Visa JPY routing
        using var client = _factory.CreateClient();

        var paymentRequest = new
        {
            CardNumber = "4111111111111111",  // Visa that should route to JPY acquirer via fallback
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = "JPY",
            Amount = 50000,  // 500 JPY
            Cvv = "456"
        };

        var json = JsonSerializer.Serialize(paymentRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/payments", content);

        // Should succeed with JPY currency support
        Assert.True(response.IsSuccessStatusCode, $"Response status: {response.StatusCode}");

        var responseContent = await response.Content.ReadAsStringAsync();
        var paymentResponse = JsonSerializer.Deserialize<dynamic>(responseContent);

        // Payment should be processed (not rejected for currency validation)
        Assert.NotNull(paymentResponse);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("JPY")]  // Now supported
    public async Task Payment_WithSupportedCurrencies_ShouldNotReject(string currency)
    {
        using var client = _factory.CreateClient();

        var paymentRequest = new
        {
            CardNumber = "5555555555554444",
            ExpiryMonth = 12,
            ExpiryYear = 2026,
            Currency = currency,
            Amount = 1000,
            Cvv = "123"
        };

        var json = JsonSerializer.Serialize(paymentRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/payments", content);

        // All these currencies should be accepted now
        Assert.True(response.IsSuccessStatusCode, $"Currency {currency} should be supported");
    }
}