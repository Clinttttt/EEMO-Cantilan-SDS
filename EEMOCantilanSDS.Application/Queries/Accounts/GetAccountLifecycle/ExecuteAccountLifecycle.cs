using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Accounts.GetAccountLifecycle;

/// <summary>Previews a lifecycle command: eligibility, the reason when blocked, and the state version to confirm. Writes nothing.</summary>
public sealed record PreviewAccountLifecycleQuery(LifecycleSource Source, Guid AccountId, LifecycleCommand Command) : IRequest<Result<LifecyclePreviewDto>>;

/// <summary>Executes a previewed lifecycle command, once: the client operation id makes a retry return the original outcome.</summary>
public sealed record ExecuteAccountLifecycleCommand(LifecycleSource Source, Guid AccountId, LifecycleExecution Execution) : IRequest<Result<LifecycleOutcome>>;

// Authorization, tenant scoping, idempotency and concurrency live in the source executor, which re-derives everything from the authenticated
// identity. These handlers only pick the executor: a source without one (permanent stalls, in this phase) has no execution through the shared contract.
public sealed class PreviewAccountLifecycleQueryHandler(AccountLifecycleResolver resolver) : IRequestHandler<PreviewAccountLifecycleQuery, Result<LifecyclePreviewDto>>
{
    public Task<Result<LifecyclePreviewDto>> Handle(PreviewAccountLifecycleQuery request, CancellationToken ct) =>
        resolver.ExecutorFor(request.Source) is { } executor && Enum.IsDefined(request.Source) && request.AccountId != Guid.Empty
            ? executor.PreviewAsync(request.Source, request.AccountId, request.Command, ct)
            : Task.FromResult(Result<LifecyclePreviewDto>.Failure("This source has no lifecycle execution through the shared contract.", ResultStatus.Invalid));
}

public sealed class ExecuteAccountLifecycleCommandHandler(AccountLifecycleResolver resolver) : IRequestHandler<ExecuteAccountLifecycleCommand, Result<LifecycleOutcome>>
{
    public Task<Result<LifecycleOutcome>> Handle(ExecuteAccountLifecycleCommand request, CancellationToken ct) =>
        resolver.ExecutorFor(request.Source) is { } executor && Enum.IsDefined(request.Source) && request.AccountId != Guid.Empty
            ? executor.ExecuteAsync(request.Source, request.AccountId, request.Execution, ct)
            : Task.FromResult(Result<LifecycleOutcome>.Failure("This source has no lifecycle execution through the shared contract.", ResultStatus.Invalid));
}
