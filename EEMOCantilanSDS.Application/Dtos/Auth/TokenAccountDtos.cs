namespace EEMOCantilanSDS.Application.Dtos.Auth
{
    /// <summary>
    /// The account an anonymous one-time link belongs to, shown so the holder can confirm WHICH account and
    /// municipality they are acting on. One mailbox can serve accounts in several LGUs (email uniqueness is
    /// per-LGU), so without this a user cannot tell two links apart.
    /// <para>
    /// Deliberately minimal — username, office and municipality only. No email, no role, no ids. It is
    /// released only to someone already holding a valid, unexpired token, exactly like the activation
    /// page's context lookup.
    /// </para>
    /// </summary>
    public record TokenAccountContextDto(
        string Username,
        string? FullName,
        string? Municipality,
        string? OfficeAcronym);

    /// <summary>Result of confirming an email address, used to greet the user on the verify page.</summary>
    public record VerifiedAccountDto(
        string Username,
        string? Municipality,
        string? OfficeAcronym,
        bool AlreadyVerified);
}
