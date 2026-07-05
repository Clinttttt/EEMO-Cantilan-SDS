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
    public void CantilanUser_WithDefaultMunicipalityId_YieldsDefaultTenantCode()
    {
        var context = NewContext();
        var admin = AdminUser.Create("Head", "head", "head@eemo.gov", "Secret123!", AdminRole.SuperAdmin);
        context.Add(admin); // MunicipalityId left as default (Guid.Empty) — unresolved -> fallback
        context.SaveChanges();

        var service = new TokenService(Config(), new UnitOfWork(context), context);
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

        var admin = AdminUser.Create("Head", "head", "head@eemo.gov", "Secret123!", AdminRole.SuperAdmin);
        context.Add(admin);
        context.Entry(admin).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = cantilan.Id;
        context.SaveChanges();

        var service = new TokenService(Config(), new UnitOfWork(context), context);
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

        var admin = AdminUser.Create("Carmen Admin", "carmen", "carmen@eemo.gov", "Secret123!", AdminRole.Admin);
        context.Add(admin);
        context.Entry(admin).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = carmen.Id;
        context.SaveChanges();

        var service = new TokenService(Config(), new UnitOfWork(context), context);
        var token = service.CreateToken(admin, "Admin");

        Assert.Equal("carmen", MunicipalityClaim(token));
        Assert.NotEqual(TenantConstants.DefaultTenantCode, MunicipalityClaim(token));
    }
}
