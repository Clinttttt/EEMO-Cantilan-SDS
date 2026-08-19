using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Municipalities.TestPaymentConnection;

/// <summary>
/// Tests the caller LGU's own PayMongo credentials. Tenant-scoped: a Head can only test their own municipality's account.
///
/// <para>
/// A success is RECORDED against the municipality, so the settings screen can say when the connection last answered rather
/// than only that a form was once filled in. A failure records nothing - it must not overwrite the last time things were
/// known to work, because that is the very fact somebody diagnosing a problem needs.
/// </para>
/// </summary>
public class TestPaymentConnectionCommandHandler(
    IAppDbContext context,
    ICurrentUserService currentUser,
    ICredentialProtector protector,
    IPayMongoAccountVerifier verifier,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<TestPaymentConnectionCommand, Result<PaymentConnectionTestDto>>
{
    public async Task<Result<PaymentConnectionTestDto>> Handle(TestPaymentConnectionCommand request, CancellationToken ct)
    {
        if (currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
            return Result<PaymentConnectionTestDto>.Forbidden();

        var municipality = await context.Municipalities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == municipalityId, ct);
        if (municipality is null)
            return Result<PaymentConnectionTestDto>.NotFound();

        // The key the office just typed wins over the stored one: before saving, testing what is already committed would
        // test the previous key and report a reassuring result about the wrong thing.
        var secret = !string.IsNullOrWhiteSpace(request.SecretKey)
            ? request.SecretKey!.Trim()
            : Stored(municipality.PayMongoSecretKeyEnc);

        if (string.IsNullOrWhiteSpace(secret))
        {
            return Result<PaymentConnectionTestDto>.Success(new PaymentConnectionTestDto(
                false,
                "There is no secret key to test yet. Paste this office's PayMongo secret key first.",
                null,
                municipality.PayMongoLastVerifiedAtUtc));
        }

        var check = await verifier.VerifyAsync(secret!, ct);
        var mode = ModeOf(secret!);

        if (!check.IsSuccess)
        {
            return Result<PaymentConnectionTestDto>.Success(new PaymentConnectionTestDto(
                false,
                check.Error ?? "PayMongo did not accept this secret key.",
                mode,
                // The PREVIOUS verification stands. A failed attempt today does not undo the fact that it worked before,
                // and erasing it would remove the most useful thing on the screen for whoever is diagnosing this.
                municipality.PayMongoLastVerifiedAtUtc));
        }

        var now = clock.UtcNow;

        // Only stamped when the tested key is the one this LGU actually uses. Testing a key that has not been saved proves
        // the key works; it does not mean this office's connection works, and saying so would be a small lie on a screen
        // whose whole job is to be believed.
        var testedTheStoredKey = string.IsNullOrWhiteSpace(request.SecretKey)
            || string.Equals(secret, Stored(municipality.PayMongoSecretKeyEnc), StringComparison.Ordinal);

        if (testedTheStoredKey)
        {
            municipality.RecordPayMongoVerified(now, currentUser.Username ?? "Head");
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result<PaymentConnectionTestDto>.Success(new PaymentConnectionTestDto(
            true,
            testedTheStoredKey
                ? "PayMongo accepted this office's credentials."
                : "PayMongo accepted this key. Save it to use it for this office.",
            mode,
            testedTheStoredKey ? now : municipality.PayMongoLastVerifiedAtUtc));
    }

    /// <summary>The stored key in plain form, or null when there is none or it cannot be read.</summary>
    private string? Stored(string? enc)
    {
        if (string.IsNullOrWhiteSpace(enc)) return null;
        try { return protector.Unprotect(enc!); } catch { return null; }
    }

    private static string? ModeOf(string secretKey) =>
        secretKey.StartsWith("sk_live", StringComparison.OrdinalIgnoreCase) ? "Live"
        : secretKey.StartsWith("sk_test", StringComparison.OrdinalIgnoreCase) ? "Test"
        : null;
}
