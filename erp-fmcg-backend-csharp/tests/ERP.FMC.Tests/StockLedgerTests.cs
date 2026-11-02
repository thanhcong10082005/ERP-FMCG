using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Services;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ERP.FMC.Tests;

// ===================================================================
// TEST TỒN KHO — POD KHÔNG BAO GIỜ TẠO TỒN ÂM
// -------------------------------------------------------------------
// Sự cố đã xảy ra thật:
//   Smoke test E2E tạo POD giao 10 RCS SKU 10000854 (Aquafina).
//   `inventory_balances` của SKU này có tồn = 0 (chưa có nguồn tồn
//   đầu kỳ) ⇒ bút toán SALE trừ 10 ⇒ tồn = -10 ⇒
//     - check OPS-06 / OPS-07 FAIL
//     - `quantity_available` âm ⇒ báo cáo tồn kho & cảnh báo sai
//
// Nguyên nhân gốc:
//   Code cũ chỉ GHI CẢNH BÁO ("CANH_BAO_TON_AM") rồi vẫn trừ hết số
//   lượng giao thực tế. Ngoài ra entity `InventoryBalance` thiếu
//   property `UomCode` ⇒ không phân biệt được tồn theo đơn vị tính.
//
// Cách sửa:
//   1. Nếu tồn thực < số lượng giao ⇒ tự động ghi bút toán OPENING
//      bù phần thiếu (giá vốn lấy từ price_lists / base_price).
//   2. Bút toán SALE ghi đúng số lượng giao thực tế ⇒ tồn không âm.
//   3. Trigger DB (migration 12) chặn bút toán làm tồn âm.
// ===================================================================

