using Orleans;

namespace PaymentGateway.Api.Models.Routing;

[GenerateSerializer]
public class RouteNode
{
    [Id(0)]
    public string BinPrefix { get; set; } = "*";

    [Id(1)]
    public string Currency { get; set; } = "*";

    [Id(2)]
    public List<string> AcquirerIds { get; set; }

    [Id(3)]
    public string? DefaultAcquirerId { get; set; }

    public RouteNode()
    {
        BinPrefix = "*";
        Currency = "*";
        AcquirerIds = new List<string>();
    }

    public RouteNode(string routeKey)
    {
        var parts = routeKey?.Split('/') ?? throw new ArgumentNullException(nameof(routeKey));
        if (parts.Length != 2)
            throw new ArgumentException("Route key must be in format 'binPrefix/currency'", nameof(routeKey));

        BinPrefix = parts[0];
        Currency = parts[1];
        AcquirerIds = new List<string>();
    }

    public void AddAcquirer(string acquirerId, bool setAsDefault = false)
    {
        if (string.IsNullOrWhiteSpace(acquirerId))
            throw new ArgumentException("Acquirer ID cannot be null or whitespace", nameof(acquirerId));

        if (!AcquirerIds.Contains(acquirerId))
        {
            AcquirerIds.Add(acquirerId);
        }

        if (setAsDefault || DefaultAcquirerId == null)
        {
            DefaultAcquirerId = acquirerId;
        }
    }

    public bool RemoveAcquirer(string acquirerId)
    {
        if (string.IsNullOrWhiteSpace(acquirerId))
            return false;

        var removed = AcquirerIds.Remove(acquirerId);

        if (removed && DefaultAcquirerId == acquirerId)
        {
            DefaultAcquirerId = AcquirerIds.FirstOrDefault();
        }

        return removed;
    }

    public string? GetPreferredAcquirer()
    {
        return DefaultAcquirerId ?? AcquirerIds.FirstOrDefault();
    }

    public bool HasAcquirers()
    {
        return AcquirerIds.Count > 0;
    }

    public bool Matches(string cardNumber, string currency)
    {
        return MatchesBinPrefix(cardNumber) && MatchesCurrency(currency);
    }

    public bool MatchesBinPrefix(string cardNumber)
    {
        if (BinPrefix == "*")
            return true;

        if (BinPrefix.EndsWith("*"))
        {
            var prefix = BinPrefix[..^1];
            return cardNumber.StartsWith(prefix);
        }

        return cardNumber.StartsWith(BinPrefix);
    }

    public bool MatchesCurrency(string currency)
    {
        return Currency == "*" || Currency.Equals(currency, StringComparison.OrdinalIgnoreCase);
    }

    public int GetSpecificity()
    {
        var binSpecificity = BinPrefix == "*" ? 0 : (BinPrefix.EndsWith("*") ? BinPrefix.Length - 1 : BinPrefix.Length);
        var currencySpecificity = Currency == "*" ? 0 : 1;
        return (binSpecificity * 100) + currencySpecificity;
    }

    public override string ToString()
    {
        return $"{BinPrefix}/{Currency}";
    }
}