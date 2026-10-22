namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IFishMeatRequestSchema
{
    Task<bool> IsReadyAsync(CancellationToken ct = default);
}
