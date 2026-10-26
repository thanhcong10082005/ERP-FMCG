using System.Data;
using System.Reflection;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ERP.FMC.Infrastructure.Services;

/// <summary>
/// Bulk operations sử dụng SqlBulkCopy + Dapper.
/// Target: Import 180k rows < 1 giây.
/// </summary>
public class BulkOperationService
{
    private readonly string _connectionString;
    private readonly ILogger<BulkOperationService> _logger;

    public BulkOperationService(IConfiguration config, ILogger<BulkOperationService> logger)
    {
        _connectionString = config.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentNullException("DefaultConnection");
        _logger = logger;
    }

    /// <summary>
    /// Bulk insert bằng SqlBulkCopy với DataTable conversion.
    /// </summary>
    public async Task<int> BulkInsertAsync<T>(string schema, string tableName, IEnumerable<T> records, int batchSize = 50_000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var dataTable = ToDataTable(records);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var bulkCopy = new SqlBulkCopy(connection)
        {
            DestinationTableName = $"[{schema}].[{tableName}]",
            BatchSize = batchSize,
            BulkCopyTimeout = 300
        };

        // Map columns
        foreach (var col in dataTable.Columns.Cast<DataColumn>())
        {
            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        }

        await bulkCopy.WriteToServerAsync(dataTable);
        sw.Stop();

        _logger.LogInformation("BulkInsert {Schema}.{Table}: {Count} rows in {Ms}ms ({Rate:F0} rows/s)",
            schema, tableName, dataTable.Rows.Count, sw.ElapsedMilliseconds,
            dataTable.Rows.Count * 1000.0 / sw.ElapsedMilliseconds);

        return dataTable.Rows.Count;
    }

    /// <summary>
    /// Bulk update bằng Dapper (batch).
    /// </summary>
    public async Task<int> BulkUpdateAsync<T>(string sql, IEnumerable<T> records, int batchSize = 1000)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        int total = 0;
        var batch = new List<T>(batchSize);
        foreach (var rec in records)
        {
            batch.Add(rec);
            if (batch.Count >= batchSize)
            {
                total += await connection.ExecuteAsync(sql, batch);
                batch.Clear();
            }
        }
        if (batch.Count > 0) total += await connection.ExecuteAsync(sql, batch);
        return total;
    }

    /// <summary>
    /// Convert List<T> to DataTable using reflection.
    /// </summary>
    private static DataTable ToDataTable<T>(IEnumerable<T> items)
    {
        var table = new DataTable();
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && !p.PropertyType.IsClass || p.PropertyType == typeof(string))
            .ToArray();

        foreach (var prop in props)
        {
            var type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            table.Columns.Add(prop.Name, type);
        }

        foreach (var item in items)
        {
            var row = table.NewRow();
            foreach (var prop in props)
            {
                var val = prop.GetValue(item);
                row[prop.Name] = val ?? DBNull.Value;
            }
            table.Rows.Add(row);
        }

        return table;
    }
}
