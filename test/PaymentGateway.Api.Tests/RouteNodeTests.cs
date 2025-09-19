using PaymentGateway.Api.Models.Routing;

namespace PaymentGateway.Api.Tests;

public class RouteNodeTests
{
    [Fact]
    public void AddAcquirer_ShouldAddAcquirerToList()
    {
        var routeNode = new RouteNode("5*/JPY");

        routeNode.AddAcquirer("jpy-acquirer");

        Assert.Contains("jpy-acquirer", routeNode.AcquirerIds);
        Assert.Equal("jpy-acquirer", routeNode.DefaultAcquirerId);
    }

    [Fact]
    public void AddAcquirer_ShouldSetAsDefaultWhenSpecified()
    {
        var routeNode = new RouteNode("5*/JPY");

        routeNode.AddAcquirer("acquirer1");
        routeNode.AddAcquirer("acquirer2", setAsDefault: true);

        Assert.Equal("acquirer2", routeNode.DefaultAcquirerId);
        Assert.Equal(2, routeNode.AcquirerIds.Count);
    }

    [Fact]
    public void AddAcquirer_ShouldNotAddDuplicateAcquirer()
    {
        var routeNode = new RouteNode("5*/JPY");

        routeNode.AddAcquirer("jpy-acquirer");
        routeNode.AddAcquirer("jpy-acquirer");

        Assert.Single(routeNode.AcquirerIds);
        Assert.Equal("jpy-acquirer", routeNode.AcquirerIds[0]);
    }

    [Fact]
    public void RemoveAcquirer_ShouldRemoveAcquirerFromList()
    {
        var routeNode = new RouteNode("5*/JPY");

        routeNode.AddAcquirer("jpy-acquirer");
        routeNode.AddAcquirer("backup-acquirer");

        var removed = routeNode.RemoveAcquirer("jpy-acquirer");

        Assert.True(removed);
        Assert.DoesNotContain("jpy-acquirer", routeNode.AcquirerIds);
        Assert.Contains("backup-acquirer", routeNode.AcquirerIds);
    }

    [Fact]
    public void RemoveAcquirer_ShouldUpdateDefaultWhenDefaultIsRemoved()
    {
        var routeNode = new RouteNode("5*/JPY");

        routeNode.AddAcquirer("jpy-acquirer");
        routeNode.AddAcquirer("backup-acquirer");
        Assert.Equal("jpy-acquirer", routeNode.DefaultAcquirerId);

        routeNode.RemoveAcquirer("jpy-acquirer");

        Assert.Equal("backup-acquirer", routeNode.DefaultAcquirerId);
    }

    [Fact]
    public void RemoveAcquirer_ShouldSetDefaultToNullWhenLastAcquirerRemoved()
    {
        var routeNode = new RouteNode("5*/JPY");

        routeNode.AddAcquirer("jpy-acquirer");
        routeNode.RemoveAcquirer("jpy-acquirer");

        Assert.Null(routeNode.DefaultAcquirerId);
        Assert.Empty(routeNode.AcquirerIds);
    }

    [Fact]
    public void GetPreferredAcquirer_ShouldReturnDefaultWhenSet()
    {
        var routeNode = new RouteNode("5*/JPY");

        routeNode.AddAcquirer("acquirer1");
        routeNode.AddAcquirer("acquirer2", setAsDefault: true);

        Assert.Equal("acquirer2", routeNode.GetPreferredAcquirer());
    }

    [Fact]
    public void GetPreferredAcquirer_ShouldReturnFirstAcquirerWhenNoDefault()
    {
        var routeNode = new RouteNode("5*/JPY");

        routeNode.AddAcquirer("acquirer1");
        routeNode.AddAcquirer("acquirer2");
        routeNode.DefaultAcquirerId = null;

        Assert.Equal("acquirer1", routeNode.GetPreferredAcquirer());
    }

    [Fact]
    public void HasAcquirers_ShouldReturnTrueWhenAcquirersExist()
    {
        var routeNode = new RouteNode("5*/JPY");

        Assert.False(routeNode.HasAcquirers());

        routeNode.AddAcquirer("jpy-acquirer");

        Assert.True(routeNode.HasAcquirers());
    }
}