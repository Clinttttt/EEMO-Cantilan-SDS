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
/// classification codes). The optional local display name is an initial declaration for onboarding review only - it is not
/// yet stored per tenant or shown in the activated portal, it is never read to decide what an operation is, and it is never a
/// source code or a classification.
/// </para>
/// <para>
/// Operations an LGU's FACILITIES already own (stall fees, commercial space rental, slaughterhouse, terminal, weekly
/// market) are deliberately absent: they follow from the facility the office adds, and offering them twice would be two
/// answers to one question.
/// </para>
/// <para>
/// The labels here are the neutral standard names the workspace shows. They exist server-side so a local name cannot be
/// accepted that reads as a DIFFERENT operation. KEEP IN STEP with the landing and admin catalogs; the contract tests pin the
/// codes and labels in all three places.
/// </para>
/// </remarks>
public static class OnboardingOperationDeclarations
{
    public const int CurrentVersion = 1;
    public const int MaxLocalNameLength = 80;

    public const string Ecf = "ECF";

    /// <summary>Stable code and neutral standard label of each declarable operation, in catalog order. One source: <see cref="TenantOperationCatalog"/>.</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> Catalog =
        TenantOperationCatalog.All.Select(e => (e.Code, e.DefaultLabel)).ToList();

    public static IReadOnlyList<string> SupportedCodes { get; } = Catalog.Select(c => c.Code).ToList();

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

            var known = Catalog.ToDictionary(c => c.Code, c => c.Label, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            // Selected operations' local names, by normalised text, to refuse two operations sharing one name.
            var selectedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var named = new List<(string Code, string Name, bool Selected)>();

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    errors.Add("Each revenue operation must be an object.");
                    continue;
                }

                var code = item.TryGetProperty("operationCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                if (string.IsNullOrWhiteSpace(code) || !known.ContainsKey(code))
                {
                    errors.Add($"“{code ?? "(missing)"}” is not a revenue operation StallTrack recognises. Remove it, or update the onboarding workspace to the current version.");
                    continue;
                }
                if (!seen.Add(code))
                {
                    errors.Add($"{code} was declared more than once; each operation is declared once.");
                    continue;
                }

                // `selected` is mandatory: an item that does not say whether it is chosen must not be read as either answer.
                var hasSelected = item.TryGetProperty("selected", out var sel);
                if (!hasSelected || sel.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    errors.Add($"{code}: 'selected' is required and must be true or false.");
                    continue;
                }
                var selected = sel.ValueKind == JsonValueKind.True;

                if (item.TryGetProperty("localDisplayName", out var name) && name.ValueKind != JsonValueKind.Null)
                {
                    if (name.ValueKind != JsonValueKind.String)
                    {
                        errors.Add($"{code}: the local display name must be text.");
                        continue;
                    }
                    var label = name.GetString()!.Trim();
                    if (label.Length > MaxLocalNameLength)
                        errors.Add($"{code}: the local display name cannot exceed {MaxLocalNameLength} characters.");
                    if (label.Any(char.IsControl))
                        errors.Add($"{code}: the local display name cannot contain control characters.");
                    if (label.Length > 0) named.Add((code, label, selected));
                }
            }

            // A selected operation's local name must not read as ANOTHER operation's standard name, nor repeat another selected
            // operation's name. Checked after the pass so the message is the same whichever item comes first.
            foreach (var (code, label, selected) in named.Where(n => n.Selected))
            {
                var impersonated = Catalog.FirstOrDefault(o => o.Code != code
                    && string.Equals(o.Label, label, StringComparison.OrdinalIgnoreCase));
                if (impersonated.Code is not null)
                    errors.Add($"{code}: the local display name “{label}” is the standard name of another operation ({impersonated.Label}).");
                else if (selectedNames.TryGetValue(label, out var other))
                    errors.Add($"{code}: the local display name “{label}” is already used by {other}.");
                else
                    selectedNames[label] = code;
            }

            return errors;
        }
    }
}
