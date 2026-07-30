using System.Globalization;
using System.Text.Json;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Audit;

/// <summary>
/// Turns an audit row's stored snapshot into a sentence an auditor can read without knowing the schema.
///
/// The trail stores the entity type, the action, and a JSON snapshot of the row before and after. On its own
/// that renders as "Updated PaymentRecord", which tells a government reader nothing — they would have to infer
/// the event. This composer names the thing that happened: who paid, for which stall, in which facility and
/// section, how much, and against which receipt.
///
/// Rules it follows:
///   • Never invent. A fact appears only if it is present in the snapshot (or in the small set of related rows
///     the repository resolved for the page); anything missing is simply left out.
///   • Never leak. Only the fields listed here are read, so a future column cannot slip into the trail's text.
///   • Money and dates are formatted the way the rest of the portal formats them (₱, "MMM d, yyyy").
/// </summary>
public static class AuditDetailComposer
{
    /// <summary>Context the repository resolves once per page so the composer can name related records.</summary>
    /// <param name="Stalls">Stalls referenced by the page, already named with this LGU's own labels.</param>
    /// <param name="People">Payors and vendors referenced by the page.</param>
    /// <param name="Facilities">
    /// This LGU's own facility names, keyed by facility code. Never hardcode a facility's name here: one
    /// municipality's "Tabo-an Public Market" is another's something else entirely.
    /// </param>
    /// <param name="SectionLabels">
    /// This LGU's own market-section labels, keyed by the section's stored number — so a change reads
    /// "Section Gulayan → Karne" for an office that renamed its sections, not the canonical wording.
    /// </param>
    public sealed record Lookup(
        IReadOnlyDictionary<Guid, StallRef> Stalls,
        IReadOnlyDictionary<Guid, string> People,
        IReadOnlyDictionary<string, string> Facilities,
        IReadOnlyDictionary<int, string>? SectionLabels = null);

    /// <summary>A stall as an auditor refers to it: its number, its facility, and its section when it has one.</summary>
    public sealed record StallRef(string StallNo, string FacilityName, string? Section, string? Occupant);

    private static readonly CultureInfo Ph = CultureInfo.GetCultureInfo("en-PH");

    /// <summary>
    /// One sentence describing the event. <paramref name="action"/> is "Created" | "Updated" | "Deleted".
    /// </summary>
    public static string Describe(
        string action, string entityType, Guid? entityId, string? newValuesJson, string? oldValuesJson, Lookup lookup)
    {
        var after = Parse(newValuesJson);
        var before = Parse(oldValuesJson);
        var snapshot = after ?? before;

        var subject = Subject(entityType, entityId, snapshot, lookup);
        var verb = Verb(action, entityType);

        return string.IsNullOrWhiteSpace(subject) ? verb : $"{verb} {subject}";
    }

    /// <summary>
    /// The named field changes behind an update: "Monthly rate ₱900.00 → ₱1,200.00". Housekeeping and secret
    /// columns are excluded, and the list is capped so one row cannot flood the page. <paramref name="lookup"/>
    /// supplies this LGU's own section labels, so a renamed section reads by its own name.
    /// </summary>
    public static IReadOnlyList<string> Changes(string? oldValuesJson, string? newValuesJson, Lookup? lookup = null)
    {
        var before = Parse(oldValuesJson);
        var after = Parse(newValuesJson);
        if (before is null || after is null) return Array.Empty<string>();

        var changes = new List<string>();

        foreach (var (field, label) in TrackedFields)
        {
            var oldText = Text(before, field, lookup);
            var newText = Text(after, field, lookup);
            if (oldText == newText) continue;

            var from = string.IsNullOrWhiteSpace(oldText) ? "—" : oldText;
            var to = string.IsNullOrWhiteSpace(newText) ? "—" : newText;
            changes.Add($"{label} {from} → {to}");

            if (changes.Count == MaxChanges) break;
        }

        return changes;
    }

    private const int MaxChanges = 4;

