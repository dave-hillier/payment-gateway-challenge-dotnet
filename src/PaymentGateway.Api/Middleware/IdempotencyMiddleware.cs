using System.Text.Json;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Middleware;

public class IdempotencyMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context, IdempotencyService idempotencyService)
    {
        if (!ShouldProcessIdempotency(context))
        {
            await next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers["Cko-Idempotency-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(idempotencyKey))
        {
            await next(context);
            return;
        }

        var existingRecord = await idempotencyService.GetAsync(idempotencyKey);
        if (existingRecord != null)
        {
            // Payment exists - return the existing payment response
            context.Response.StatusCode = existingRecord.StatusCode;
            context.Response.ContentType = "application/json";
            var jsonResponse = JsonSerializer.Serialize(existingRecord.Response, JsonOptions);
            await context.Response.WriteAsync(jsonResponse);
            return;
        }

        var originalBodyStream = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        await next(context);

        responseBodyStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(responseBodyStream).ReadToEndAsync();

        if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300 && !string.IsNullOrEmpty(responseBody))
        {
            var responseObject = JsonSerializer.Deserialize<object>(responseBody);
            if (responseObject != null)
            {
                await idempotencyService.StoreAsync(idempotencyKey, responseObject, context.Response.StatusCode);
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