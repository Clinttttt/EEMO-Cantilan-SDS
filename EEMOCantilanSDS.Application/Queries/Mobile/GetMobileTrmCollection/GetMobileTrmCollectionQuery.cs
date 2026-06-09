using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileTrmCollection;

public sealed record GetMobileTrmCollectionQuery() : IRequest<Result<MobileTrmCollectionDto>>;
