using System.Text.Json;
using EEMOCantilanSDS.Application.Requests.Stalls;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A renewal request that says nothing about the arrangement must arrive as a SIGNED contract.
/// </summary>
/// <remarks>
/// This is not a theoretical concern about serialisers. OccupancyArrangement starts at 1, so default(OccupancyArrangement) is 0 —
/// not a defined member. Contract.Create decides everything from one comparison, <c>arrangement == SignedContract</c>, so a request
/// that bound to 0 would be treated as NOT signed: the name on contract discarded and the term silently replaced with the
/// open-ended length. Every existing caller omits the field, including the bulk "Renew all" which is deliberately signed-only, so
/// the whole of the old behaviour rests on the record's parameter default surviving deserialisation.
///
/// <para>Asserted against the real JSON an older client sends — the body without the property at all — rather than against the
/// constructor, which would only prove C# honours its own default.</para>
/// </remarks>
public class RenewStallContractRequestDefaultsTests
{
    [Fact]
    public void ABodyWithoutTheArrangement_DeserialisesAsASignedContract()
    {
        const string body = """
        {
          "effectivityDate": "2026-09-12",
          "durationYears": 3,
          "actualOccupant": "Maria Santos",
          "nameOnContract": "Maria Santos"
        }
        """;

        var request = JsonSerializer.Deserialize<RenewStallContractRequest>(
            body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Equal(OccupancyArrangement.SignedContract, request!.Arrangement);

        // And nought is never what arrives, because nought is not a member of the enum at all.
        Assert.NotEqual(default(OccupancyArrangement), request.Arrangement);
    }

    [Fact]
    public void ABodyAskingForAnExtension_IsHonoured()
    {
        const string body = """
        {
          "effectivityDate": "2026-09-12",
          "durationYears": 0,
          "actualOccupant": "Maria Santos",
          "nameOnContract": null,
          "arrangement": 3
        }
        """;

        var request = JsonSerializer.Deserialize<RenewStallContractRequest>(
            body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(request);
        Assert.Equal(OccupancyArrangement.Extension, request!.Arrangement);
    }
}
