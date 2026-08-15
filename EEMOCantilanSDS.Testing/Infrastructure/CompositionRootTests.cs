using EEMOCantilanSDS.Api;
using EEMOCantilanSDS.Application;
using EEMOCantilanSDS.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The composition root must be able to build everything it registers.
///
/// <para>
/// This exists to make a registration reshuffle PROVABLE. Until now the only check on
/// <c>AddInfrastructureService</c> was that the application still starts, which is a slow, manual and partial answer: a service
/// only used by one endpoint is not exercised by booting, so a lost or mis-scoped registration would surface as a 500 the first
/// time a clerk opened that page.
/// </para>
///
/// <para>
/// Registration is pure — it reads configuration and hands EF a connection string — so a placeholder connection string is
/// honest here and no database is touched. Nothing below opens a connection.
/// </para>
/// </summary>
public class CompositionRootTests
{
    /// <summary>
    /// The real composition, in the order <c>Program.cs</c> builds it: API, then Infrastructure, then Application. Mirroring
    /// the host matters — half a container proves half a thing, and the services the API layer owns (the SignalR notifier, for
    /// one) are exactly the sort a reshuffle could drop.
    /// </summary>
    private static IServiceCollection RealRegistrations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never connected to. Present because registration reads it.
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
                ["Jwt:Key"] = "composition-root-test-signing-key-that-is-long-enough-for-hmac",
                ["Jwt:Issuer"] = "stalltrack-tests",
                ["Jwt:Audience"] = "stalltrack-tests",
                // The payment gateway's HttpClient factory THROWS without this, and three online-payment handlers depend on
                // it. Present so the test represents a configured deployment rather than a broken one.
                ["PayMongo:BaseUrl"] = "https://api.paymongo.test/v1/",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);   // the host registers this; nothing else does
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddApi(new CompositionTestEnvironment(), configuration);
        services.AddInfrastructureService(configuration);
        services.AddApplicationService();
        return services;
    }

    /// <summary>A hosting environment for registration only — nothing here reads the file system.</summary>
    private sealed class CompositionTestEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "EEMOCantilanSDS.Api";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public void EveryServiceWeRegisterCanActuallyBeBuilt()
    {
        // Deliberately OUR services only, resolved one by one, rather than ValidateOnBuild over the whole container.
        // ValidateOnBuild also walks the framework's own descriptors - SignalR's connection dispatcher, MVC's result
        // executors, Swagger's options - which need pieces only a real WebApplication provides (IHostApplicationLifetime and
        // the like). Stubbing those would be chasing a moving target and would prove nothing about this codebase. What matters
        // here is that every service WE register, including every MediatR handler, can be constructed from the composition the
        // host builds.
        var services = RealRegistrations();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var failures = new List<string>();
        var checked_ = 0;

        foreach (var descriptor in services)
        {
            var serviceType = descriptor.ServiceType;

            // Open generics cannot be resolved without type arguments; the closed registrations that matter (every
            // IRequestHandler<,>) appear separately.
            if (serviceType.ContainsGenericParameters) continue;
            if (!IsOurs(serviceType) && !IsOurs(descriptor.ImplementationType)) continue;

            checked_++;
            try
            {
                var resolved = scope.ServiceProvider.GetService(serviceType);
                if (resolved is null) failures.Add($"{Name(serviceType)} resolved to null");
            }
            catch (Exception ex)
            {
                failures.Add($"{Name(serviceType)}: {FirstLine(ex)}");
            }
        }

        Assert.True(checked_ > 100, $"only {checked_} of our services were checked; the scan is not finding them");
        Assert.True(failures.Count == 0,
            $"{failures.Count} of {checked_} services the application registers cannot be built:{Environment.NewLine}" +
            string.Join(Environment.NewLine, failures.Take(15)));
    }

    private static bool IsOurs(Type? type) =>
        type?.Assembly.GetName().Name?.StartsWith("EEMOCantilanSDS", StringComparison.Ordinal) == true;

    private static string Name(Type type) => type.FullName ?? type.Name;

    private static string FirstLine(Exception ex)
    {
        var message = (ex.InnerException ?? ex).Message;
        var newline = message.IndexOf('\n');
        return newline < 0 ? message : message[..newline].TrimEnd('\r');
    }

    [Fact]
    public void NoServiceTypeIsRegisteredTwice()
    {
        // Why this matters beyond tidiness: with each service type registered exactly once, the ORDER of the registration
        // groups cannot change behaviour, so moving them between extensions is safe. A duplicate would make the last
        // registration win for GetService and change what IEnumerable<T> yields, and then a reshuffle WOULD be able to alter
        // the application while every other test still passed.
        var duplicates = RealRegistrations()
            .Where(d => IsOurs(d.ServiceType) || IsOurs(d.ImplementationType))
            .Where(d => !d.ServiceType.ContainsGenericParameters)
            .GroupBy(d => d.ServiceType)
            .Where(g => g.Count() > 1)
            .Select(g => $"{Name(g.Key)} registered {g.Count()} times")
            .ToList();

        Assert.True(duplicates.Count == 0,
            "these service types are registered more than once, so registration order decides which one wins:" +
            Environment.NewLine + string.Join(Environment.NewLine, duplicates));
    }

    [Fact]
    public void TheRegistrationSetIsNotSilentlyShrinking()
    {
        // A floor, not an exact figure: an exact count would fail on every legitimate addition and be raised without thought.
        // What it catches is a reshuffle that drops a whole group - the failure mode of splitting one large extension into
        // several and forgetting to call one of them.
        var services = RealRegistrations();

        Assert.True(services.Count > 60,
            $"only {services.Count} services are registered; a group of registrations looks to have been dropped");
    }
}