    /// <summary>
    /// The related records a page of audit rows refers to, so the repository can load them in one round-trip
    /// instead of per row. Only ids the composer actually reads are collected.
    /// </summary>
    public static void CollectReferences(
        string entityType, Guid? entityId, string? newValuesJson, string? oldValuesJson,
        HashSet<Guid> stallIds, HashSet<Guid> personIds)
    {
        var snapshot = Parse(newValuesJson) ?? Parse(oldValuesJson);

        if (entityType == "Stall" && entityId is { } stallEntityId)
            stallIds.Add(stallEntityId);

        if (Guid(snapshot, "StallId") is { } stallId)
            stallIds.Add(stallId);

        foreach (var field in new[] { "PayorUserId", "VendorId" })
            if (Guid(snapshot, field) is { } personId)
                personIds.Add(personId);
    }

    /// <summary>
    /// Fields worth reporting on an update, with the words the office uses for them. Deliberately a fixed list:
    /// it keeps the trail readable and stops any new column from appearing in the text unreviewed.
    /// </summary>
    private static readonly (string Field, string Label)[] TrackedFields =
    {
        ("AmountPaid", "Amount paid"),
        ("BalanceDue", "Balance"),
        ("Status", "Status"),
        ("ORNumber", "OR no."),
        ("MonthlyRate", "Monthly rate"),
        ("DailyRate", "Daily rate"),
        ("DailyFee", "Daily fee"),
        ("IsPaid", "Paid"),
        ("IsAbsent", "Absent"),
        ("StallNo", "Stall no."),
        ("Section", "Section"),
        ("Occupant", "Occupant"),
        ("ActualOccupant", "Occupant"),
        ("FullName", "Name"),
        ("Username", "Username"),
        ("Email", "Email"),
        ("ContactNumber", "Contact number"),
        ("Role", "Role"),
        ("IsActive", "Active"),
        ("MfaEnabled", "Two-factor"),
        ("Heads", "Head count"),
        ("NumberOfHeads", "Head count"),
        ("Kilos", "Kilos"),
        ("FishKilos", "Fish kilos"),
        ("Amount", "Amount"),
        ("Fee", "Fee"),
        ("SlaughterFee", "Slaughter fee"),
        ("Route", "Route"),
        ("TripNumber", "Trip no."),
        ("DriverName", "Driver"),
        ("Goods", "Goods"),
        ("Reason", "Reason"),
    };

    private static string Verb(string action, string entityType) => (action, entityType) switch
    {
        ("Created", "PaymentRecord") => "Recorded a payment for",
        ("Updated", "PaymentRecord") => "Updated the payment record of",
        ("Deleted", "PaymentRecord") => "Removed the payment record of",

        ("Created", "DailyCollection") => "Recorded a daily collection for",
        ("Updated", "DailyCollection") => "Updated the daily collection of",
        ("Deleted", "DailyCollection") => "Removed the daily collection of",

        ("Created", "SlaughterTransaction") => "Recorded a collection for",
        ("Updated", "SlaughterTransaction") => "Updated the collection of",
        ("Deleted", "SlaughterTransaction") => "Removed the collection of",

        ("Created", "TrmTrip") => "Recorded a trip for",
        ("Updated", "TrmTrip") => "Updated the trip of",
        ("Deleted", "TrmTrip") => "Removed the trip of",

        ("Created", "TpmAttendance") => "Recorded a market-day collection for",
        ("Updated", "TpmAttendance") => "Updated the market-day collection of",
        ("Deleted", "TpmAttendance") => "Removed the market-day collection of",

        ("Created", "OnlinePaymentTransaction") => "Started an online payment for",
        ("Updated", "OnlinePaymentTransaction") => "Updated the online payment of",

        ("Created", "Stall") => "Added stall",
        ("Updated", "Stall") => "Updated stall",
        ("Deleted", "Stall") => "Removed stall",

        ("Created", "StallMonthlyException") => "Excused a billing month for",
        ("Deleted", "StallMonthlyException") => "Removed the billing exception of",
        ("Updated", "StallMonthlyException") => "Updated the billing exception of",

        ("Created", "NpmMarketClosure") => "Declared a market closure for",
        ("Deleted", "NpmMarketClosure") => "Removed the market closure of",
        ("Updated", "NpmMarketClosure") => "Updated the market closure of",

        ("Created", "AdminUser") => "Created the staff account of",
        ("Updated", "AdminUser") => "Updated the staff account of",
        ("Deleted", "AdminUser") => "Removed the staff account of",

        ("Created", "CollectorUser") => "Created the collector account of",
        ("Updated", "CollectorUser") => "Updated the collector account of",
        ("Deleted", "CollectorUser") => "Removed the collector account of",

        ("Created", "PayorUser") => "Registered the payor account of",
        ("Updated", "PayorUser") => "Updated the payor account of",
        ("Deleted", "PayorUser") => "Removed the payor account of",

        ("Created", "PayorActivationCode") => "Issued an activation code for",
        ("Updated", "PayorActivationCode") => "Updated the activation code of",
        ("Deleted", "PayorActivationCode") => "Revoked the activation code of",

        ("Created", "PayorStallLink") => "Linked a payor to",
        ("Deleted", "PayorStallLink") => "Unlinked a payor from",
        ("Updated", "PayorStallLink") => "Updated the payor link of",

        ("Created", _) => $"Created {Humanise(entityType)}",
        ("Deleted", _) => $"Removed {Humanise(entityType)}",
        _ => $"Updated {Humanise(entityType)}",
    };

