using ERP.FMC.Application.Mappings;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Application.Services;

public class CategoryService : ICategoryService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(IUnitOfWork uow, ILogger<CategoryService> logger)
    {
        _uow = uow; _logger = logger;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await _uow.Categories.GetAllAsync();
        return items.OrderBy(c => c.SortOrder).Select(x => x.ToDto()).ToList();
    }

    public async Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _uow.Categories.GetByIdAsync(id);
        return c == null ? null : c.ToDto();
    }

    public async Task<CategoryDto> CreateAsync(CategoryDto dto, CancellationToken ct = default)
    {
        var c = dto.ToEntity();
        c.Id = Guid.NewGuid();
        await _uow.Categories.AddAsync(c);
        await _uow.SaveChangesAsync();
        return c.ToDto();
    }

    public async Task<CategoryDto?> UpdateAsync(Guid id, CategoryDto dto, CancellationToken ct = default)
    {
        var c = await _uow.Categories.GetByIdAsync(id);
        if (c == null) return null;
        c.CategoryCode = dto.CategoryCode;
        c.CategoryName = dto.CategoryName;
        c.ParentId = dto.ParentId;
        c.SortOrder = dto.SortOrder;
        c.IsActive = dto.IsActive;
        await _uow.Categories.UpdateAsync(c);
        await _uow.SaveChangesAsync();
        return c.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _uow.Categories.GetByIdAsync(id);
        if (c == null) return false;
        await _uow.Categories.DeleteAsync(c);
        await _uow.SaveChangesAsync();
        return true;
    }
}

public class RouteService : IRouteService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<RouteService> _logger;

    public RouteService(IUnitOfWork uow, ILogger<RouteService> logger)
    {
        _uow = uow; _logger = logger;
    }

    public async Task<IReadOnlyList<RouteDto>> GetAllAsync(CancellationToken ct = default)
    {
        var routes = await _uow.Routes.GetAllAsync();
        return routes.OrderBy(r => r.RouteCode).Select(x => x.ToDto()).ToList();
    }

    public async Task<RouteDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _uow.Routes.GetByIdAsync(id);
        return r == null ? null : r.ToDto();
    }

    public async Task<RouteDto> CreateAsync(CreateRouteDto dto, CancellationToken ct = default)
    {
        var r = dto.ToEntity();
        r.Id = Guid.NewGuid();
        await _uow.Routes.AddAsync(r);
        await _uow.SaveChangesAsync();
        return r.ToDto();
    }

    public async Task<RouteDto?> UpdateAsync(Guid id, CreateRouteDto dto, CancellationToken ct = default)
    {
        var r = await _uow.Routes.GetByIdAsync(id);
        if (r == null) return null;
        r.RouteCode = dto.RouteCode;
        r.RouteName = dto.RouteName;
        r.BusinessLine = dto.BusinessLine;
        r.Region = dto.Region;
        await _uow.Routes.UpdateAsync(r);
        await _uow.SaveChangesAsync();
        return r.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _uow.Routes.GetByIdAsync(id);
        if (r == null) return false;
        await _uow.Routes.DeleteAsync(r);
        await _uow.SaveChangesAsync();
        return true;
    }
}

public class WarehouseService : IWarehouseService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<WarehouseService> _logger;

    public WarehouseService(IUnitOfWork uow, ILogger<WarehouseService> logger)
    {
        _uow = uow; _logger = logger;
    }

    public async Task<IReadOnlyList<WarehouseDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await _uow.Warehouses.GetAllAsync();
        return items.OrderBy(w => w.WarehouseCode).Select(x => x.ToDto()).ToList();
    }

    public async Task<WarehouseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var w = await _uow.Warehouses.GetByIdAsync(id);
        return w == null ? null : w.ToDto();
    }

    public async Task<WarehouseDto> CreateAsync(CreateWarehouseDto dto, CancellationToken ct = default)
    {
        var w = dto.ToEntity();
        w.Id = Guid.NewGuid();
        await _uow.Warehouses.AddAsync(w);
        await _uow.SaveChangesAsync();
        return w.ToDto();
    }

    public async Task<WarehouseDto?> UpdateAsync(Guid id, CreateWarehouseDto dto, CancellationToken ct = default)
    {
        var w = await _uow.Warehouses.GetByIdAsync(id);
        if (w == null) return null;
        w.WarehouseCode = dto.WarehouseCode;
        w.WarehouseName = dto.WarehouseName;
        w.WarehouseType = dto.WarehouseType;
        w.Address = dto.Address;
        await _uow.Warehouses.UpdateAsync(w);
        await _uow.SaveChangesAsync();
        return w.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var w = await _uow.Warehouses.GetByIdAsync(id);
        if (w == null) return false;
        await _uow.Warehouses.DeleteAsync(w);
        await _uow.SaveChangesAsync();
        return true;
    }
}