public class StockLedgerTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<StockLedger> _ledgers = new();

    private (DeliveryPodService svc, Guid tripOrderId, Guid salesOrderId, Guid productId,
             Guid warehouseId) Setup(
        decimal onHand = 0m,
        string balanceUom = "RCS",
        decimal priceListPrice = 92_000m,
        decimal basePrice = 92_000m)
    {
        var partnerId = Guid.NewGuid();
        var salesOrderId = Guid.NewGuid();
        var tripOrderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();

        var partner = new Partner
        {
            Id = partnerId,
            PartnerCode = "CT001",
            PartnerName = "Khách Test",
            OldDebt = 0m,
            CurrentDebt = 0m,
        };

        var salesOrder = new SalesOrder
        {
            Id = salesOrderId,
            OrderNumber = "SO001",
            CustomerId = partnerId,
            WarehouseId = warehouseId,
            TotalAmount = 1_000_000m,
            PaidAmount = 0,
            Status = "CONFIRMED",
            PaymentStatus = "UNPAID",
        };

        var trip = new DeliveryTrip
        {
            Id = tripId,
            TripNumber = "TRIP-TEST",
            TripDate = DateTime.Today,
            Status = "DELIVERING",
            DriverId = null,
        };

        var tripOrder = new DeliveryTripOrder
        {
            Id = tripOrderId,
            TripId = tripId,
            SalesOrderId = salesOrderId,
            Status = "PENDING",
        };

        var orderItem = new SalesOrderItem
        {
            Id = Guid.NewGuid(),
            SalesOrderId = salesOrderId,
            ProductId = productId,
            ProductSku = "10000854",
            UomCode = "RCS",
            OrderedQty = 10m,
            UnitPrice = 92_000m,
            LineTotal = 920_000m,
        };

        var product = new Product
        {
            Id = productId,
            Sku = "10000854",
            ProductName = "Aquafina Chai nhựa 500x24",
            BaseUom = "RCS",
            BasePrice = basePrice,
        };

        var balances = new List<InventoryBalance>
        {
            new()
            {
                Id = Guid.NewGuid(),
                WarehouseId = warehouseId,
                ProductId = productId,
                UomCode = balanceUom,
                OnHand = onHand,
                LastCost = 90_000m,
            }
        };

        var priceLists = new List<PriceList>
        {
            new()
            {
                Id = Guid.NewGuid(),
                PriceListCode = "PL001",
                PriceListName = "Bang gia chuan",
                ProductId = productId,
                UnitPrice = priceListPrice,
                EffectiveFrom = DateTime.UtcNow.AddDays(-1),
                IsActive = true,
                Priority = 0,
            }
        };

        _uow.Setup(u => u.DeliveryPods.GetAllAsync()).ReturnsAsync(new List<DeliveryPod>());
        _uow.Setup(u => u.DeliveryTripOrders.GetByIdAsync(tripOrderId)).ReturnsAsync(tripOrder);
        _uow.Setup(u => u.DeliveryTrips.GetByIdAsync(tripId)).ReturnsAsync(trip);
        _uow.Setup(u => u.SalesOrders.GetByIdAsync(salesOrderId)).ReturnsAsync(salesOrder);
        _uow.Setup(u => u.Partners.GetByIdAsync(partnerId)).ReturnsAsync(partner);
        _uow.Setup(u => u.SalesOrderItems.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<SalesOrderItem, bool>>>()))
            .ReturnsAsync(new List<SalesOrderItem> { orderItem });
        _uow.Setup(u => u.Products.GetAllAsync()).ReturnsAsync(new List<Product> { product });
        _uow.Setup(u => u.InventoryBalances.GetAllAsync()).ReturnsAsync(balances);
        _uow.Setup(u => u.PriceLists.FindAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<PriceList, bool>>>()))
            .ReturnsAsync(priceLists);
        _uow.Setup(u => u.DeliveryPods.AddAsync(It.IsAny<DeliveryPod>()))
            .ReturnsAsync((DeliveryPod p) => p);
        _uow.Setup(u => u.StockLedgers.AddAsync(It.IsAny<StockLedger>()))
            .ReturnsAsync((StockLedger s) => { _ledgers.Add(s); return s; });
        _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _uow.Setup(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<Task<bool>> op, CancellationToken _) => op());
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        var antiFraud = new AntiFraudService();
        var svc = new DeliveryPodService(
            _uow.Object, NullLogger<DeliveryPodService>.Instance, antiFraud,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AntiFraud:Enforce"] = "false"
                })
                .Build());

        return (svc, tripOrderId, salesOrderId, productId, warehouseId);
    }

    private static CreatePodRequestDto Pod(Guid tripOrderId, string status = "DELIVERED")
        => new()
        {
            ClientPodId = Guid.NewGuid().ToString(),
            TripOrderId = tripOrderId.ToString(),
            DeliveryStatus = status,
            NewOrderCash = 100_000m,
            TotalCollected = 100_000m,
        };

    // ===================================================================
    // Acceptance
    // ===================================================================

    [Fact]
    public async Task Pod_Without_Stock_Creates_Opening_Then_Sale_And_Never_Goes_Negative()
    {
        // Tồn = 0, giao 10 ⇒ phải nạp opening 10 rồi xuất 10 ⇒ tồn = 0.
        var (svc, tripOrderId, _, _, _) = Setup(onHand: 0m);

        await svc.CreatePodAsync(Pod(tripOrderId), null);

        Assert.Equal(2, _ledgers.Count);

        var opening = _ledgers.Single(l => l.TransactionType == "OPENING");
        var sale = _ledgers.Single(l => l.TransactionType == "SALE");

        Assert.Equal(10m, opening.QuantityIn);
        Assert.Equal(0m, opening.QuantityOut);
        Assert.Equal(10m, sale.QuantityOut);
        Assert.Equal(0m, sale.QuantityIn);

        // Net của ledger = 0 ⇒ tồn không âm.
        var net = _ledgers.Sum(l => l.QuantityIn - l.QuantityOut);
        Assert.Equal(0m, net);
    }

    [Fact]
    public async Task Pod_Without_Stock_Uses_PriceList_As_Opening_UnitCost()
    {
        var (svc, tripOrderId, _, _, _) = Setup(
            onHand: 0m, priceListPrice: 92_000m, basePrice: 50_000m);

        await svc.CreatePodAsync(Pod(tripOrderId), null);

        var opening = _ledgers.Single(l => l.TransactionType == "OPENING");
        Assert.Equal(92_000m, opening.UnitCost);
    }

    [Fact]
    public async Task Pod_Without_Stock_Falls_Back_To_BasePrice_When_NoPriceList()
    {
        var (svc, tripOrderId, _, _, _) = Setup(
            onHand: 0m, priceListPrice: 0m, basePrice: 77_000m);

        await svc.CreatePodAsync(Pod(tripOrderId), null);

        var opening = _ledgers.Single(l => l.TransactionType == "OPENING");
        Assert.Equal(77_000m, opening.UnitCost);
    }

    [Fact]
    public async Task Pod_Partial_Stock_Top_Up_Only_The_Shortfall()
    {
        // Tồn = 4, giao 10 ⇒ nạp opening 6, xuất 10 ⇒ tồn = 0.
        var (svc, tripOrderId, _, _, _) = Setup(onHand: 4m);

        await svc.CreatePodAsync(Pod(tripOrderId), null);

        var opening = _ledgers.Single(l => l.TransactionType == "OPENING");
        var sale = _ledgers.Single(l => l.TransactionType == "SALE");

        Assert.Equal(6m, opening.QuantityIn);
        Assert.Equal(10m, sale.QuantityOut);

        // Tồn cuối = tồn đầu kỳ (4) + net ledger (6 − 10) = 0 ⇒ không âm.
        var net = _ledgers.Sum(l => l.QuantityIn - l.QuantityOut);
        Assert.Equal(0m, 4m + net);
    }

    [Fact]
    public async Task Pod_With_Enough_Stock_Does_Not_Create_Opening()
    {
        // Tồn = 100, giao 10 ⇒ không cần nạp bù.
        var (svc, tripOrderId, _, _, _) = Setup(onHand: 100m);

        await svc.CreatePodAsync(Pod(tripOrderId), null);

        Assert.Single(_ledgers);
        var sale = _ledgers.Single();
        Assert.Equal("SALE", sale.TransactionType);
        Assert.Equal(10m, sale.QuantityOut);
    }

    [Fact]
    public async Task FailedPod_Does_Not_Write_StockLedger()
    {
        var (svc, tripOrderId, _, _, _) = Setup(onHand: 0m);

        await svc.CreatePodAsync(Pod(tripOrderId, status: "FAILED"), null);

        Assert.Empty(_ledgers);
    }

    [Fact]
    public async Task Negative_Existing_Stock_Is_Treated_As_Zero_And_Topped_Up()
    {
        // Dữ liệu cũ có tồn âm (-10) ⇒ không được nhân thêm âm.
        var (svc, tripOrderId, _, _, _) = Setup(onHand: -10m);

        await svc.CreatePodAsync(Pod(tripOrderId), null);

        var opening = _ledgers.Single(l => l.TransactionType == "OPENING");
        var sale = _ledgers.Single(l => l.TransactionType == "SALE");

        Assert.Equal(10m, opening.QuantityIn);   // bù đúng số lượng giao
        Assert.Equal(10m, sale.QuantityOut);
    }

    [Fact]
    public async Task StockLedger_Uses_Uom_From_Order_Item()
    {
        var (svc, tripOrderId, _, _, _) = Setup(onHand: 0m, balanceUom: "RCS");

        await svc.CreatePodAsync(Pod(tripOrderId), null);

        Assert.All(_ledgers, l => Assert.Equal("RCS", l.UomCode));
    }

    [Fact]
    public void InventoryBalance_Exposes_UomCode()
    {
        // Entity phải có UomCode vì bảng có cột uom_code NOT NULL và
        // UNIQUE key (warehouse_id, product_id, uom_code).
        var b = new InventoryBalance { UomCode = "RCS" };
        Assert.Equal("RCS", b.UomCode);
    }
}
