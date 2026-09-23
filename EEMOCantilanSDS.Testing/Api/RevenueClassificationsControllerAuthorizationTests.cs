using System.Reflection;
using EEMOCantilanSDS.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace EEMOCantilanSDS.Testing.Api;

public sealed class RevenueClassificationsControllerAuthorizationTests
{
    [Fact]
    public void RevenueConfigurationEndpointsAreHeadOnly()
    {
        var controller = typeof(RevenueClassificationsController);
        var authorization = controller.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("SuperAdmin", authorization.Roles);
        Assert.Empty(controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<AuthorizeAttribute>()));
    }
}
