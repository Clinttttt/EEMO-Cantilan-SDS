using System.Reflection;
using EEMOCantilanSDS.Client.Components.Pages.Menus;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace EEMOCantilanSDS.ComponentTests.Pages;

public sealed class InterfaceV2RouteAliasTests
{
    [Fact]
    public void Overview_UsesTheExistingMenuComponentAndAuthorization()
        => AssertRouteAliases<Menu>("/menu", "/overview");

    [Fact]
    public void CollectionActivity_UsesTheExistingTransactionsComponentAndAuthorization()
        => AssertRouteAliases<Transactions>("/transactions", "/collections/activity");

    [Fact]
    public void MonitoringFollowUp_UsesTheExistingFollowUpComponentAndAuthorization()
        => AssertRouteAliases<FollowUpQueue>("/reports/follow-up", "/monitoring/follow-up");

    [Fact]
    public void Administration_UsesTheExistingSettingsComponentAndAuthorization()
        => AssertRouteAliases<Settings>("/settings", "/admin");

    private static void AssertRouteAliases<TPage>(string legacyRoute, string canonicalRoute)
    {
        var pageType = typeof(TPage);
        var routes = pageType.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .Select(route => route.Template)
            .OrderBy(route => route, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { legacyRoute, canonicalRoute }.OrderBy(route => route, StringComparer.Ordinal),
            routes);
        Assert.Same(pageType, AssertUniqueRouteOwner(legacyRoute));
        Assert.Same(pageType, AssertUniqueRouteOwner(canonicalRoute));

        var authorize = Assert.Single(pageType
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("SuperAdmin,Admin", authorize.Roles);
        Assert.Null(authorize.Policy);
        Assert.Null(authorize.AuthenticationSchemes);
    }

    private static Type AssertUniqueRouteOwner(string route)
    {
        var owners = typeof(Menu).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes(typeof(RouteAttribute), inherit: true)
                .Cast<RouteAttribute>()
                .Any(attribute => string.Equals(attribute.Template, route, StringComparison.Ordinal)))
            .ToArray();

        return Assert.Single(owners);
    }
}
