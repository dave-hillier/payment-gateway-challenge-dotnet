using System.Net;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Api.Tests.Mocks;

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _defaultResponse;

    public void SetupResponse(string requestPath, Func<HttpRequestMessage, HttpResponseMessage> responseFunc)
    {
        _responses[requestPath] = responseFunc;
    }

    public void SetupDefaultResponse(Func<HttpRequestMessage, HttpResponseMessage> responseFunc)
    {
        _defaultResponse = responseFunc;
    }

    public static MockHttpMessageHandler CreateBankSimulator()
    {
        var handler = new MockHttpMessageHandler();

        handler.SetupResponse("/payments", request =>
        {
            var content = request.Content?.ReadAsStringAsync().Result ?? "";
            var bankRequest = JsonSerializer.Deserialize<BankRequest>(content, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            if (bankRequest?.CardNumber == null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            var lastDigit = bankRequest.CardNumber.Last();

            return lastDigit switch
            {
                '0' => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                '1' or '3' or '5' or '7' or '9' => CreateSuccessResponse(true),
                '2' or '4' or '6' or '8' => CreateSuccessResponse(false),
                _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            };
        });

        return handler;
    }

    private static HttpResponseMessage CreateSuccessResponse(bool authorized)
    {
        var response = new
        {
            authorized = authorized,
            authorization_code = authorized ? Guid.NewGuid().ToString("N")[..8] : null
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.PathAndQuery ?? "";

        if (_responses.TryGetValue(path, out var responseFunc))
        {
            return Task.FromResult(responseFunc(request));
        }

        if (_defaultResponse != null)
        {
            return Task.FromResult(_defaultResponse(request));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private class BankRequest
    {
        public string? CardNumber { get; set; }
        public string? ExpiryDate { get; set; }
        public string? Currency { get; set; }
        public int Amount { get; set; }
        public string? Cvv { get; set; }
    }
}