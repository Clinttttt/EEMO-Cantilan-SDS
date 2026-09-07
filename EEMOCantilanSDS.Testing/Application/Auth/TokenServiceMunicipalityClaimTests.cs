using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Increment 2 — the JWT <c>municipality</c> claim must carry the USER's municipality TenantCode so each
/// LGU gets a distinct cache namespace. A Cantilan user (MunicipalityId = default / unresolved) still
/// yields "cantilan-sds" (byte-for-byte identical to before); a user in another LGU yields that LGU's code.
/// </summary>
public class TokenServiceMunicipalityClaimTests : RepositoryTestBase
{
    // HmacSha512 requires a key of at least 512 bits (64 bytes).
    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "unit-test-signing-key-that-is-comfortably-longer-than-sixty-four-bytes-0123456789",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
            })
            .Build();

    private static string MunicipalityClaim(string token) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(token)
            .Claims
            .First(c => c.Type == AppClaimTypes.Municipality)
            .Value;

    [Fact]
    public void ADedicatedOperatorsTokenCarriesTheFlag()
    {
        // On the token so the API's policy decides by the same fact the Application guard reads from the database.
        // Without it the policy could only see role and tenant, refused a dedicated operator, and that account could
        // approve an LGU's onboarding and then be refused the activation that completes it.
        var context = NewContext();
        var operatorAccount = AdminUser.Create(
            "Console", "console", "console@stalltrack.site", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin,
            isPlatformOperator: true);
        context.Add(operatorAccount);
        context.SaveChanges();

        var service = new TokenService(Config(), new UnitOfWork(context), context, new FixedClock(DateTime.UtcNow));
        var token = service.CreateToken(operatorAccount, "SuperAdmin");

        var claim = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
            .FirstOrDefault(c => c.Type == AppClaimTypes.PlatformOperator);

        Assert.NotNull(claim);
        Assert.Equal("true", claim!.Value);
    }

    /// <summary>
    /// A token minted through the REFRESH path carries the operator flag too.
    /// </summary>
    /// <remarks>
    /// This is the premise the whole operator boundary rests on, and it is invisible from either end.
    ///
    /// <para><c>RefreshTokenCommandHandler</c> renews an access token through <see cref="ITokenService.CreateAccessToken"/> and asks
    /// nothing about who the account is. That was raised as a gap on 2026-09-05 — a refresh cookie issued before the municipal
    /// sign-in was closed keeps working — and on 2026-09-08 it was closed WITHOUT a guard, for two reasons that both need to stay
    /// true:</para>
    ///
    /// <para>1. The operator's own console refreshes through that same route (<c>auth.interceptor.ts</c> posts to
    /// <c>api/adminauth/refresh-token</c>), so refusing operators there would sign the operator out of the platform it runs — the
    /// exact failure the boundary was built to avoid.</para>
    ///
    /// <para>2. A refreshed token still carries this claim, so <c>PlatformOperatorBoundaryMiddleware</c> refuses it on every
    /// municipal endpoint. The cookie can mint tokens; the tokens cannot reach an office's records.</para>
    ///
    /// <para>The sibling test above proves the claim through <c>CreateToken</c>. This one goes through <c>CreateAccessToken</c>
    /// deliberately, because that is the method the refresh path calls: if the two ever diverge and the claim is added only on the
    /// login route, the boundary opens silently and nothing else in the suite would notice.</para>
    /// </remarks>
    [Fact]
    public void ARefreshedAccessTokenStillCarriesTheOperatorFlag()
    {
        var context = NewContext();
        var operatorAccount = AdminUser.Create(
            "Console", "console", "console@stalltrack.site", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin,
            isPlatformOperator: true);
        context.Add(operatorAccount);
        context.SaveChanges();

        var service = new TokenService(Config(), new UnitOfWork(context), context, new FixedClock(DateTime.UtcNow));

        // The refresh handler's own call: CreateAccessToken(user), with no say in the role or the claims.
        var refreshed = service.CreateAccessToken(operatorAccount);

        var claim = new JwtSecurityTokenHandler().ReadJwtToken(refreshed).Claims
            .FirstOrDefault(c => c.Type == AppClaimTypes.PlatformOperator);

        Assert.NotNull(claim);
        Assert.Equal("true", claim!.Value);
    }

    /// <summary>
    /// A refresh request carrying no token at all is refused, not answered with a server error.
    /// </summary>
    /// <remarks>
    /// Found on 2026-09-08 while verifying a deployment: posting to <c>api/adminauth/refresh-token</c> with neither a body nor a cookie
    /// returned 500. The controller passes the missing value straight through, and <c>HashRefreshToken</c> was reached with null.
    ///
    /// <para>Not a data leak — nothing is returned either way — but a 500 from an auth endpoint is wrong twice over: it tells an
    /// anonymous caller the server broke rather than that its request was invalid, and it would bury a real fault among noise in the
    /// logs if one ever occurred there. <c>RevokeRefreshTokenAsync</c> beside it had always guarded the same case; this method simply
    /// never did.</para>
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ARefreshRequestWithNoTokenIsRefusedRatherThanThrowing(string? token)
    {
        var context = NewContext();
        var service = new TokenService(Config(), new UnitOfWork(context), context, new FixedClock(DateTime.UtcNow));

        var user = await service.ValidateRefreshToken(token!, CancellationToken.None);

        Assert.Null(user);
    }

    [Fact]
    public void AnOrdinaryAccountsTokenCarriesNoOperatorFlag()
    {
        // Absent rather than "false": a claim that says false is a claim to read wrongly one day, and the policy treats
        // anything other than "true" as not an operator.
        var context = NewContext();
        var head = AdminUser.Create("Head", "head", "head@eemo.gov", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin);
        context.Add(head);
        context.SaveChanges();

        var service = new TokenService(Config(), new UnitOfWork(context), context, new FixedClock(DateTime.UtcNow));
        var token = service.CreateToken(head, "SuperAdmin");

        Assert.DoesNotContain(
            new JwtSecurityTokenHandler().ReadJwtToken(token).Claims,
            c => c.Type == AppClaimTypes.PlatformOperator);
    }

    [Fact]
    public void CantilanUser_WithDefaultMunicipalityId_YieldsDefaultTenantCode()
    {
        var context = NewContext();
        var admin = AdminUser.Create("Head", "head", "head@eemo.gov", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin);
        context.Add(admin); // MunicipalityId left as default (Guid.Empty) — unresolved -> fallback
        context.SaveChanges();

        var service = new TokenService(Config(), new UnitOfWork(context), context, new FixedClock(DateTime.UtcNow));
        var token = service.CreateToken(admin, "SuperAdmin");

        Assert.Equal(TenantConstants.DefaultTenantCode, MunicipalityClaim(token));
        Assert.Equal("cantilan-sds", MunicipalityClaim(token));
    }

    [Fact]
    public void CantilanUser_ExplicitlyLinkedToCantilan_YieldsCantilanSds()
    {
        var context = NewContext();
        var cantilan = Municipality.Create(
            "CANTILAN", "Cantilan", "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: "cantilan-sds", isDefault: true);
        context.Add(cantilan);

        var admin = AdminUser.Create("Head", "head", "head@eemo.gov", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin);
        context.Add(admin);
        context.Entry(admin).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = cantilan.Id;
        context.SaveChanges();

        var service = new TokenService(Config(), new UnitOfWork(context), context, new FixedClock(DateTime.UtcNow));
        var token = service.CreateToken(admin, "SuperAdmin");

        Assert.Equal("cantilan-sds", MunicipalityClaim(token));
    }

    [Fact]
    public void UserInAnotherMunicipality_YieldsThatMunicipalitysTenantCode()
    {
        var context = NewContext();
        var carmen = Municipality.Create(
            "CARMEN", "Carmen", "Surigao del Sur", MunicipalityStatus.Upcoming, tenantCode: "carmen");
        context.Add(carmen);

        var admin = AdminUser.Create("Carmen Admin", "carmen", "carmen@eemo.gov", TestPasswords.Hash("Secret123!"), AdminRole.Admin);
        context.Add(admin);
        context.Entry(admin).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = carmen.Id;
        context.SaveChanges();

        var service = new TokenService(Config(), new UnitOfWork(context), context, new FixedClock(DateTime.UtcNow));
        var token = service.CreateToken(admin, "Admin");

        Assert.Equal("carmen", MunicipalityClaim(token));
        Assert.NotEqual(TenantConstants.DefaultTenantCode, MunicipalityClaim(token));
    }
}
