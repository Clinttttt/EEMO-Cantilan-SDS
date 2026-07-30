namespace EEMOCantilanSDS.Application.Dtos.Auth
{
    /// <summary>Two-factor status for the signed-in user's security panel.</summary>
    public record MfaStatusDto(
        bool Enabled,
        bool PendingEnrollment,
        DateTime? EnrolledAt,
        int RecoveryCodesRemaining,
        /// <summary>
        /// True when this account has never been shown the two-factor reminder and has it switched off — the
        /// portal offers it once, then leaves the choice alone.
        /// </summary>
        bool ReminderPending = false);

    /// <summary>
    /// What the user needs to add the account to an authenticator app. Returned ONCE, when enrollment starts;
    /// after confirmation the secret is never disclosed again (only the encrypted copy is kept).
    /// </summary>
    public record MfaEnrollmentDto(
        string ManualKey,
        string ProvisioningUri,
        string QrCodeDataUri);

    /// <summary>
    /// Single-use recovery codes, shown exactly once. Only their hashes are stored, so they cannot be
    /// re-displayed later — the user must save them now or regenerate a new set.
    /// </summary>
    public record MfaRecoveryCodesDto(IReadOnlyList<string> Codes);
}
