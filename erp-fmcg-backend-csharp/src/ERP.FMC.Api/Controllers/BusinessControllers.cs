using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

[ApiController]
[Route("api/sales-orders")]
[Produces("application/json")]
public class SalesOrdersController : ControllerBase
{
    private readonly ISalesOrderService _service;

    public SalesOrdersController(ISalesOrderService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<PagedResult<SalesOrderDto>>> GetAll([FromQuery] SalesOrderFilterDto filter, CancellationToken ct)
        => Ok(await _service.GetPagedAsync(filter, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SalesOrderDto>> GetById(Guid id, CancellationToken ct)
    {
        var o = await _service.GetByIdAsync(id, ct);
        return o == null ? NotFound() : Ok(o);
    }

    [HttpGet("by-number/{orderNumber}")]
    public async Task<ActionResult<SalesOrderDto>> GetByNumber(string orderNumber, CancellationToken ct)
    {
        var o = await _service.GetByNumberAsync(orderNumber, ct);
        return o == null ? NotFound() : Ok(o);
    }

    [HttpPost]
    public async Task<ActionResult<SalesOrderDto>> Create([FromBody] CreateSalesOrderDto dto, CancellationToken ct)
    {
        try
        {
            var o = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = o.Id }, o);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SalesOrderDto>> Update(Guid id, [FromBody] UpdateSalesOrderDto dto, CancellationToken ct)
    {
        var o = await _service.UpdateAsync(id, dto, ct);
        return o == null ? NotFound() : Ok(o);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<SalesOrderDto>> AddItem(Guid id, [FromBody] CreateSalesOrderItemDto dto, CancellationToken ct)
    {
        try
        {
            var o = await _service.AddItemAsync(id, dto, ct);
            return o == null ? NotFound() : Ok(o);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult> RemoveItem(Guid id, Guid itemId, CancellationToken ct)
    {
        var ok = await _service.RemoveItemAsync(id, itemId, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var ok = await _service.MarkAsConfirmedAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{id:guid}/deliver")]
    public async Task<ActionResult> Deliver(Guid id, CancellationToken ct)
    {
        var ok = await _service.MarkAsDeliveredAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}

[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _service;

    public InventoryController(IInventoryService service) => _service = service;

    [HttpGet("balances")]
    public async Task<ActionResult<IReadOnlyList<InventoryBalanceDto>>> GetBalances(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? productId, CancellationToken ct)
        => Ok(await _service.GetBalancesAsync(warehouseId, productId, ct));

    [HttpGet("ledger")]
    public async Task<ActionResult<IReadOnlyList<StockLedgerDto>>> GetLedger(
        [FromQuery] Guid? warehouseId, [FromQuery] Guid? productId,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        => Ok(await _service.GetLedgerAsync(warehouseId, productId, from, to, ct));
}

[ApiController]
[Route("api/price-lists")]
[Produces("application/json")]
public class PriceListsController : ControllerBase
{
    private readonly IPriceListService _service;

    public PriceListsController(IPriceListService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PriceListDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("by-product/{productId:guid}")]
    public async Task<ActionResult<PriceListDto>> GetByProduct(Guid productId, CancellationToken ct)
    {
        var p = await _service.GetByProductAsync(productId, ct);
        return p == null ? NotFound() : Ok(p);
    }

    [HttpPost]
    public async Task<ActionResult<PriceListDto>> Create([FromBody] PriceListDto dto, CancellationToken ct)
        => Ok(await _service.CreateAsync(dto, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PriceListDto>> Update(Guid id, [FromBody] PriceListDto dto, CancellationToken ct)
    {
        var p = await _service.UpdateAsync(id, dto, ct);
        return p == null ? NotFound() : Ok(p);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}
