using System.Text.Json;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Mobile.Models;

namespace EEMOCantilanSDS.UnitTest.Mobile;

/// <summary>
/// An itemized Slaughterhouse collection is queued on the device with its selected charges (never a rate) and must reach the server
/// exactly as selected after a restart; a per-head capture must keep its original wire shape.
/// </summary>
public class SlaughterChargesQueueTests
{
    private static PendingOperation Operation(IReadOnlyList<SlaughterChargeSelectionDto>? charges) => new()
    {
        Kind = OfflineOperationKind.FeeScheduleCollection,
        OperationCode = CollectorOperationCodes.Slaughterhouse,
        BusinessDate = new DateOnly(2026, 10, 19),
        OwnerName = "Juan Owner",
        AnimalType = AnimalType.Hog,
        NumberOfHeads = 3,
        ReceivedAmount = 210m,
        SlaughterCharges = charges
    };

    [Fact]
    public void QueuedCharges_SurviveARestartAndReachTheWireDto()
    {
        var selected = new[] { new SlaughterChargeSelectionDto(Guid.NewGuid(), 3), new SlaughterChargeSelectionDto(Guid.NewGuid(), 40) };
        var restored = JsonSerializer.Deserialize<PendingOperation>(JsonSerializer.Serialize(Operation(selected)))!;

        Assert.Equal(selected, restored.SlaughterCharges);
        Assert.Equal(selected, restored.ToDto().SlaughterCharges);
    }

    [Fact]
    public void APerHeadCapture_KeepsItsWireShape_WithNoChargesProperty()
    {
        var json = JsonSerializer.Serialize(Operation(null).ToDto(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("slaughterCharges", json, StringComparison.OrdinalIgnoreCase);
    }
}
