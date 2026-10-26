namespace ERP.FMC.Application.DTOs;

/// <summary>
/// Paged result chung cho tất cả list endpoints.
/// </summary>
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int TotalCount { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}
