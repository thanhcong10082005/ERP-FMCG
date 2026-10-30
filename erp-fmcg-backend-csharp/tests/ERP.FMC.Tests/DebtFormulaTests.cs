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
// TEST CÔNG THỨC CÔNG NỢ
// -------------------------------------------------------------------
// Acceptance:
//   [ ] POD DELIVERED với old_debt_cash = 50.000
//         → partners.old_debt giảm 50.000, current_debt giảm 50.000
//   [ ] POD DELIVERED với old_debt_credit = 30.000
//         → old_debt giảm 30.000, current_debt tăng 30.000, TỔNG KHÔNG ĐỔI
//   [ ] POD new_order_credit = 20.000 → current_debt tăng 20.000
//   [ ] POD FAILED → công nợ KHÔNG đổi
//   [ ] Transaction rollback → công nợ không bị update dở
//
// ★ Vì sao test ở tầng service chứ không phải HTTP:
//   Logic công nợ nằm trong `DeliveryPodService` (Application layer).
//   Test service với mock UoW ⇒ nhanh, ổn định, không cần SQL Server.
// ===================================================================

public class DebtFormulaTests
{
    private readonly Mock<IUnitOfWork> _uow = new();

    private (DeliveryPodService svc, Guid tripOrderId, Guid salesOrderId, Guid partnerId) Setup(
        decimal oldDebt = 0m, decimal currentDebt = 0m, decimal totalAmount = 1_000_000m)
    {
        var partnerId = Guid.NewGuid();
        var salesOrderId = Guid.NewGuid();
        var tripOrderId = Guid.NewGuid();
        var tripId = Guid.NewGuid();

        var partner = new Partner
        {
            Id = partnerId,
            PartnerCode = "CT001",
            PartnerName = "Khách Test",
            OldDebt = oldDebt,
            CurrentDebt = currentDebt,
        };

        var salesOrder = new SalesOrder
        {
            Id = salesOrderId,
            OrderNumber = "SO001",
            CustomerId = partnerId,
            WarehouseId = Guid.NewGuid(),
            TotalAmount = totalAmount,
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

        _uow.Setup(u => u.DeliveryPods.GetAllAsync())
             .ReturnsAsync(new List<DeliveryPod>());
        _uow.Setup(u => u.DeliveryTripOrders.GetByIdAsync(tripOrderId))
             .ReturnsAsync(tripOrder);
        _uow.Setup(u => u.DeliveryTrips.GetByIdAsync(tripId))
             .ReturnsAsync(trip);
        _uow.Setup(u => u.SalesOrders.GetByIdAsync(salesOrderId))
             .ReturnsAsync(salesOrder);
        _uow.Setup(u => u.Partners.GetByIdAsync(partnerId))
             .ReturnsAsync(partner);
        _uow.Setup(u => u.SalesOrderItems.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<SalesOrderItem, bool>>>()))
             .ReturnsAsync(new List<SalesOrderItem>());
        _uow.Setup(u => u.Products.GetAllAsync())
             .ReturnsAsync(new List<Product>());
        _uow.Setup(u => u.InventoryBalances.GetAllAsync())
             .ReturnsAsync(new List<InventoryBalance>());
        _uow.Setup(u => u.DeliveryPods.AddAsync(It.IsAny<DeliveryPod>()))
             .ReturnsAsync((DeliveryPod p) => p);
        _uow.Setup(u => u.StockLedgers.AddAsync(It.IsAny<StockLedger>()))
             .ReturnsAsync((StockLedger s) => s);
        _uow.Setup(u => u.SaveChangesAsync()).ReturnsAsync(1);
        _uow.Setup(u => u.BeginTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.CommitTransactionAsync()).Returns(Task.CompletedTask);
        _uow.Setup(u => u.RollbackTransactionAsync()).Returns(Task.CompletedTask);

        var antiFraud = new AntiFraudService();

        // Bỏ qua chốt chặn anti-fraud để test riêng logic công nợ.
        var svc = new DeliveryPodService(
            _uow.Object, NullLogger<DeliveryPodService>.Instance, antiFraud,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AntiFraud:Enforce"] = "false"
                })
                .Build());

        return (svc, tripOrderId, salesOrderId, partnerId);
    }

    private static CreatePodRequestDto Pod(
        Guid tripOrderId,
        decimal newCash = 0, decimal newTransfer = 0, decimal newCredit = 0,
        decimal oldCash = 0, decimal oldTransfer = 0, decimal oldCredit = 0,
        string status = "DELIVERED")
        => new()
        {
            ClientPodId = Guid.NewGuid().ToString(),
            TripOrderId = tripOrderId.ToString(),
            DeliveryStatus = status,
            NewOrderCash = newCash,
            NewOrderTransfer = newTransfer,
            NewOrderCredit = newCredit,
            OldDebtCash = oldCash,
            OldDebtTransfer = oldTransfer,
            OldDebtCredit = oldCredit,
            TotalCollected = newCash + newTransfer + newCredit + oldCash + oldTransfer + oldCredit,
        };

    private Partner GetPartner(Guid partnerId)
        => _uow.Object.Partners.GetByIdAsync(partnerId).Result!;

    // ===================================================================
    // Acceptance
    // ===================================================================

    [Fact]
    public async Task OldDebtCash_Reduces_Both_OldDebt_And_CurrentDebt()
    {
        // current_debt = 500.000 (nợ hiện tại), old_debt = 200.000
        var (svc, tripOrderId, _, partnerId) = Setup(oldDebt: 200_000m, currentDebt: 500_000m);

        await svc.CreatePodAsync(Pod(tripOrderId, oldCash: 50_000m), null);

        var partner = GetPartner(partnerId);
        Assert.Equal(150_000m, partner.OldDebt);        // 200.000 − 50.000
        Assert.Equal(450_000m, partner.CurrentDebt);    // 500.000 − 50.000
    }

    [Fact]
    public async Task OldDebtCredit_Moves_Debt_From_Old_To_Current_Total_Unchanged()
    {
        var (svc, tripOrderId, _, partnerId) = Setup(oldDebt: 200_000m, currentDebt: 500_000m);
        var totalBefore = 200_000m + 500_000m;

        await svc.CreatePodAsync(Pod(tripOrderId, oldCredit: 30_000m), null);

        var partner = GetPartner(partnerId);
        Assert.Equal(170_000m, partner.OldDebt);        // 200.000 − 30.000
        Assert.Equal(530_000m, partner.CurrentDebt);    // 500.000 + 30.000
        Assert.Equal(totalBefore, partner.OldDebt + partner.CurrentDebt);  // ★ BẤT BIẾN
    }

    [Fact]
    public async Task NewOrderCredit_Increases_CurrentDebt()
    {
        var (svc, tripOrderId, _, partnerId) = Setup(oldDebt: 0m, currentDebt: 100_000m);

        await svc.CreatePodAsync(Pod(tripOrderId, newCredit: 20_000m), null);

        var partner = GetPartner(partnerId);
        Assert.Equal(120_000m, partner.CurrentDebt);
    }

    [Fact]
    public async Task NewOrderCash_Increases_PaidAmount_And_Sets_PaymentStatus()
    {
        var (svc, tripOrderId, salesOrderId, _) = Setup(totalAmount: 1_000_000m);

        await svc.CreatePodAsync(Pod(tripOrderId, newCash: 400_000m), null);

        var order = await _uow.Object.SalesOrders.GetByIdAsync(salesOrderId);
        Assert.Equal(400_000m, order!.PaidAmount);
        Assert.Equal("PARTIAL", order.PaymentStatus);
    }

    [Fact]
    public async Task FailedPod_Does_Not_Change_Debt()
    {
        var (svc, tripOrderId, _, partnerId) = Setup(oldDebt: 200_000m, currentDebt: 500_000m);

        await svc.CreatePodAsync(
            Pod(tripOrderId, newCredit: 20_000m, oldCash: 50_000m, status: "FAILED"),
            null);

        var partner = GetPartner(partnerId);
        Assert.Equal(200_000m, partner.OldDebt);        // KHÔNG đổi
        Assert.Equal(500_000m, partner.CurrentDebt);    // KHÔNG đổi
    }

    [Fact]
    public async Task Collecting_More_Than_OldDebt_Is_Clamped_To_Zero()
    {
        // Khách chỉ có 100.000 nợ cũ nhưng tài xế thu 300.000 ⇒ clamp về 0.
        var (svc, tripOrderId, _, partnerId) = Setup(oldDebt: 100_000m, currentDebt: 0m);

        await svc.CreatePodAsync(Pod(tripOrderId, oldCash: 300_000m), null);

        var partner = GetPartner(partnerId);
        Assert.Equal(0m, partner.OldDebt);
        Assert.Equal(0m, partner.CurrentDebt);   // không âm
    }

    // ===================================================================
    // IDEMPOTENCY
    // ===================================================================

    [Fact]
    public async Task Duplicate_ClientPodId_Returns_Existing_Pod_Without_Creating_New()
    {
        var (svc, tripOrderId, _, partnerId) = Setup(oldDebt: 200_000m, currentDebt: 0m);

        var clientPodId = Guid.NewGuid().ToString();
        var request = Pod(tripOrderId, oldCash: 50_000m);
        request.ClientPodId = clientPodId;

        var first = await svc.CreatePodAsync(request, null);
        Assert.True(first.Created);

        // Giờ giả lập lần gửi thứ 2: service đọc lại danh sách POD đã có.
        var existing = new DeliveryPod
        {
            Id = first.PodId != null ? Guid.Parse(first.PodId) : Guid.NewGuid(),
            ClientPodId = Guid.Parse(clientPodId),
            TripOrderId = tripOrderId,
            SalesOrderId = first.SalesOrderId != null ? Guid.Parse(first.SalesOrderId) : Guid.NewGuid(),
            PartnerId = partnerId,
            DeliveryStatus = "DELIVERED",
            TotalCollected = 50_000m,
        };
        _uow.Setup(u => u.DeliveryPods.GetAllAsync())
             .ReturnsAsync(new List<DeliveryPod> { existing });

        var second = await svc.CreatePodAsync(request, null);

        Assert.False(second.Created);
        Assert.Equal(first.PodId, second.PodId);

        // Công nợ KHÔNG bị trừ 2 lần.
        var partner = GetPartner(partnerId);
        Assert.Equal(150_000m, partner.OldDebt);
    }
}
