using Xunit;

namespace ERP.FMC.Tests;

/// <summary>
/// Smoke tests cho toàn bộ project structure.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void Project_Structure_Is_Valid()
    {
        // Verify key types are accessible
        var partnerType = typeof(ERP.FMC.Domain.Entities.Partner);
        var productType = typeof(ERP.FMC.Domain.Entities.Product);
        var orderType = typeof(ERP.FMC.Domain.Entities.SalesOrder);
        var itemType = typeof(ERP.FMC.Domain.Entities.SalesOrderItem);

        Assert.NotNull(partnerType);
        Assert.NotNull(productType);
        Assert.NotNull(orderType);
        Assert.NotNull(itemType);
    }

    [Fact]
    public void Enums_Are_Defined()
    {
        var draft = ERP.FMC.Domain.Enums.OrderStatus.Draft;
        var paid = ERP.FMC.Domain.Enums.PaymentStatus.Paid;
        var store = ERP.FMC.Domain.Enums.PartnerType.Store;
        var inbound = ERP.FMC.Domain.Enums.MovementType.In;

        Assert.Equal(0, (int)draft);
        Assert.Equal(2, (int)paid);
        Assert.Equal(0, (int)store);
        Assert.Equal(0, (int)inbound);
    }
}
