using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Command.Admins.InviteAdmin;

/// <summary>The invitation email and its result. Best-effort: the account exists either way, so a mail problem never fails the invite.</summary>
internal static class InvitationMail
{
    public static async Task<AdminInvitationDto> SendAsync(
        AdminUser admin, string rawToken, DateTime expiresUtc, IEmailSender emailSender, CancellationToken ct)
    {
        var url = ActivationLinks.Build(rawToken);
        var body =
            $"Hello {admin.FullName},\n\n" +
            "You have been invited to StallTrack. Use the secure link below to choose your username and password, then sign in.\n\n" +
            $"Accept your invitation:\n{url}\n\n" +
            $"This is a one-time link and expires on {expiresUtc:MMMM d, yyyy} (UTC). If you were not expecting it, ignore this message; " +
            "nothing happens until the link is used.\n\n— StallTrack";
        bool sent;
        try { sent = await emailSender.SendAsync(admin.Email!, admin.FullName, "You have been invited to StallTrack", body, ct); }
        catch { sent = false; }

        return new AdminInvitationDto(
            new AdminDto(admin.Id, admin.FullName!, admin.Username!, admin.Email!, admin.Role, admin.IsActive), url, sent, expiresUtc);
    }
}
