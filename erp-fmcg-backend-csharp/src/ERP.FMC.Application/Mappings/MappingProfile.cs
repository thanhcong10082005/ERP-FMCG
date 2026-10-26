using AutoMapper;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Application.DTOs;

namespace ERP.FMC.Application.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        // Partner
        CreateMap<Partner, PartnerDto>().ReverseMap();
        CreateMap<CreatePartnerDto, Partner>();
        CreateMap<UpdatePartnerDto, Partner>();

        // Product
        CreateMap<Product, ProductDto>()
            .ForMember(d => d.CategoryName, opt => opt.MapFrom(s => s.Category != null ? s.Category.CategoryName : null));
        CreateMap<CreateProductDto, Product>();
        CreateMap<UpdateProductDto, Product>();

        // Warehouse
        CreateMap<Warehouse, WarehouseDto>().ReverseMap();
        CreateMap<CreateWarehouseDto, Warehouse>();

        // Route
        CreateMap<Route, RouteDto>()
            .ForMember(d => d.PartnerCount, opt => opt.MapFrom(s => s.Partners != null ? s.Partners.Count : 0));
        CreateMap<CreateRouteDto, Route>();

        // Category
        CreateMap<Category, CategoryDto>().ReverseMap();

        // Sales Order
        CreateMap<SalesOrder, SalesOrderDto>()
            .ForMember(d => d.CustomerCode, opt => opt.MapFrom(s => s.Customer != null ? s.Customer.PartnerCode : null))
            .ForMember(d => d.CustomerName, opt => opt.MapFrom(s => s.Customer != null ? s.Customer.PartnerName : null))
            .ForMember(d => d.WarehouseName, opt => opt.MapFrom(s => s.Warehouse != null ? s.Warehouse.WarehouseName : null));
        CreateMap<CreateSalesOrderDto, SalesOrder>();

        // Sales Order Item
        CreateMap<SalesOrderItem, SalesOrderItemDto>()
            .ForMember(d => d.ProductName, opt => opt.MapFrom(s => s.Product != null ? s.Product.ProductName : null));
        CreateMap<CreateSalesOrderItemDto, SalesOrderItem>();

        // Inventory
        CreateMap<InventoryBalance, InventoryBalanceDto>()
            .ForMember(d => d.WarehouseName, opt => opt.MapFrom(s => s.Warehouse != null ? s.Warehouse.WarehouseName : null))
            .ForMember(d => d.ProductSku, opt => opt.MapFrom(s => s.Product != null ? s.Product.Sku : null))
            .ForMember(d => d.ProductName, opt => opt.MapFrom(s => s.Product != null ? s.Product.ProductName : null));

        CreateMap<StockLedger, StockLedgerDto>()
            .ForMember(d => d.WarehouseName, opt => opt.MapFrom(s => s.Warehouse != null ? s.Warehouse.WarehouseName : null))
            .ForMember(d => d.ProductSku, opt => opt.MapFrom(s => s.Product != null ? s.Product.Sku : null));

        // Price List
        CreateMap<PriceList, PriceListDto>()
            .ForMember(d => d.ProductSku, opt => opt.MapFrom(s => s.Product != null ? s.Product.Sku : null));
    }
}
