namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>Provider-neutral transaction handle used for high-risk source-boundary commands.</summary>
public interface IAppDbContextTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
