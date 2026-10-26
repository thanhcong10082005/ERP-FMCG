using AutoMapper;
using ERP.FMC.Application.DTOs;
using ERP.FMC.Application.Interfaces;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Linq.Expressions;

namespace ERP.FMC.Application.Services;

public class PartnerService : IPartnerService
{
    private readonly IUnitOfWork _uow;
    private readonly IMapper _mapper;
    private readonly ILogger<PartnerService> _logger;

    public PartnerService(IUnitOfWork uow, IMapper mapper, ILogger<PartnerService> logger)
    {
        _uow = uow;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<PagedResult<PartnerDto>> GetPagedAsync(PartnerFilterDto filter, CancellationToken ct = default)
    {
        var predicate = BuildPredicate(filter);
        var skip = (filter.Page - 1) * filter.PageSize;

        var all = (await _uow.Partners.GetAllAsync()).AsEnumerable();
        if (predicate != null) all = all.Where(predicate);

        var total = all.Count();
        var items = all.OrderBy(p => p.PartnerCode).Skip(skip).Take(filter.PageSize).ToList();

        return new PagedResult<PartnerDto>
        {
            Items = _mapper.Map<IReadOnlyList<PartnerDto>>(items),
            TotalCount = total,
            Page = filter.Page,
            PageSize = filter.PageSize
        };
    }

    public async Task<PartnerDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var partner = await _uow.Partners.GetByIdAsync(id);
        return partner == null ? null : _mapper.Map<PartnerDto>(partner);
    }

    public async Task<PartnerDto?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var partner = await _uow.Partners.FirstOrDefaultAsync(p => p.PartnerCode == code);
        return partner == null ? null : _mapper.Map<PartnerDto>(partner);
    }

    public async Task<PartnerDto> CreateAsync(CreatePartnerDto dto, CancellationToken ct = default)
    {
        if (await _uow.Partners.ExistsAsync(p => p.PartnerCode == dto.PartnerCode))
            throw new InvalidOperationException($"Partner code '{dto.PartnerCode}' already exists");

        var partner = _mapper.Map<Partner>(dto);
        partner.Id = Guid.NewGuid();

        await _uow.Partners.AddAsync(partner);
        await _uow.SaveChangesAsync();
        _logger.LogInformation("Created partner {Code} - {Name}", partner.PartnerCode, partner.PartnerName);

        return _mapper.Map<PartnerDto>(partner);
    }

    public async Task<PartnerDto?> UpdateAsync(Guid id, UpdatePartnerDto dto, CancellationToken ct = default)
    {
        var partner = await _uow.Partners.GetByIdAsync(id);
        if (partner == null) return null;

        partner.PartnerName = dto.PartnerName;
        partner.Phone = dto.Phone;
        partner.Email = dto.Email;
        partner.Address = dto.Address;
        partner.RouteCode = dto.RouteCode;
        partner.OutletTypeCode = dto.OutletTypeCode;
        partner.CreditLimit = dto.CreditLimit;
        partner.IsActive = dto.IsActive;

        await _uow.Partners.UpdateAsync(partner);
        await _uow.SaveChangesAsync();
        _logger.LogInformation("Updated partner {Id}", id);

        return _mapper.Map<PartnerDto>(partner);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var partner = await _uow.Partners.GetByIdAsync(id);
        if (partner == null) return false;

        await _uow.Partners.DeleteAsync(partner);
        await _uow.SaveChangesAsync();
        _logger.LogInformation("Deleted partner {Id}", id);
        return true;
    }

    private Func<Partner, bool>? BuildPredicate(PartnerFilterDto f)
    {
        Func<Partner, bool>? p = null;
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.ToLower();
            var prev = p;
            p = prev == null
                ? x => x.PartnerCode.ToLower().Contains(s) || x.PartnerName.ToLower().Contains(s)
                : (x => prev(x) && (x.PartnerCode.ToLower().Contains(s) || x.PartnerName.ToLower().Contains(s)));
        }
        if (!string.IsNullOrWhiteSpace(f.PartnerType))
        {
            var t = f.PartnerType;
            var prev = p;
            p = prev == null ? x => x.PartnerType == t : (x => prev(x) && x.PartnerType == t);
        }
        if (!string.IsNullOrWhiteSpace(f.RouteCode))
        {
            var r = f.RouteCode;
            var prev = p;
            p = prev == null ? x => x.RouteCode == r : (x => prev(x) && x.RouteCode == r);
        }
        if (f.IsActive.HasValue)
        {
            var a = f.IsActive.Value;
            var prev = p;
            p = prev == null ? x => x.IsActive == a : (x => prev(x) && x.IsActive == a);
        }
        return p;
    }
}

internal static class ExpressionExt
{
    public static Expression And(this Expression left, Expression right) =>
        Expression.AndAlso(left, right);
}
