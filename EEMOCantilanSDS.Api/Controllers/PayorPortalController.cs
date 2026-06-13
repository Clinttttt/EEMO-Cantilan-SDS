using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Application.Queries.Payors.GetPayorBalances;
using EEMOCantilanSDS.Application.Queries.Payors.GetPayorPayableItems;
using EEMOCantilanSDS.Application.Queries.Payors.GetPayorPaymentHistory;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Route("api/payor")]
[ApiController]
[Authorize(Roles = "Payor")]
public class PayorPortalController(ISender sender) : ApiBaseController(sender)
{
    [HttpGet("balances")]
    public async Task<ActionResult<IReadOnlyList<PayorStallBalanceDto>>> GetBalancesAsync()
    {
        var result = await Sender.Send(new GetPayorBalancesQuery());
        return HandleResponse(result);
    }

    [HttpGet("payable-items")]
    public async Task<ActionResult<IReadOnlyList<PayorPayableItemDto>>> GetPayableItemsAsync()
    {
        var result = await Sender.Send(new GetPayorPayableItemsQuery());
        return HandleResponse(result);
    }

    [HttpGet("stalls/{stallId:guid}/history")]
    public async Task<ActionResult<IReadOnlyList<PaymentHistoryDto>>> GetHistoryAsync(Guid stallId)
    {
        var result = await Sender.Send(new GetPayorPaymentHistoryQuery(stallId));
        return HandleResponse(result);
    }
}
