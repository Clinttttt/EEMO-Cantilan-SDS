using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Whose merchant account an LGU's online payments settle into.
///
/// <para>
/// The global PayMongo configuration is not a platform account. It is ONE municipality's - the default LGU's - and it was
/// being handed to every tenant as a fallback. A freshly activated LGU therefore appeared to have working online payments
/// and, had a vendor paid, the money would have settled into the default LGU's account: wrong municipality, and no trace
/// on the office that thought it had collected it.
/// </para>
///
/// <para>
/// So the rule is: your own keys if you have them, the global configuration ONLY if you are the municipality it belongs
/// to, and otherwise nothing at all.
/// </para>
/// </summary>
public class PayMongoCredentialResolverTests
{
    private const string GlobalSecret = "sk_live_default_lgu";

    private static Municipality Lgu(string code, bool isDefault, string? ownSecretEnc = null)
    {
        var lgu = Municipality.Create(code, code, "Surigao del Sur", MunicipalityStatus.Active,
            code.ToLowerInvariant(), isDefault: isDefault);

        if (ownSecretEnc is not null)
            lgu.SetPayMongoCredentials(ownSecretEnc, "pk_live_own", null, "head");

        return lgu;
    }

    private static PayMongoCredentialResolver Build(Municipality? current)
    {
        var repo = new Mock<IMunicipalityRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(current);

        var accessor = new Mock<ICurrentMunicipalityAccessor>();
        accessor.SetupGet(a => a.MunicipalityId).Returns(current?.Id ?? Guid.Empty);

        var protector = new Mock<ICredentialProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns((string c) => "plain:" + c);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PayMongo:SecretKey"] = GlobalSecret,
            ["PayMongo:PublicKey"] = "pk_live_default_lgu",
            ["PayMongo:WebhookSecret"] = "whsk_default_lgu",
        }).Build();

        return new PayMongoCredentialResolver(repo.Object, accessor.Object, protector.Object, config);
    }

    [Fact]
    public async Task AnLGUWithNoAccountOfItsOwnGetsNOTHING()
    {
        // The defect, at its source. Madrid was activated, configured nothing, and resolved to the default LGU's keys.
        var resolved = await Build(Lgu("MADRID", isDefault: false)).ResolveAsync();

        Assert.False(resolved.IsConfigured);
        Assert.Equal(string.Empty, resolved.SecretKey);
        Assert.NotEqual(GlobalSecret, resolved.SecretKey);
    }

    [Fact]
    public async Task TheDEFAULTMunicipalityStillUsesTheGlobalConfiguration()
    {
        // Because that configuration IS its account. This is the case that must not change - it is the live one.
        var resolved = await Build(Lgu("CANTILAN", isDefault: true)).ResolveAsync();

        Assert.True(resolved.IsConfigured);
        Assert.Equal(GlobalSecret, resolved.SecretKey);
        Assert.Equal("pk_live_default_lgu", resolved.PublicKey);
        Assert.Equal("whsk_default_lgu", resolved.WebhookSecret);
    }

    [Fact]
    public async Task AnLGUWithItsOWNAccountUsesItsOwnKeys()
    {
        var resolved = await Build(Lgu("MADRID", isDefault: false, ownSecretEnc: "enc_madrid")).ResolveAsync();

        Assert.True(resolved.IsConfigured);
        Assert.Equal("plain:enc_madrid", resolved.SecretKey);
        Assert.Equal("pk_live_own", resolved.PublicKey);
    }

    [Fact]
    public async Task AnLGUWithItsOwnAccountDoesNotBorrowTheDefaultsWEBHOOKSecret()
    {
        // It used to fall back to the global webhook secret, which would accept the default LGU's webhook signatures on
        // this LGU's transactions - a payment confirmed by another municipality's account.
        var resolved = await Build(Lgu("MADRID", isDefault: false, ownSecretEnc: "enc_madrid")).ResolveAsync();

        Assert.NotEqual("whsk_default_lgu", resolved.WebhookSecret);
        Assert.Null(resolved.WebhookSecret);
    }

    [Fact]
    public async Task ATokenLessCallerFallsBackToTheDefaultsAccount()
    {
        // Activation, startup and the webhook before it pins its transaction's LGU have no user to resolve, and the
        // accessor answers them with the default municipality - whose account this is.
        var resolved = await Build(null).ResolveAsync();

        Assert.True(resolved.IsConfigured);
        Assert.Equal(GlobalSecret, resolved.SecretKey);
    }

    [Fact]
    public void NoneCannotTransact()
    {
        Assert.False(PayMongoCredentials.None.IsConfigured);
    }
}