    /// <summary>The record the event is about, named the way the office names it.</summary>
    private static string Subject(string entityType, Guid? entityId, JsonElement? snapshot, Lookup lookup)
    {
        var parts = new List<string>();

        switch (entityType)
        {
            case "PaymentRecord":
            {
                parts.AddIfPresent(StallPhrase(snapshot, lookup));
                parts.AddIfPresent(PeriodPhrase(snapshot));
                parts.AddIfPresent(MoneyPhrase(snapshot, "AmountPaid", "₱{0}"));
                parts.AddIfPresent(OrPhrase(snapshot));
                break;
            }
            case "DailyCollection":
            {
                parts.AddIfPresent(StallPhrase(snapshot, lookup));
                parts.AddIfPresent(DatePhrase(snapshot, "CollectionDate"));
                parts.AddIfPresent(MoneyPhrase(snapshot, "DailyFee", "₱{0}"));
                parts.AddIfPresent(OrPhrase(snapshot));
                break;
            }
            case "SlaughterTransaction":
            {
                parts.AddIfPresent(FacilityPhrase("SLH", lookup));
                parts.AddIfPresent(TextPhrase(snapshot, "OwnerName"));
                parts.AddIfPresent(AnimalPhrase(snapshot));
                parts.AddIfPresent(DatePhrase(snapshot, "TransactionDate"));
                parts.AddIfPresent(MoneyPhrase(snapshot, "SlaughterFee", "₱{0}"));
                parts.AddIfPresent(OrPhrase(snapshot));
                break;
            }
            case "TrmTrip":
            {
                parts.AddIfPresent(FacilityPhrase("TRM", lookup));
                parts.AddIfPresent(TextPhrase(snapshot, "DriverName"));
                parts.AddIfPresent(TripPhrase(snapshot));
                parts.AddIfPresent(TextPhrase(snapshot, "Route"));
                parts.AddIfPresent(MoneyPhrase(snapshot, "Fee", "₱{0}"));
                parts.AddIfPresent(OrPhrase(snapshot));
                break;
            }
            case "TpmAttendance":
            {
                parts.AddIfPresent(FacilityPhrase("TPM", lookup));
                parts.AddIfPresent(PersonPhrase(snapshot, "VendorId", lookup));
                parts.AddIfPresent(DatePhrase(snapshot, "MarketDate"));
                parts.AddIfPresent(MoneyPhrase(snapshot, "Fee", "₱{0}"));
                parts.AddIfPresent(OrPhrase(snapshot));
                break;
            }
            case "OnlinePaymentTransaction":
            {
                parts.AddIfPresent(MoneyPhrase(snapshot, "Amount", "₱{0}"));
                parts.AddIfPresent(TextPhrase(snapshot, "Status"));
                break;
            }
            case "Stall":
            {
                parts.AddIfPresent(StallSelfPhrase(entityId, snapshot, lookup));
                break;
            }
            case "StallMonthlyException":
            case "NpmMarketClosure":
            {
                parts.AddIfPresent(StallPhrase(snapshot, lookup));
                parts.AddIfPresent(PeriodPhrase(snapshot));
                parts.AddIfPresent(DatePhrase(snapshot, "ClosureDate"));
                parts.AddIfPresent(TextPhrase(snapshot, "Reason"));
                break;
            }
            case "AdminUser":
            {
                // "Juan Dela Cruz (Head)" — the person, then the role that distinguishes a Head from an
                // Administrator. The username is deliberately left out: "Juan Dela Cruz · head · Head" read as
                // a database row, not a sentence.
                parts.AddIfPresent(AccountPhrase(snapshot, withRole: true));
                break;
            }
            case "CollectorUser":
            case "PayorUser":
            {
                // The verb already says which kind of account it is, so the role would only repeat it.
                parts.AddIfPresent(AccountPhrase(snapshot, withRole: false));
                break;
            }
            case "PayorActivationCode":
            case "PayorStallLink":
            {
                parts.AddIfPresent(StallPhrase(snapshot, lookup));
                parts.AddIfPresent(PersonPhrase(snapshot, "PayorUserId", lookup));
                break;
            }
        }

        return string.Join(" · ", parts);
    }

