using AutoMapper;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Application.Services;

public class ProductService : IProductService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<ProductService> _logger;

    public ProductService(IUnitOfWork uow, IMapper mapper, ILogger<ProductService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<PagedResult<ProductDto>> GetPagedAsync(ProductFilterDto filter, CancellationToken ct = default)
    {
        var query = (await _uow.Products.GetAllAsync()).AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.ToLower();
            query = query.Where(p => p.Sku.ToLower().Contains(s) || p.ProductName.ToLower().Contains(s));
        }
        if (!string.IsNullOrWhiteSpace(filter.Brand))
            query = query.Where(p => p.Brand == filter.Brand);
        if (filter.CategoryId.HasValue)
            query = query.Where(p => p.CategoryId == filter.CategoryId);
        if (filter.IsActive.HasValue)
            query = query.Where(p => p.IsActive == filter.IsActive);

        var total = query.Count();
        var items = query
            .OrderBy(p => p.Sku)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        return new PagedResult<ProductDto>
        {
            Items = _mapper.Map<IReadOnlyList<ProductDto>>(items),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<ProductDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _uow.Products.GetByIdAsync(id);
        return product == null ? null : _mapper.Map<ProductDto>(product);
    }

    public async Task<ProductDto?> GetBySkuAsync(string sku, CancellationToken ct = default)
    {
        var product = await _uow.Products.FirstOrDefaultAsync(p => p.Sku == sku);
        return product == null ? null : _mapper.Map<ProductDto>(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken ct = default)
    {
        if (await _uow.Products.ExistsAsync(p => p.Sku == dto.Sku))
            throw new InvalidOperationException($"SKU '{dto.Sku}' already exists");

        var product = _mapper.Map<Product>(dto);
        product.Id = Guid.NewGuid();

        await _uow.Products.AddAsync(product);
        await _uow.SaveChangesAsync();
        _logger.LogInformation("Created product {Sku} - {Name}", product.Sku, product.ProductName);

        return _mapper.Map<ProductDto>(product);
    }

    public async Task<ProductDto?> UpdateAsync(Guid id, UpdateProductDto dto, CancellationToken ct = default)
    {
        var product = await _uow.Products.GetByIdAsync(id);
        if (product == null) return null;

        product.ProductName = dto.ProductName;
        product.ShortName = dto.ShortName;
        product.Brand = dto.Brand;
        product.CategoryId = dto.CategoryId;
        product.BasePrice = dto.BasePrice;
        product.VatRate = dto.VatRate;
        product.IsActive = dto.IsActive;

        await _uow.Products.UpdateAsync(product);
        await _uow.SaveChangesAsync();
        return _mapper.Map<ProductDto>(product);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var product = await _uow.Products.GetByIdAsync(id);
        if (product == null) return false;

        await _uow.Products.DeleteAsync(product);
        await _uow.SaveChangesAsync();
        return true;
    }
}
