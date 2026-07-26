namespace EEMOCantilanSDS.Application.Dtos.Auth
{
    /// <summary>
    /// One MFA-enrolled account, listed for the platform operator's two-factor recovery tool.
    /// <para>
    /// Deliberately carries no secret material — identity and state only, so the operator can find the right
    /// account across LGUs before clearing its second factor.
    /// </para>
    /// </summary>
    public record MfaEnrolledAccountDto(
        Guid Id,
        string Username,
        string? FullName,
        string? Email,
        string? Municipality,
        string? OfficeAcronym,
        bool IsHead,
        DateTime? EnrolledAt,
        int RecoveryCodesRemaining);
}