    // ── Phrase builders ───────────────────────────────────────────────────────────────────────────────

    private static string? StallPhrase(JsonElement? snapshot, Lookup lookup)
    {
        if (Guid(snapshot, "StallId") is not { } stallId) return null;
        if (!lookup.Stalls.TryGetValue(stallId, out var stall)) return null;
        return StallText(stall);
    }

    private static string? StallSelfPhrase(Guid? entityId, JsonElement? snapshot, Lookup lookup)
    {
        if (entityId is { } id && lookup.Stalls.TryGetValue(id, out var stall))
            return StallText(stall);

        // Falls back to the snapshot when the stall itself has since been removed.
        var stallNo = Text(snapshot, "StallNo");
        return string.IsNullOrWhiteSpace(stallNo) ? null : $"Stall {stallNo}";
    }

    private static string StallText(StallRef stall)
    {
        var where = string.IsNullOrWhiteSpace(stall.Section)
            ? stall.FacilityName
            : $"{stall.FacilityName} ({stall.Section})";

        var who = string.IsNullOrWhiteSpace(stall.Occupant) ? null : stall.Occupant;
        return who is null
            ? $"Stall {stall.StallNo} — {where}"
            : $"{who} · Stall {stall.StallNo} — {where}";
    }

    private static string? PersonPhrase(JsonElement? snapshot, string field, Lookup lookup) =>
        Guid(snapshot, field) is { } id && lookup.People.TryGetValue(id, out var name) ? name : null;

