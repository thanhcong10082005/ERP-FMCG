namespace ERP.FMC.Domain.Enums;

public enum OrderStatus
{
    Draft = 0,
    Confirmed = 1,
    Shipped = 2,
    Delivered = 3,
    Invoiced = 4,
    Cancelled = 9
}

public enum PaymentStatus
{
    Unpaid = 0,
    Partial = 1,
    Paid = 2,
    Overdue = 3
}

public enum PartnerType
{
    Store = 0,
    ASO = 1,
    Corporate = 2,
    Warehouse = 3
}

public enum MovementType
{
    In = 0,
    Out = 1,
    Transfer = 2,
    Adjustment = 3
}
