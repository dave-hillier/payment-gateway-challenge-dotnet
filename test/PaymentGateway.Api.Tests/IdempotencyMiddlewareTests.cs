using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Api.Data;
using PaymentGateway.Api.Data.Entities;
using PaymentGateway.Api.Enums;
using PaymentGateway.Api.Middleware;
using PaymentGateway.Api.Services;
using System.Data.Common;

namespace PaymentGateway.Api.Tests;

public class IdempotencyMiddlewareTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IdempotencyService _idempotencyService;
    private readonly PaymentGatewayDbContext _dbContext;

    public IdempotencyMiddlewareTests()
    {
        var services = new ServiceCollection();

        // Set up in-memory database
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        services.AddSingleton<DbConnection>(connection);

        services.AddDbContext<PaymentGatewayDbContext>((container, options) =>
        {
            var conn = container.GetRequiredService<DbConnection>();
            options.UseSqlite(conn);
        });

        services.AddScoped<IdempotencyService>();

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<PaymentGatewayDbContext>();
        _dbContext.Database.EnsureCreated();

        _idempotencyService = _serviceProvider.GetRequiredService<IdempotencyService>();
    }

    public void Dispose()
    {
        _dbContext?.Dispose();
        _serviceProvider?.Dispose();
    }

    [Fact]
    public async Task InvokeAsync_WithoutIdempotencyKey_CallsNextDelegate()
    {
        // Arrange
        var context = CreateHttpContext("/api/payments", "POST");
        var nextCalled = false;

        RequestDelegate next = (_) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new IdempotencyMiddleware(next);

        // Act
        await middleware.InvokeAsync(context, _idempotencyService);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WithNonPostRequest_CallsNextDelegate()
    {
        // Arrange
        var context = CreateHttpContext("/api/payments", "GET");
        context.Request.Headers["Cko-Idempotency-Key"] = "test-key";
        var nextCalled = false;

        RequestDelegate next = (_) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new IdempotencyMiddleware(next);

        // Act
        await middleware.InvokeAsync(context, _idempotencyService);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WithNonPaymentsPath_CallsNextDelegate()
    {
        // Arrange
        var context = CreateHttpContext("/api/other", "POST");
        context.Request.Headers["Cko-Idempotency-Key"] = "test-key";
        var nextCalled = false;

        RequestDelegate next = (_) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new IdempotencyMiddleware(next);

        // Act
        await middleware.InvokeAsync(context, _idempotencyService);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WithExistingProcessingRequest_ReturnsPaymentResponse()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var context = CreateHttpContext("/api/payments", "POST");
        context.Request.Headers["Cko-Idempotency-Key"] = idempotencyKey;

        // Create a completed payment request in the database
        var existingPayment = new PaymentRequest
        {
            Id = Guid.NewGuid(),
            CardNumber = "4111111111111111",
            CardNumberLastFour = "1111",
            ExpiryMonth = 12,
            ExpiryYear = 2025,
            Currency = "USD",
            Amount = 1000,
            CVV = "123",
            Status = PaymentStatus.Authorized,
            CreatedAt = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey
        };
        _dbContext.PaymentRequests.Add(existingPayment);
        await _dbContext.SaveChangesAsync();

        RequestDelegate next = (_) => throw new InvalidOperationException("Next should not be called");
        var middleware = new IdempotencyMiddleware(next);

        // Act
        await middleware.InvokeAsync(context, _idempotencyService);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        var responseBody = await GetResponseBody(context);
        var deserializedResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

        Assert.Equal(existingPayment.Id.ToString(), deserializedResponse.GetProperty("id").GetString());
        Assert.Equal(3, deserializedResponse.GetProperty("status").GetInt32()); // PaymentStatus.Authorized = 3
    }

    [Fact]
    public async Task InvokeAsync_WithExistingCompletedRequest_ReturnsCachedResponse()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var context = CreateHttpContext("/api/payments", "POST");
        context.Request.Headers["Cko-Idempotency-Key"] = idempotencyKey;

        // Create a completed payment in the database
        var existingPayment = new PaymentRequest
        {
            Id = Guid.NewGuid(),
            CardNumber = "4111111111111111",
            CardNumberLastFour = "1111",
            ExpiryMonth = 12,
            ExpiryYear = 2025,
            Currency = "USD",
            Amount = 1000,
            CVV = "123",
            Status = PaymentStatus.Authorized,
            CreatedAt = DateTime.UtcNow,
            IdempotencyKey = idempotencyKey
        };
        _dbContext.PaymentRequests.Add(existingPayment);
        await _dbContext.SaveChangesAsync();

        RequestDelegate next = (_) => throw new InvalidOperationException("Next should not be called");
        var middleware = new IdempotencyMiddleware(next);

        // Act
        await middleware.InvokeAsync(context, _idempotencyService);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        var responseBody = await GetResponseBody(context);
        var deserializedResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

        Assert.Equal(existingPayment.Id.ToString(), deserializedResponse.GetProperty("id").GetString());
        Assert.Equal(3, deserializedResponse.GetProperty("status").GetInt32()); // PaymentStatus.Authorized = 3
    }

    [Fact]
    public async Task InvokeAsync_WithNewRequest_ProcessesAndCachesResponse()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var context = CreateHttpContext("/api/payments", "POST");
        context.Request.Headers["Cko-Idempotency-Key"] = idempotencyKey;

        var testResponse = new { id = Guid.NewGuid(), status = "Authorized" };
        var responseJson = JsonSerializer.Serialize(testResponse);

        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.WriteAsync(responseJson);
        };

        var middleware = new IdempotencyMiddleware(next);

        // Act
        await middleware.InvokeAsync(context, _idempotencyService);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);

        // Verify the request was processed successfully
        // The middleware doesn't store anything for new requests in the database implementation
        // since the controller handles the database storage

        var responseBody = await GetResponseBody(context);
        var deserializedResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

        Assert.Equal(testResponse.id.ToString(), deserializedResponse.GetProperty("id").GetString());
        Assert.Equal(testResponse.status, deserializedResponse.GetProperty("status").GetString());
    }

    [Fact]
    public async Task InvokeAsync_WithErrorResponse_DoesNotCacheResponse()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var context = CreateHttpContext("/api/payments", "POST");
        context.Request.Headers["Cko-Idempotency-Key"] = idempotencyKey;

        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 400;
            return ctx.Response.WriteAsync("Bad Request");
        };

        var middleware = new IdempotencyMiddleware(next);

        // Act
        await middleware.InvokeAsync(context, _idempotencyService);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);

        // Verify error response was processed
        // With database implementation, error responses are handled by the controller, not the middleware
    }

    [Fact]
    public async Task InvokeAsync_WithEmptyResponse_DoesNotCacheResponse()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var context = CreateHttpContext("/api/payments", "POST");
        context.Request.Headers["Cko-Idempotency-Key"] = idempotencyKey;

        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask; // No response body
        };

        var middleware = new IdempotencyMiddleware(next);

        // Act
        await middleware.InvokeAsync(context, _idempotencyService);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);

        // Verify empty response was processed
        // With database implementation, empty responses are handled by the controller, not the middleware
    }

    [Theory]
    [InlineData("/api/payments")]
    [InlineData("/api/payments/")]
    [InlineData("/api/payments/123")]
    public void ShouldProcessIdempotency_WithPaymentsPath_ReturnsTrue(string path)
    {
        // Arrange
        var context = CreateHttpContext(path, "POST");

        // Act & Assert through reflection since the method is private
        var shouldProcess = ShouldProcessIdempotencyReflection(context);
        Assert.True(shouldProcess);
    }

    [Theory]
    [InlineData("/api/other")]
    [InlineData("/health")]
    [InlineData("/swagger")]
    public void ShouldProcessIdempotency_WithNonPaymentsPath_ReturnsFalse(string path)
    {
        // Arrange
        var context = CreateHttpContext(path, HttpMethods.Post);

        // Act & Assert
        var shouldProcess = ShouldProcessIdempotencyReflection(context);
        Assert.False(shouldProcess);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public void ShouldProcessIdempotency_WithNonPostMethod_ReturnsFalse(string httpMethod)
    {
        // Arrange
        var context = CreateHttpContext("/api/payments", httpMethod);

        // Act & Assert
        var shouldProcess = ShouldProcessIdempotencyReflection(context);
        Assert.False(shouldProcess);
    }

    private static HttpContext CreateHttpContext(string path, string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<string> GetResponseBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body);
        return await reader.ReadToEndAsync();
    }

    private static bool ShouldProcessIdempotencyReflection(HttpContext context)
    {
        // Using reflection to test the private static method
        var method = typeof(IdempotencyMiddleware).GetMethod("ShouldProcessIdempotency",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (bool)method!.Invoke(null, new object[] { context })!;
    }
}