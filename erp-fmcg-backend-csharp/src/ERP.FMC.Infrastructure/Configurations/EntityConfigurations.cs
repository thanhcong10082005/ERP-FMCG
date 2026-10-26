using ERP.FMC.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.FMC.Infrastructure.Configurations;

public class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.HasIndex(p => p.PartnerCode).IsUnique();
        builder.HasIndex(p => p.RouteCode);
        builder.HasIndex(p => p.PartnerType);

        builder.HasOne(p => p.Route)
            .WithMany(r => r.Partners)
            .HasForeignKey(p => p.RouteId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasIndex(p => p.Sku).IsUnique();

        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => o.OrderDate);
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => new { o.CustomerId, o.OrderDate });

        builder.HasOne(o => o.Customer)
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.Warehouse)
            .WithMany()
            .HasForeignKey(o => o.WarehouseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(o => o.SalePerson)
            .WithMany()
            .HasForeignKey(o => o.SalePersonId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(o => o.Items)
            .WithOne(i => i.SalesOrder)
            .HasForeignKey(i => i.SalesOrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class SalesOrderItemConfiguration : IEntityTypeConfiguration<SalesOrderItem>
{
    public void Configure(EntityTypeBuilder<SalesOrderItem> builder)
    {
        builder.HasIndex(i => new { i.SalesOrderId, i.LineNumber }).IsUnique();
        builder.HasIndex(i => i.ProductId);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> builder)
    {
        builder.HasIndex(b => new { b.WarehouseId, b.ProductId }).IsUnique();
    }
}

public class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.HasIndex(p => p.PriceListCode);
        builder.HasIndex(p => new { p.EffectiveFrom, p.EffectiveTo });
    }
}

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.HasIndex(w => w.WarehouseCode).IsUnique();
    }
}

public class RouteConfiguration : IEntityTypeConfiguration<Route>
{
    public void Configure(EntityTypeBuilder<Route> builder)
    {
        builder.HasIndex(r => r.RouteCode).IsUnique();
    }
}

public class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.HasIndex(u => u.Username).IsUnique();
        builder.HasIndex(u => u.Email);
    }
}

public class StockLedgerConfiguration : IEntityTypeConfiguration<StockLedger>
{
    public void Configure(EntityTypeBuilder<StockLedger> builder)
    {
        builder.HasIndex(s => new { s.WarehouseId, s.ProductId, s.TransactionDate });
        builder.HasIndex(s => s.ReferenceType);
    }
}
