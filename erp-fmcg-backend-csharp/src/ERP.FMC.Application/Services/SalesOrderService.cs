using ERP.FMC.Application.Mappings;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using ERP.FMC.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Application.Services;

public class SalesOrderService : ISalesOrderService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<SalesOrderService> _logger;

    public SalesOrderService(IUnitOfWork uow, ILogger<SalesOrderService> logger)
    {
        _uow = uow; _logger = logger;
    }

    public async Task<PagedResult<SalesOrderDto>> GetPagedAsync(SalesOrderFilterDto filter, CancellationToken ct = default)
    {
        var all = (await _uow.SalesOrders.GetAllAsync()).AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.ToLower();
            all = all.Where(o => o.OrderNumber.ToLower().Contains(s));
        }
        if (filter.CustomerId.HasValue)
            all = all.Where(o => o.CustomerId == filter.CustomerId);
        if (filter.WarehouseId.HasValue)
            all = all.Where(o => o.WarehouseId == filter.WarehouseId);
        if (!string.IsNullOrWhiteSpace(filter.Status))
            all = all.Where(o => o.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.PaymentStatus))
            all = all.Where(o => o.PaymentStatus == filter.PaymentStatus);
        if (filter.FromDate.HasValue)
            all = all.Where(o => o.OrderDate >= filter.FromDate);
        if (filter.ToDate.HasValue)
            all = all.Where(o => o.OrderDate <= filter.ToDate);

        var total = all.Count();
        var items = all.OrderByDescending(o => o.OrderDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        return new PagedResult<SalesOrderDto>
        {
            Items = items.Select(x => x.ToDto()).ToList(),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<SalesOrderDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var order = await _uow.SalesOrders.GetByIdAsync(id);
        if (order == null) return null;

        var items = await _uow.SalesOrderItems.FindAsync(i => i.SalesOrderId == id);
        var dto = order.ToDto();
        dto.Items = items.OrderBy(i => i.LineNumber).Select(x => x.ToDto()).ToList();
        return dto;
    }

    public async Task<SalesOrderDto?> GetByNumberAsync(string orderNumber, CancellationToken ct = default)
    {
        var order = await _uow.SalesOrders.FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
        return order == null ? null : await GetByIdAsync(order.Id);
    }

    public async Task<SalesOrderDto> CreateAsync(CreateSalesOrderDto dto, CancellationToken ct = default)
    {
        if (!dto.Items.Any())
            throw new InvalidOperationException("Order must have at least one item");

        await _uow.BeginTransactionAsync();
        try
        {
            var order = new SalesOrder
            {
                Id = Guid.NewGuid(),
                OrderNumber = await GenerateOrderNumberAsync(),
                CustomerId = dto.CustomerId,
                WarehouseId = dto.WarehouseId,
                OrderDate = dto.OrderDate,
                DeliveryDate = dto.DeliveryDate,
                Status = OrderStatus.Confirmed.ToString(),
                PaymentStatus = PaymentStatus.Unpaid.ToString(),
                Notes = dto.Notes
            };

            decimal subtotal = 0, discount = 0, tax = 0, total = 0;
            var items = new List<SalesOrderItem>();
            int lineNo = 1;

            foreach (var itemDto in dto.Items)
            {
                var product = await _uow.Products.GetByIdAsync(itemDto.ProductId);
                if (product == null)
                    throw new InvalidOperationException($"Product {itemDto.ProductId} not found");

                var itemSubtotal = itemDto.OrderedQty * itemDto.UnitPrice;
                var itemDiscount = itemSubtotal * itemDto.DiscountPercent / 100m;
                var itemTax = (itemSubtotal - itemDiscount) * product.VatRate / 100m;
                var itemTotal = itemSubtotal - itemDiscount + itemTax;

                subtotal += itemSubtotal;
                discount += itemDiscount;
                tax += itemTax;
                total += itemTotal;

                items.Add(new SalesOrderItem
                {
                    Id = Guid.NewGuid(),
                    SalesOrderId = order.Id,
                    LineNumber = lineNo++,
                    ProductId = itemDto.ProductId,
                    ProductSku = product.Sku,
                    UomCode = product.BaseUom,
                    OrderedQty = itemDto.OrderedQty,
                    BaseQty = itemDto.OrderedQty,
                    UnitPrice = itemDto.UnitPrice,
                    DiscountPercent = itemDto.DiscountPercent,
                    DiscountAmount = itemDiscount,
                    LineSubtotal = itemSubtotal,
                    LineTotal = itemTotal,
                    DeliveredQty = 0,
                    Notes = itemDto.Notes
                });
            }

            order.Subtotal = subtotal;
            order.DiscountAmount = discount;
            order.TaxAmount = tax;
            order.TotalAmount = total;

            await _uow.SalesOrders.AddAsync(order);
            foreach (var it in items)
                await _uow.SalesOrderItems.AddAsync(it);

            await _uow.SaveChangesAsync();
            await _uow.CommitTransactionAsync();

            _logger.LogInformation("Created order {Number} total={Total}", order.OrderNumber, order.TotalAmount);

            var result = await GetByIdAsync(order.Id);
            return result!;
        }
        catch
        {
            await _uow.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task<SalesOrderDto?> UpdateAsync(Guid id, UpdateSalesOrderDto dto, CancellationToken ct = default)
    {
        var order = await _uow.SalesOrders.GetByIdAsync(id);
        if (order == null) return null;

        if (dto.DeliveryDate.HasValue) order.DeliveryDate = dto.DeliveryDate;
        if (!string.IsNullOrEmpty(dto.Status)) order.Status = dto.Status;
        if (!string.IsNullOrEmpty(dto.PaymentStatus)) order.PaymentStatus = dto.PaymentStatus;
        if (dto.PaidAmount.HasValue) order.PaidAmount = dto.PaidAmount.Value;
        if (dto.Notes != null) order.Notes = dto.Notes;

        await _uow.SalesOrders.UpdateAsync(order);
        await _uow.SaveChangesAsync();
        return order.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var order = await _uow.SalesOrders.GetByIdAsync(id);
        if (order == null) return false;

        await _uow.SalesOrders.DeleteAsync(order);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<SalesOrderDto?> AddItemAsync(Guid orderId, CreateSalesOrderItemDto dto, CancellationToken ct = default)
    {
        var order = await _uow.SalesOrders.GetByIdAsync(orderId);
        if (order == null) return null;

        var product = await _uow.Products.GetByIdAsync(dto.ProductId);
        if (product == null) throw new InvalidOperationException($"Product {dto.ProductId} not found");

        var existing = await _uow.SalesOrderItems.FindAsync(i => i.SalesOrderId == orderId);
        var lineNo = existing.Any() ? existing.Max(i => i.LineNumber) + 1 : 1;

        var subtotal = dto.OrderedQty * dto.UnitPrice;
        var discount = subtotal * dto.DiscountPercent / 100m;
        var tax = (subtotal - discount) * product.VatRate / 100m;
        var total = subtotal - discount + tax;

        var item = new SalesOrderItem
        {
            Id = Guid.NewGuid(),
            SalesOrderId = orderId,
            LineNumber = lineNo,
            ProductId = dto.ProductId,
            ProductSku = product.Sku,
            UomCode = product.BaseUom,
            OrderedQty = dto.OrderedQty,
            BaseQty = dto.OrderedQty,
            UnitPrice = dto.UnitPrice,
            DiscountPercent = dto.DiscountPercent,
            DiscountAmount = discount,
            LineSubtotal = subtotal,
            LineTotal = total,
            Notes = dto.Notes
        };

        await _uow.SalesOrderItems.AddAsync(item);

        order.Subtotal += subtotal;
        order.DiscountAmount += discount;
        order.TaxAmount += tax;
        order.TotalAmount += total;
        await _uow.SalesOrders.UpdateAsync(order);

        await _uow.SaveChangesAsync();
        return await GetByIdAsync(orderId);
    }

    public async Task<bool> RemoveItemAsync(Guid orderId, Guid itemId, CancellationToken ct = default)
    {
        var item = await _uow.SalesOrderItems.GetByIdAsync(itemId);
        if (item == null || item.SalesOrderId != orderId) return false;

        await _uow.SalesOrderItems.DeleteAsync(item);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkAsConfirmedAsync(Guid id, CancellationToken ct = default)
    {
        var o = await _uow.SalesOrders.GetByIdAsync(id);
        if (o == null) return false;
        o.Status = OrderStatus.Confirmed.ToString();
        await _uow.SalesOrders.UpdateAsync(o);
        await _uow.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MarkAsDeliveredAsync(Guid id, CancellationToken ct = default)
    {
        var o = await _uow.SalesOrders.GetByIdAsync(id);
        if (o == null) return false;
        o.Status = OrderStatus.Delivered.ToString();

        var items = await _uow.SalesOrderItems.FindAsync(i => i.SalesOrderId == id);
        foreach (var i in items) i.DeliveredQty = i.OrderedQty;

        await _uow.SaveChangesAsync();
        return true;
    }

    private async Task<string> GenerateOrderNumberAsync()
    {
        var prefix = "SO";
        var today = DateTime.Now.ToString("yyyyMMdd");
        var count = await _uow.SalesOrders.CountAsync(o => o.OrderNumber.StartsWith($"{prefix}{today}"));
        return $"{prefix}{today}-{(count + 1):D5}";
    }
}
