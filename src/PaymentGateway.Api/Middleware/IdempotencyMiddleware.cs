using System.Text;
using System.Text.Json;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Middleware;

public class IdempotencyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IdempotencyService _idempotencyService;

    public IdempotencyMiddleware(RequestDelegate next, IdempotencyService idempotencyService)
    {
        _next = next;
        _idempotencyService = idempotencyService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldProcessIdempotency(context))
        {
            await _next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers["Cko-Idempotency-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(idempotencyKey))
        {
            await _next(context);
            return;
        }

        var existingRecord = _idempotencyService.Get(idempotencyKey);
        if (existingRecord != null)
        {
            if (existingRecord.Response == null)
            {
                context.Response.StatusCode = 409;
                await context.Response.WriteAsync("Concurrent request detected");
                return;
            }

            context.Response.StatusCode = existingRecord.StatusCode;
            context.Response.ContentType = "application/json";
            var jsonResponse = JsonSerializer.Serialize(existingRecord.Response);
            await context.Response.WriteAsync(jsonResponse);
            return;
        }

        _idempotencyService.MarkAsProcessing(idempotencyKey);

        var originalBodyStream = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        await _next(context);

        responseBodyStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync();

        if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300 && !string.IsNullOrEmpty(responseBody))
        {
            var responseObject = JsonSerializer.Deserialize<object>(responseBody);
            if (responseObject != null)
            {
                _idempotencyService.Store(idempotencyKey, responseObject, context.Response.StatusCode);
            }
        }

        responseBodyStream.Seek(0, SeekOrigin.Begin);
        await responseBodyStream.CopyToAsync(originalBodyStream);
        context.Response.Body = originalBodyStream;
    }

    private static bool ShouldProcessIdempotency(HttpContext context)
    {
        return context.Request.Method == HttpMethods.Post &&
               context.Request.Path.StartsWithSegments("/api/payments");
    }
}