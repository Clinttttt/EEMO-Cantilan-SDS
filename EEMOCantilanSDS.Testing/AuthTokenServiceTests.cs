using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Testing;

public class AuthTokenServiceTests : RepositoryTestBase
{
    private static (TokenService service, AppDbContext context) BuildWithAdmin(out AdminUser admin)
    {
        var context = NewContext();
        admin = AdminUser.Create("Head", "head", "head@eemo.gov", "Secret123!", AdminRole.SuperAdmin);
        context.Add(admin);
        context.SaveChanges();

        var service = new TokenService(new ConfigurationBuilder().Build(), new UnitOfWork(context), context);
        return (service, context);
    }

    [Fact]
    public async Task GenerateAndSaveRefreshToken_StoresHash_ButValidatesByRawToken()
    {
        var (service, _) = BuildWithAdmin(out var admin);

        var raw = await service.GenerateAndSaveRefreshToken(admin);

        Assert.NotEqual(raw, admin.RefreshToken); // stored value is a hash, not the raw token
        var validated = await service.ValidateRefreshToken(raw);
        Assert.NotNull(validated);
        Assert.Equal(admin.Id, validated!.Id);
    }

    [Fact]
    public async Task ValidateRefreshToken_RejectsInactiveUser()
    {
        var (service, context) = BuildWithAdmin(out var admin);
        var raw = await service.GenerateAndSaveRefreshToken(admin);

        admin.Deactivate("test");
        await context.SaveChangesAsync();

        Assert.Null(await service.ValidateRefreshToken(raw));
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_InvalidatesToken()
    {
        var (service, _) = BuildWithAdmin(out var admin);
        var raw = await service.GenerateAndSaveRefreshToken(admin);

        await service.RevokeRefreshTokenAsync(raw);

        Assert.Null(admin.RefreshToken);
        Assert.Null(await service.ValidateRefreshToken(raw));
    }
}
