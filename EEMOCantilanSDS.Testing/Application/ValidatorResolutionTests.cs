using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Queries.Reports.GetFinancialReport;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityReports;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityHistory;
using EEMOCantilanSDS.Application;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Infrastructure;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Every validator must be constructible by the container that will actually build it.
///
/// <para>
/// Validators are found by assembly scanning and built by DI per request, so a constructor dependency the container cannot
/// supply is neither a compile error nor a unit-test failure — the unit tests construct validators directly with their own
/// doubles. It surfaces as a 500 the first time a clerk submits that form. This became a live risk when validators started
/// taking <see cref="IClock"/>: four of them now need something from the container that they did not need before.
/// </para>
///
/// <para>
/// The same trap has already been paid for once here: sixteen middleware tests passed with the middleware's registration
/// deleted from <c>Program.cs</c>, because each test built the pipeline itself.
/// </para>
///
/// <para>
/// What is asserted is that every constructor parameter a validator declares is REGISTERED — not that a database answers.
/// Registration is pure: <c>AddInfrastructureService</c> only reads configuration and hands a connection string to EF, so a
/// placeholder is honest here and no database is touched.
/// </para>
/// </summary>
public class ValidatorResolutionTests
{
    private static IServiceCollection RealRegistrations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Never connected to. Present because registration reads it.
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplicationService();
        services.AddInfrastructureService(configuration);
        return services;
    }

    public static TheoryData<Type> ValidatorTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(ApplicationAssemblyMarker).Assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition) continue;
            if (!typeof(IValidator).IsAssignableFrom(type)) continue;
            data.Add(type);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ValidatorTypes))]
    public void EveryDependencyAValidatorDeclaresIsRegistered(Type validatorType)
    {
        var services = RealRegistrations();
        var registered = services.Select(d => d.ServiceType).ToHashSet();

        var constructor = validatorType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .First();

        var missing = constructor.GetParameters()
            .Select(p => p.ParameterType)
            .Where(t => !registered.Contains(t))
            .Select(t => t.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{validatorType.Name} asks the container for {string.Join(", ", missing)}, which nothing registers. " +
            "It would compile, pass its unit tests, and fail when a clerk submits the form.");
    }

    [Fact]
    public void TheClockIsRegistered()
    {
        // Named on its own because twenty-four types now depend on it: if this registration ever disappears, the message
        // should say so rather than leaving a reader to infer it from two dozen failures.
        Assert.Contains(RealRegistrations(), d => d.ServiceType == typeof(IClock));
    }

    [Theory]
    [InlineData(typeof(IValidator<GetFacilityHistoryQuery>), typeof(GetFacilityHistoryQueryValidator))]
    [InlineData(typeof(IValidator<GetFacilityReportsQuery>), typeof(GetFacilityReportsQueryValidator))]
    [InlineData(typeof(IValidator<GetFinancialReportQuery>), typeof(GetFinancialReportQueryValidator))]
    public void TheClockTakingValidatorsAreActuallyBuiltByTheContainer(Type serviceType, Type expected)
    {
        // The theory above proves the dependencies are REGISTERED. This one proves the container can hand back a working
        // instance — the step where a lifetime mismatch or a missing public constructor would show up instead. These three
        // need nothing but the clock, so they can be built without a database; the fourth (CreateStallCommandValidator) also
        // needs a repository and is covered by the registration check.
        var services = new ServiceCollection();
        services.AddApplicationService();
        services.AddSingleton<IClock, SystemClock>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetServices(serviceType);

        Assert.Contains(resolved, v => v is not null && v.GetType() == expected);
    }

    [Fact]
    public void TheValidatorScanFindsSomething()
    {
        // Guards the theory above from passing vacuously: an empty theory is a green tick that asserts nothing.
        Assert.NotEmpty(ValidatorTypes());
    }
}
