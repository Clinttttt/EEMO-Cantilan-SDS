using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetOfficeProfile;

/// <summary>The caller Head's current office/LGU branding, to pre-fill the self-service edit form.</summary>
public record GetMyOfficeProfileQuery : IRequest<Result<OfficeProfileEditDto>>;
