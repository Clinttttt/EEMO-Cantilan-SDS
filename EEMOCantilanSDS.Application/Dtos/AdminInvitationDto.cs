namespace EEMOCantilanSDS.Application.Dtos;

/// <summary>
/// The result of inviting (or re-inviting) an Admin. <paramref name="ActivationUrl"/> is shown to the inviting Head once, so they can
/// pass it on if the email did not arrive; only its hash is stored, so it can never be read back later.
/// </summary>
public record AdminInvitationDto(AdminDto Admin, string ActivationUrl, bool EmailSent, DateTime ExpiresAtUtc);
