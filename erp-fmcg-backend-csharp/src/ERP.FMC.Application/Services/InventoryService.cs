using AutoMapper;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Interfaces;

namespace ERP.FMC.Application.Services;

public class InventoryService : IInventoryService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public InventoryService(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow; _mapper = mapper;
    }

    public async Task<IReadOnlyList<InventoryBalanceDto>> GetBalancesAsync(Guid? warehouseId, Guid? productId, CancellationToken ct = default)
    {
        var all = await _uow.InventoryBalances.GetAllAsync();
        var query = all.AsQueryable();

        if (warehouseId.HasValue) query = query.Where(b => b.WarehouseId == warehouseId);
        if (productId.HasValue) query = query.Where(b => b.ProductId == productId);

        // Hydrate warehouses + products
        var warehouses = (await _uow.Warehouses.GetAllAsync()).ToDictionary(w => w.Id);
        var products = (await _uow.Products.GetAllAsync()).ToDictionary(p => p.Id);

        var dtos = query.ToList().Select(b => new InventoryBalanceDto
        {
            Id = b.Id,
            WarehouseId = b.WarehouseId,
            WarehouseName = warehouses.GetValueOrDefault(b.WarehouseId)?.WarehouseName,
            ProductId = b.ProductId,
            ProductSku = products.GetValueOrDefault(b.ProductId)?.Sku,
            ProductName = products.GetValueOrDefault(b.ProductId)?.ProductName,
            OnHand = b.OnHand,
            Reserved = b.Reserved,
            Available = b.Available ?? 0,
            LastCost = b.LastCost,
            LastMovementAt = b.LastMovementAt ?? b.UpdatedAt
        }).ToList();

        return dtos;
    }

    public async Task<IReadOnlyList<StockLedgerDto>> GetLedgerAsync(Guid? warehouseId, Guid? productId,
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var all = await _uow.StockLedgers.GetAllAsync();
        var query = all.AsQueryable();

        if (warehouseId.HasValue) query = query.Where(l => l.WarehouseId == warehouseId);
        if (productId.HasValue) query = query.Where(l => l.ProductId == productId);
        if (from.HasValue) query = query.Where(l => l.TransactionDate >= from);
        if (to.HasValue) query = query.Where(l => l.TransactionDate <= to);

        var warehouses = (await _uow.Warehouses.GetAllAsync()).ToDictionary(w => w.Id);
        var products = (await _uow.Products.GetAllAsync()).ToDictionary(p => p.Id);

        return query.OrderByDescending(l => l.TransactionDate).Take(1000).Select(l => new StockLedgerDto
        {
            Id = l.Id,
            WarehouseId = l.WarehouseId,
            WarehouseName = warehouses.ContainsKey(l.WarehouseId) ? warehouses[l.WarehouseId].WarehouseName : null,
            ProductId = l.ProductId,
            ProductSku = products.ContainsKey(l.ProductId) ? products[l.ProductId].Sku : null,
            MovementType = (int)l.MovementType,
            ReferenceType = l.ReferenceType,
            ReferenceNumber = l.ReferenceNumber,
            Quantity = l.Quantity,
            UomCode = l.UomCode,
            UnitCost = l.UnitCost ?? 0,
            TransactionDate = l.TransactionDate
        }).ToList();
    }
}

public class PriceListService : IPriceListService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;

    public PriceListService(IUnitOfWork uow, IMapper mapper)
    {
        _uow = uow; _mapper = mapper;
    }

    public async Task<IReadOnlyList<PriceListDto>> GetAllAsync(CancellationToken ct = default)
    {
        var items = await _uow.PriceLists.GetAllAsync();
        return _mapper.Map<IReadOnlyList<PriceListDto>>(items.OrderBy(p => p.PriceListCode));
    }

    public async Task<PriceListDto?> GetByProductAsync(Guid productId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow;
        var prices = await _uow.PriceLists.FindAsync(p =>
            p.ProductId == productId && p.IsActive &&
            p.EffectiveFrom <= today && (p.EffectiveTo == null || p.EffectiveTo >= today));
        var price = prices.OrderByDescending(p => p.Priority).FirstOrDefault();
        return price == null ? null : _mapper.Map<PriceListDto>(price);
    }

    public async Task<PriceListDto> CreateAsync(PriceListDto dto, CancellationToken ct = default)
    {
        var p = _mapper.Map<Domain.Entities.PriceList>(dto);
        p.Id = Guid.NewGuid();
        await _uow.PriceLists.AddAsync(p);
        await _uow.SaveChangesAsync();
        return _mapper.Map<PriceListDto>(p);
    }

    public async Task<PriceListDto?> UpdateAsync(Guid id, PriceListDto dto, CancellationToken ct = default)
    {
        var p = await _uow.PriceLists.GetByIdAsync(id);
        if (p == null) return null;
        p.PriceListName = dto.PriceListName;
        p.UnitPrice = dto.UnitPrice;
        p.EffectiveFrom = dto.EffectiveFrom;
        p.EffectiveTo = dto.EffectiveTo;
        p.IsActive = dto.IsActive;
        await _uow.PriceLists.UpdateAsync(p);
        await _uow.SaveChangesAsync();
        return _mapper.Map<PriceListDto>(p);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _uow.PriceLists.GetByIdAsync(id);
        if (p == null) return false;
        await _uow.PriceLists.DeleteAsync(p);
        await _uow.SaveChangesAsync();
        return true;
    }
}