    /// <summary>This LGU's own name for a facility. Absent from the lookup → the phrase is simply left out,
    /// never replaced by another municipality's wording.</summary>
    private static string? FacilityPhrase(string code, Lookup lookup) =>
        lookup.Facilities.TryGetValue(code, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;

    private static string? PeriodPhrase(JsonElement? snapshot)
    {
        var year = Int(snapshot, "BillingYear");
        var month = Int(snapshot, "BillingMonth");
        if (year is null || month is null || month is < 1 or > 12) return null;
        return new DateTime(year.Value, month.Value, 1).ToString("MMMM yyyy", Ph);
    }

    private static string? DatePhrase(JsonElement? snapshot, string field)
    {
        var raw = Text(snapshot, field);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return DateTime.TryParse(raw, Ph, DateTimeStyles.None, out var parsed)
            ? parsed.ToString("MMM d, yyyy", Ph)
            : raw;
    }

    private static string? MoneyPhrase(JsonElement? snapshot, string field, string format)
    {
        var value = Decimal(snapshot, field);
        if (value is null || value == 0m) return null;
        return string.Format(Ph, format, value.Value.ToString("N2", Ph));
    }

    private static string? OrPhrase(JsonElement? snapshot)
    {
        var or = Text(snapshot, "ORNumber");
        return string.IsNullOrWhiteSpace(or) ? null : $"OR {or}";
    }

    private static string? AnimalPhrase(JsonElement? snapshot)
    {
        var animal = Text(snapshot, "CustomAnimalType");
        if (string.IsNullOrWhiteSpace(animal)) animal = Text(snapshot, "AnimalType");
        var heads = Int(snapshot, "NumberOfHeads");

        if (string.IsNullOrWhiteSpace(animal)) return heads is > 0 ? $"{heads} head" : null;
        return heads is > 1 ? $"{animal} ×{heads}" : animal;
    }

    private static string? TripPhrase(JsonElement? snapshot)
    {
        var trip = Int(snapshot, "TripNumber");
        return trip is > 0 ? $"Trip #{trip}" : null;
    }

    /// <summary>
    /// An account named as a person: their full name, falling back to the username when a name was never set,
    /// with the role in brackets where it tells the reader something.
    /// </summary>
    private static string? AccountPhrase(JsonElement? snapshot, bool withRole)
    {
        var name = Text(snapshot, "FullName");
        if (string.IsNullOrWhiteSpace(name)) name = Text(snapshot, "Username");
        if (string.IsNullOrWhiteSpace(name)) return null;

        if (!withRole) return name;

        var role = Text(snapshot, "Role");
        return string.IsNullOrWhiteSpace(role) || string.Equals(role, name, StringComparison.OrdinalIgnoreCase)
            ? name
            : $"{name} ({role})";
    }

    private static string? TextPhrase(JsonElement? snapshot, string field)
    {
        var value = Text(snapshot, field);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    // ── Snapshot readers (tolerant: a missing or differently-typed field is simply absent) ─────────────

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGet(JsonElement? snapshot, string field, out JsonElement value)
    {
        value = default;
        if (snapshot is not { ValueKind: JsonValueKind.Object } root) return false;
        return root.TryGetProperty(field, out value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    /// <summary>
    /// Enum-valued columns, translated by field name. A snapshot stores an enum as its NUMBER, so without this
    /// the trail would read "Status 1 → 3" — worse than useless on a government record. Values mirror the
    /// domain enums; an unmapped number falls back to the number itself rather than inventing a name.
    /// Market sections are deliberately NOT listed here: their wording belongs to the LGU and comes from the
    /// lookup, so an office that renamed its sections sees its own names.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<int, string>> EnumLabels = new(StringComparer.Ordinal)
    {
        ["Status"] = new() { [1] = "Unpaid", [2] = "Partial", [3] = "Paid" },              // PaymentStatus
        ["Role"] = new() { [1] = "Head", [2] = "Administrator" },                          // AdminRole
        ["AnimalType"] = new() { [1] = "Hog", [2] = "Carabao", [3] = "Cow", [99] = "Other" },
        ["StallStatus"] = new() { [1] = "Active", [2] = "Closed" },
    };

    private static string Text(JsonElement? snapshot, string field, Lookup? lookup = null)
    {
        if (!TryGet(snapshot, field, out var value)) return string.Empty;

        // A market section reads by the LGU's own label for it.
        if (field == "Section"
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var section)
            && lookup?.SectionLabels is { } sections
            && sections.TryGetValue(section, out var sectionLabel))
        {
            return sectionLabel;
        }

        // An enum column reads as its name, not its number.
        if (value.ValueKind == JsonValueKind.Number
            && EnumLabels.TryGetValue(field, out var labels)
            && value.TryGetInt32(out var number)
            && labels.TryGetValue(number, out var label))
        {
            return label;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            _ => value.ToString(),
        };
    }

    private static int? Int(JsonElement? snapshot, string field) =>
        TryGet(snapshot, field, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var i)
            ? i
            : null;

    private static decimal? Decimal(JsonElement? snapshot, string field) =>
        TryGet(snapshot, field, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d)
            ? d
            : null;

    private static Guid? Guid(JsonElement? snapshot, string field)
    {
        if (!TryGet(snapshot, field, out var value)) return null;
        return value.ValueKind == JsonValueKind.String && System.Guid.TryParse(value.GetString(), out var id) && id != System.Guid.Empty
            ? id
            : null;
    }

    /// <summary>"PaymentRecord" → "payment record", so a fallback sentence still reads like English.</summary>
    private static string Humanise(string entityType)
    {
        var spaced = string.Concat(entityType.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLower(c) : c.ToString()));
        return char.ToLower(spaced[0]) + spaced[1..];
    }

    private static void AddIfPresent(this List<string> parts, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) parts.Add(value!);
    }
}
