using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ERP.FMC.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _service;

    public ProductsController(IProductService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetAll([FromQuery] ProductFilterDto filter, CancellationToken ct)
        => Ok(await _service.GetPagedAsync(filter, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> GetById(Guid id, CancellationToken ct)
    {
        var p = await _service.GetByIdAsync(id, ct);
        return p == null ? NotFound() : Ok(p);
    }

    [HttpGet("by-sku/{sku}")]
    public async Task<ActionResult<ProductDto>> GetBySku(string sku, CancellationToken ct)
    {
        var p = await _service.GetBySkuAsync(sku, ct);
        return p == null ? NotFound() : Ok(p);
    }

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create([FromBody] CreateProductDto dto, CancellationToken ct)
    {
        try
        {
            var p = await _service.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(GetById), new { id = p.Id }, p);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Update(Guid id, [FromBody] UpdateProductDto dto, CancellationToken ct)
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

[ApiController]
[Route("api/warehouses")]
[Produces("application/json")]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _service;

    public WarehousesController(IWarehouseService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WarehouseDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WarehouseDto>> GetById(Guid id, CancellationToken ct)
    {
        var w = await _service.GetByIdAsync(id, ct);
        return w == null ? NotFound() : Ok(w);
    }

    [HttpPost]
    public async Task<ActionResult<WarehouseDto>> Create([FromBody] CreateWarehouseDto dto, CancellationToken ct)
        => Ok(await _service.CreateAsync(dto, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WarehouseDto>> Update(Guid id, [FromBody] CreateWarehouseDto dto, CancellationToken ct)
    {
        var w = await _service.UpdateAsync(id, dto, ct);
        return w == null ? NotFound() : Ok(w);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}

[ApiController]
[Route("api/routes")]
[Produces("application/json")]
public class RoutesController : ControllerBase
{
    private readonly IRouteService _service;

    public RoutesController(IRouteService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RouteDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RouteDto>> GetById(Guid id, CancellationToken ct)
    {
        var r = await _service.GetByIdAsync(id, ct);
        return r == null ? NotFound() : Ok(r);
    }

    [HttpPost]
    public async Task<ActionResult<RouteDto>> Create([FromBody] CreateRouteDto dto, CancellationToken ct)
        => Ok(await _service.CreateAsync(dto, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<RouteDto>> Update(Guid id, [FromBody] CreateRouteDto dto, CancellationToken ct)
    {
        var r = await _service.UpdateAsync(id, dto, ct);
        return r == null ? NotFound() : Ok(r);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}

[ApiController]
[Route("api/categories")]
[Produces("application/json")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _service;

    public CategoriesController(ICategoryService service) => _service = service;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> GetById(Guid id, CancellationToken ct)
    {
        var c = await _service.GetByIdAsync(id, ct);
        return c == null ? NotFound() : Ok(c);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create([FromBody] CategoryDto dto, CancellationToken ct)
        => Ok(await _service.CreateAsync(dto, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Update(Guid id, [FromBody] CategoryDto dto, CancellationToken ct)
    {
        var c = await _service.UpdateAsync(id, dto, ct);
        return c == null ? NotFound() : Ok(c);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ok = await _service.DeleteAsync(id, ct);
        return ok ? NoContent() : NotFound();
    }
}
