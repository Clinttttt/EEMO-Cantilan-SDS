using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Application.Common.Onboarding;

/// <summary>
/// The revenue operations an LGU may DECLARE while onboarding, and the server-side check of that declaration.
/// </summary>
/// <remarks>
/// <para>
/// A declaration is the office's initial intent, kept inside the onboarding draft document (<c>operations</c>). It is not
/// configuration, not a Collector assignment and not posting authority: nothing here enables a service, registers a source
/// or creates a rate. The Head completes each operation in the portal after activation.
/// </para>
/// <para>
/// Codes are the platform's own stable identities (<see cref="CollectorOperationCodes"/>, plus the utility
/// classification codes). The optional local display name is presentation only - it is never read to decide what an
/// operation is, and it is never a source code or a classification.
/// </para>
/// <para>
/// Operations an LGU's FACILITIES already own (stall fees, commercial space rental, slaughterhouse, terminal, weekly
/// market) are deliberately absent: they follow from the facility the office adds, and offering them twice would be two
/// answers to one question.
/// </para>
/// </remarks>
public static class OnboardingOperationDeclarations
{
    public const int CurrentVersion = 1;
    public const int MaxLocalNameLength = 80;

    public const string Ecf = "ECF";

    public static readonly IReadOnlyList<string> SupportedCodes =
    [
        CollectorOperationCodes.FishMeatVendorFee,
        CollectorOperationCodes.WeightAndMeasure,
        CollectorOperationCodes.MarketFees,
        CollectorOperationCodes.KanmanggaySpaceRental,
        CollectorOperationCodes.FiestaArawLotRental,
        CollectorOperationCodes.VegetableFruitSpaceRental,
        Ecf,
        CollectorOperationCodes.Wcf,
        CollectorOperationCodes.LandingBerthing,
        CollectorOperationCodes.Transportation,
        CollectorOperationCodes.TransferLargeCattle,
    ];

    /// <summary>
    /// Returns one message per problem found in the <c>operations</c> block of an onboarding document, or an empty list when
    /// the block is absent (every draft saved before declarations existed) or sound. Properties it does not know are ignored,
    /// never rejected, so a newer workspace can add fields without breaking an older server.
    /// </summary>
    public static IReadOnlyList<string> Validate(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return [];

        JsonDocument doc;
        try { doc = JsonDocument.Parse(configJson); }
        catch (JsonException) { return ["The onboarding configuration is not valid JSON."]; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("operations", out var ops)
                || ops.ValueKind == JsonValueKind.Null)
                return [];

            var errors = new List<string>();
            if (ops.ValueKind != JsonValueKind.Object)
                return ["Revenue operations must be an object with a version and a list of items."];

            if (ops.TryGetProperty("version", out var version)
                && !(version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var v) && v >= 1 && v <= CurrentVersion))
                errors.Add($"Revenue operations version is not supported (this server reads version {CurrentVersion}).");

            if (!ops.TryGetProperty("items", out var items) || items.ValueKind == JsonValueKind.Null)
                return errors;
            if (items.ValueKind != JsonValueKind.Array)
            {
                errors.Add("Revenue operations items must be a list.");
                return errors;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("Each revenue operation must be an object.");
                    continue;
                }

                var code = item.TryGetProperty("operationCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                if (string.IsNullOrWhiteSpace(code) || !SupportedCodes.Contains(code, StringComparer.Ordinal))
                {
                    errors.Add($"“{code ?? "(missing)"}” is not a revenue operation StallTrack recognises.");
                    continue;
                }
                if (!seen.Add(code))
                    errors.Add($"{code} was declared more than once; each operation is declared once.");

                if (item.TryGetProperty("selected", out var sel) && sel.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    errors.Add($"{code}: 'selected' must be true or false.");

                if (item.TryGetProperty("localDisplayName", out var name) && name.ValueKind != JsonValueKind.Null)
                {
                    if (name.ValueKind != JsonValueKind.String)
                    {
                        errors.Add($"{code}: the local display name must be text.");
                        continue;
                    }
                    var label = name.GetString()!;
                    if (label.Trim().Length > MaxLocalNameLength)
                        errors.Add($"{code}: the local display name cannot exceed {MaxLocalNameLength} characters.");
                    if (label.Any(char.IsControl))
                        errors.Add($"{code}: the local display name cannot contain control characters.");
                }
            }

            return errors;
        }
    }
}
