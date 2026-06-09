using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetMobileTpmCollection;

public sealed record GetMobileTpmCollectionQuery() : IRequest<Result<MobileTpmCollectionDto>>;
