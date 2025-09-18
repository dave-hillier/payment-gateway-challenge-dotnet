using System.Text.Json;
using Microsoft.AspNetCore.Http;
using PaymentGateway.Api.Middleware;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Tests;

public class IdempotencyMiddlewareTests
{
    private readonly IdempotencyService _idempotencyService;

    public IdempotencyMiddlewareTests()
    {
        _idempotencyService = new IdempotencyService();
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

        var middleware = new IdempotencyMiddleware(next, _idempotencyService);

        // Act
        await middleware.InvokeAsync(context);

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

        var middleware = new IdempotencyMiddleware(next, _idempotencyService);

        // Act
        await middleware.InvokeAsync(context);

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

        var middleware = new IdempotencyMiddleware(next, _idempotencyService);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_WithExistingProcessingRequest_ReturnsConflict()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var context = CreateHttpContext("/api/payments", "POST");
        context.Request.Headers["Cko-Idempotency-Key"] = idempotencyKey;

        // Mark as processing (no response yet)
        _idempotencyService.MarkAsProcessing(idempotencyKey);

        RequestDelegate next = (_) => Task.CompletedTask;
        var middleware = new IdempotencyMiddleware(next, _idempotencyService);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(409, context.Response.StatusCode);
        var responseBody = await GetResponseBody(context);
        Assert.Equal("Concurrent request detected", responseBody);
    }

    [Fact]
    public async Task InvokeAsync_WithExistingCompletedRequest_ReturnsCachedResponse()
    {
        // Arrange
        var idempotencyKey = "test-key";
        var context = CreateHttpContext("/api/payments", "POST");
        context.Request.Headers["Cko-Idempotency-Key"] = idempotencyKey;

        var cachedResponse = new { id = Guid.NewGuid(), status = "Authorized" };
        _idempotencyService.Store(idempotencyKey, cachedResponse, 200);

        RequestDelegate next = (_) => throw new InvalidOperationException("Next should not be called");
        var middleware = new IdempotencyMiddleware(next, _idempotencyService);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("application/json", context.Response.ContentType);

        var responseBody = await GetResponseBody(context);
        var deserializedResponse = JsonSerializer.Deserialize<JsonElement>(responseBody);

        Assert.Equal(cachedResponse.id.ToString(), deserializedResponse.GetProperty("id").GetString());
        Assert.Equal(cachedResponse.status, deserializedResponse.GetProperty("status").GetString());
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

        var middleware = new IdempotencyMiddleware(next, _idempotencyService);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);

        // Verify response was cached
        var cachedRecord = _idempotencyService.Get(idempotencyKey);
        Assert.NotNull(cachedRecord);
        Assert.Equal(200, cachedRecord.StatusCode);
        Assert.NotNull(cachedRecord.Response);

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

        var middleware = new IdempotencyMiddleware(next, _idempotencyService);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(400, context.Response.StatusCode);

        // Verify response was NOT cached (error responses shouldn't be cached)
        var cachedRecord = _idempotencyService.Get(idempotencyKey);
        Assert.NotNull(cachedRecord); // The processing record should exist
        Assert.Null(cachedRecord.Response); // But no response should be cached
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

        var middleware = new IdempotencyMiddleware(next, _idempotencyService);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(200, context.Response.StatusCode);

        // Verify response was NOT cached (empty responses shouldn't be cached)
        var cachedRecord = _idempotencyService.Get(idempotencyKey);
        Assert.NotNull(cachedRecord); // The processing record should exist
        Assert.Null(cachedRecord.Response); // But no response should be cached
    }

    [Theory]
    [InlineData("/api/payments")]
    [InlineData("/api/payments/")]
    [InlineData("/api/payments/123")]
    public void ShouldProcessIdempotency_WithPaymentsPath_ReturnsTrue(string path)
    {
        // Arrange
        var context = CreateHttpContext(path, HttpMethods.Post);
        var middleware = new IdempotencyMiddleware(_ => Task.CompletedTask, _idempotencyService);

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