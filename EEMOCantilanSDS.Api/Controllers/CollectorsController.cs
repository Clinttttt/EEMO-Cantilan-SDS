using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Queries.Collectors.GetAllCollectors;
using EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorById;
using EEMOCantilanSDS.Application.Queries.Collectors.GetNextEmployeeId;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EEMOCantilanSDS.Api.Controllers;

[Authorize(Roles = "SuperAdmin")]
[Route("api/[controller]")]
[ApiController]
public class CollectorsController : ApiBaseController
{
    public CollectorsController(ISender sender) : base(sender)
    {
    }
 
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CollectorListDto>>> GetAllCollectorsAsync()
    {
        var result = await Sender.Send(new GetAllCollectorsQuery());
        return HandleResponse(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CollectorActivityDto>> GetCollectorByIdAsync(Guid id)
    {
        var result = await Sender.Send(new GetCollectorByIdQuery(id));
        return HandleResponse(result);
    }

    [HttpPost]
    public async Task<ActionResult<CollectorDto>> CreateCollectorAsync([FromBody] CreateCollectorCommand command)
    {
        var result = await Sender.Send(command);
        return HandleResponse(result);
    }

    [HttpGet("next-employee-id")]
    public async Task<ActionResult<string>> GetNextEmployeeIdAsync()
    {
        var result = await Sender.Send(new GetNextEmployeeIdQuery());
        return HandleResponse(result);
    }
}
