namespace EEMOCantilanSDS.Application.Dtos.Tenancy
{
    /// <summary>
    /// The caller LGU's OR-series suggestion. <see cref="Suggestion"/> is the pre-fill the portal shows for
    /// the next OR number (null when disabled or unconfigured); the admin may accept or override it.
    /// </summary>
    public record OrSeriesSuggestionDto(
        bool Enabled,
        string? Suggestion,
        string? Prefix,
        long NextNumber,
        int PadWidth);
}
