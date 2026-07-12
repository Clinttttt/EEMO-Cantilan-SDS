using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Backup.RestoreTenantData;

/// <summary>
/// Head-triggered scoped restore of the caller's OWN municipality from an uploaded round-trippable
/// snapshot. Confirmation phrase and admin password are re-verified SERVER-SIDE before anything runs; the
/// restore itself is a single transaction scoped to the caller's tenant (any failure = zero changes).
/// </summary>
public record RestoreTenantDataCommand(byte[] SnapshotJson, string ConfirmationPhrase, string Password)
    : IRequest<Result<TenantRestoreResult>>;
