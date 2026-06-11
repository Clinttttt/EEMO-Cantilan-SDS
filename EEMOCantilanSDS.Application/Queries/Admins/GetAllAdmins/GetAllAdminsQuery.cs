using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Admins.GetAllAdmins;

public record GetAllAdminsQuery : IRequest<Result<IReadOnlyList<AdminListDto>>>;
