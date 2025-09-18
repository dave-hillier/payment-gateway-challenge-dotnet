using System.Net;
using System.Text.Json;

namespace PaymentGateway.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = GetErrorResponse(exception);

        _logger.LogError(exception, "Unhandled exception occurred: {ExceptionType} - {ExceptionMessage} - Mapped to: {StatusCode} - {Message}",
            exception.GetType().Name, exception.Message, statusCode, message);

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            error = message,
            statusCode = (int)statusCode
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse);
        await context.Response.WriteAsync(jsonResponse);
    }

    private static (HttpStatusCode statusCode, string message) GetErrorResponse(Exception exception)
    {
        return exception switch
        {
            HttpRequestException httpEx when httpEx.Message.Contains("503") || httpEx.Message.Contains("Service Unavailable") =>
                (HttpStatusCode.BadGateway, "Acquiring bank service is temporarily unavailable"),
            HttpRequestException httpEx when IsConnectionError(httpEx) =>
                (HttpStatusCode.ServiceUnavailable, "Service temporarily unavailable due to external dependency failure"),
            TaskCanceledException =>
                (HttpStatusCode.RequestTimeout, "Request timed out"),
            JsonException =>
                (HttpStatusCode.BadGateway, "Invalid response from acquiring bank"),
            _ =>
                (HttpStatusCode.InternalServerError, "An unexpected error occurred")
        };
    }

    private static bool IsConnectionError(HttpRequestException exception)
    {
        var message = exception.Message.ToLowerInvariant();
        return message.Contains("connection") ||
               message.Contains("network") ||
               message.Contains("timeout") ||
               message.Contains("unreachable");
    }
}