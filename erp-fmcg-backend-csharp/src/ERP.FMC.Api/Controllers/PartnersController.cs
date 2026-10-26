using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class PartnersController : ControllerBase
{
    private readonly IPartnerService _service;

    public PartnersController(IPartnerService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<PartnerDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<PartnerDto>>> GetAll([FromQuery] PartnerFilterDto filter, CancellationToken ct)
    {
        var result = await _service.GetPagedAsync(filter, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PartnerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PartnerDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("by-code/{code}")]
    public async Task<ActionResult<PartnerDto>> GetByCode(string code, CancellationToken ct)
    {
        var result = await _service.GetByCodeAsync(code, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(PartnerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PartnerDto>> Create([FromBody] CreatePartnerDto dto, CancellationToken ct)
    {
        try
        {
            var result = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PartnerDto>> Update(Guid id, [FromBody] UpdatePartnerDto dto, CancellationToken ct)
    {
        var result = await _service.UpdateAsync(id, dto, ct);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}
