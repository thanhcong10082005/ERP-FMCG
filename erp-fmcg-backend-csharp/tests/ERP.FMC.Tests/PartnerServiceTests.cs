using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Application.Mappings;
using ERP.FMC.Application.Services;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ERP.FMC.Tests;

public class PartnerServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly PartnerService _service;

    public PartnerServiceTests()
    {
        // ★ 6C: AutoMapper đã gỡ — dùng mapping thủ công (EntityMapper).
        _service = new PartnerService(_uow.Object, NullLogger<PartnerService>.Instance);
    }

    [Fact]
    public async Task GetByIdAsync_Returns_Partner_When_Exists()
    {
        var id = Guid.NewGuid();
        var partner = new Partner
        {
            Id = id,
            PartnerCode = "CT001",
            PartnerName = "Test Customer",
            PartnerType = "STORE",
            IsActive = true
        };

        _uow.Setup(u => u.Partners.GetByIdAsync(id))
            .ReturnsAsync(partner);

        var result = await _service.GetByIdAsync(id);

        Assert.NotNull(result);
        Assert.Equal("CT001", result!.PartnerCode);
        Assert.Equal("Test Customer", result.PartnerName);
    }

    [Fact]
    public async Task GetByIdAsync_Returns_Null_When_NotFound()
    {
        var id = Guid.NewGuid();
        _uow.Setup(u => u.Partners.GetByIdAsync(id))
            .ReturnsAsync((Partner?)null);

        var result = await _service.GetByIdAsync(id);

        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_Throws_When_Duplicate_Code()
    {
        _uow.Setup(u => u.Partners.ExistsAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Partner, bool>>>()))
            .ReturnsAsync(true);

        var dto = new CreatePartnerDto
        {
            PartnerCode = "CT001",
            PartnerName = "Duplicate Test",
            PartnerType = "STORE"
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(dto));
    }
}

public class ProductServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly ProductService _service;

    public ProductServiceTests()
    {
        // ★ 6C: AutoMapper đã gỡ — dùng mapping thủ công (EntityMapper).
        _service = new ProductService(_uow.Object, NullLogger<ProductService>.Instance);
    }

    [Fact]
    public async Task GetBySkuAsync_Returns_Product()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = "TEST-SKU-001",
            ProductName = "Test Product",
            BaseUom = "CAI",
            BasePrice = 100000m
        };
        _uow.Setup(u => u.Products.FirstOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Product, bool>>>()))
            .ReturnsAsync(product);

        var result = await _service.GetBySkuAsync("TEST-SKU-001");

        Assert.NotNull(result);
        Assert.Equal(100000m, result!.BasePrice);
    }
}
