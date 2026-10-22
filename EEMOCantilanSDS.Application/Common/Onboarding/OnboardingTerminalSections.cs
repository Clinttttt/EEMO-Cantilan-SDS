using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;

namespace EEMOCantilanSDS.Application.Common.Onboarding;

/// <summary>Checks the versioned Terminal block at draft-save and submission, before operator activation.</summary>
public static class OnboardingTerminalSections
{
    public static IReadOnlyList<string> Validate(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("terminalSections", out var block)) return [];
            if (block.ValueKind != JsonValueKind.Object ||
                !block.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                (!version.TryGetInt32(out var versionNumber) || versionNumber != 1) ||
                !block.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array ||
                items.GetArrayLength() != 3)
                return ["The Terminal section declarations use an unsupported or incomplete format. Return the draft for correction."];
            var codes = new HashSet<TerminalSection>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("section", out var sectionJson) || sectionJson.ValueKind != JsonValueKind.String ||
                    !Enum.TryParse<TerminalSection>(sectionJson.GetString(), out var section) || !Enum.IsDefined(section) ||
                    !codes.Add(section) ||
                    !item.TryGetProperty("isEnabled", out var enabled) ||
                    enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return ["Each stable Terminal section must be declared once with an enabled value."];
                string? label = null;
                if (item.TryGetProperty("localDisplayName", out var n) && n.ValueKind != JsonValueKind.Null)
                {
                    if (n.ValueKind != JsonValueKind.String) return ["Terminal section name must be text."];
                    try { label = TerminalSectionSetting.NormalizeName(n.GetString()); }
                    catch (ArgumentException) { return ["Terminal section names must be 80 characters or fewer."]; }
                }
                if (!names.Add(label ?? OfficeCollectionWorkflow.SectionName(section)))
                    return ["Terminal section names must be unique."];
            }
            return [];
        }
        catch (JsonException)
        {
            return ["The Terminal section declarations could not be read."];
        }
    }
}
