using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetPayorPayableItems;

/// <summary>Per-record payable months for the authenticated payor (drives the online pay list).</summary>
public record GetPayorPayableItemsQuery : IRequest<Result<IReadOnlyList<PayorPayableItemDto>>>;
