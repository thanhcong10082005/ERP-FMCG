using AutoMapper;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Application.Services;

public class CategoryService : ICategoryService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(IUnitOfWork uow, IMapper mapper, ILogger<CategoryService> logger)
    {
        _uow = uow; _mapper = mapper; _logger = logger;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await _uow.Categories.GetAllAsync();
        return _mapper.Map<IReadOnlyList<CategoryDto>>(items.OrderBy(c => c.SortOrder));
    }

    public async Task<CategoryDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _uow.Categories.GetByIdAsync(id);
        return c == null ? null : _mapper.Map<CategoryDto>(c);
    }

    public async Task<CategoryDto> CreateAsync(CategoryDto dto, CancellationToken ct = default)
    {
        var c = _mapper.Map<Category>(dto);
        c.Id = Guid.NewGuid();
        await _uow.Categories.AddAsync(c);
        await _uow.SaveChangesAsync();
        return _mapper.Map<CategoryDto>(c);
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
        return _mapper.Map<CategoryDto>(c);
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
    private readonly IMapper _mapper;
    private readonly ILogger<RouteService> _logger;

    public RouteService(IUnitOfWork uow, IMapper mapper, ILogger<RouteService> logger)
    {
        _uow = uow; _mapper = mapper; _logger = logger;
    }

    public async Task<IReadOnlyList<RouteDto>> GetAllAsync(CancellationToken ct = default)
    {
        var routes = await _uow.Routes.GetAllAsync();
        return _mapper.Map<IReadOnlyList<RouteDto>>(routes.OrderBy(r => r.RouteCode));
    }

    public async Task<RouteDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _uow.Routes.GetByIdAsync(id);
        return r == null ? null : _mapper.Map<RouteDto>(r);
    }

    public async Task<RouteDto> CreateAsync(CreateRouteDto dto, CancellationToken ct = default)
    {
        var r = _mapper.Map<Route>(dto);
        r.Id = Guid.NewGuid();
        await _uow.Routes.AddAsync(r);
        await _uow.SaveChangesAsync();
        return _mapper.Map<RouteDto>(r);
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
        return _mapper.Map<RouteDto>(r);
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
    private readonly IMapper _mapper;
    private readonly ILogger<WarehouseService> _logger;

    public WarehouseService(IUnitOfWork uow, IMapper mapper, ILogger<WarehouseService> logger)
    {
        _uow = uow; _mapper = mapper; _logger = logger;
    }

    public async Task<IReadOnlyList<WarehouseDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await _uow.Warehouses.GetAllAsync();
        return _mapper.Map<IReadOnlyList<WarehouseDto>>(items.OrderBy(w => w.WarehouseCode));
    }

    public async Task<WarehouseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var w = await _uow.Warehouses.GetByIdAsync(id);
        return w == null ? null : _mapper.Map<WarehouseDto>(w);
    }

    public async Task<WarehouseDto> CreateAsync(CreateWarehouseDto dto, CancellationToken ct = default)
    {
        var w = _mapper.Map<Warehouse>(dto);
        w.Id = Guid.NewGuid();
        await _uow.Warehouses.AddAsync(w);
        await _uow.SaveChangesAsync();
        return _mapper.Map<WarehouseDto>(w);
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
        return _mapper.Map<WarehouseDto>(w);
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
