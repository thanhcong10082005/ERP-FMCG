<#
.SYNOPSIS
    Xoa du lieu nghiep vu cua ERP_FMCG de chay lai pipeline tu dau.

.DESCRIPTION
    Script nay KHONG xoa database. No chi xoa du lieu nghiep vu
    (don hang, dong hang, bang gia, uom_conversions, ton kho, lich su import)
    va GIU LAI master data seed (categories, routes, warehouses, users, partners).

    Pipeline (01_full_import_pipeline.py) tu dong lam buoc nay khi chay
    (tru khi truyen co --no-clean), nen thuong khong can chay script nay.

.PARAMETER Server
    SQL Server instance. Mac dinh: .\SQLEXPRESS

.PARAMETER Database
    Ten database. Mac dinh: ERP_FMCG

.PARAMETER Full
    Neu chi dinh: xoa TOAN BO database va tao lai tu dau (can chay lai 01_schema.sql).

.EXAMPLE
    .\scripts\03_clean_db.ps1
    .\scripts\03_clean_db.ps1 -Full
#>
[CmdletBinding()]
param(
    [string]$Server   = ".\SQLEXPRESS",
    [string]$Database = "ERP_FMCG",
    [switch]$Full
)

$ErrorActionPreference = "Stop"

function Invoke-Sql {
    param(
        [Parameter(Mandatory = $true)][string]$Query,
        [string]$DatabaseName = "master"
    )
    Write-Host "  > $Query" -ForegroundColor DarkGray
    # Bat buoc SET QUOTED_IDENTIFIER ON: co bang voi filtered index
    # (inventory_balances.idx_ib_*) se bao loi 1934 neu thieu SET nay.
    sqlcmd -S $Server -d $DatabaseName -E -b -Q "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; $Query"
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd that bai (exit $LASTEXITCODE)"
    }
}

if ($Full) {
    Write-Host "=== XOA TOAN BO DATABASE '$Database' va tao lai ===" -ForegroundColor Yellow
    Invoke-Sql -Query "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database];"
    Invoke-Sql -Query "CREATE DATABASE [$Database] COLLATE Vietnamese_CI_AS"
    Write-Host "  Database da duoc tao lai. Hay chay tiep:" -ForegroundColor Yellow
    Write-Host "    sqlcmd -S `"$Server`" -d `"$Database`" -E -f 65001 -i 01_schema.sql" -ForegroundColor Yellow
    exit 0
}

Write-Host "=== Xoa du lieu nghiep vu cu trong '$Database' ===" -ForegroundColor Cyan

# Thu tu xoa theo phu thuoc FK:
#   sales_order_items -> sales_orders (CASCADE) -> price_lists / stock_ledger
Invoke-Sql -Query "DELETE FROM [tenant].[sales_order_items];" -DatabaseName $Database
Invoke-Sql -Query "DELETE FROM [tenant].[sales_orders];"      -DatabaseName $Database
Invoke-Sql -Query "DELETE FROM [tenant].[price_lists];"       -DatabaseName $Database
Invoke-Sql -Query "DELETE FROM [tenant].[uom_conversions];"   -DatabaseName $Database
Invoke-Sql -Query "DELETE FROM [tenant].[stock_ledger];"      -DatabaseName $Database
Invoke-Sql -Query "DELETE FROM [tenant].[inventory_balances];" -DatabaseName $Database
Invoke-Sql -Query "DELETE FROM [tenant].[import_errors];"     -DatabaseName $Database
Invoke-Sql -Query "DELETE FROM [tenant].[import_jobs];"       -DatabaseName $Database

Write-Host ""
Write-Host "=== KIEM TRA ===" -ForegroundColor Cyan
Invoke-Sql -Query @"
SET NOCOUNT ON;
SELECT 'categories'       AS bang, COUNT(*) AS con_lai FROM [tenant].[categories]
UNION ALL SELECT 'products',          COUNT(*) FROM [tenant].[products]
UNION ALL SELECT 'routes',            COUNT(*) FROM [tenant].[routes]
UNION ALL SELECT 'warehouses',        COUNT(*) FROM [tenant].[warehouses]
UNION ALL SELECT 'user_accounts',     COUNT(*) FROM [tenant].[user_accounts]
UNION ALL SELECT 'partners',          COUNT(*) FROM [tenant].[partners]
UNION ALL SELECT 'sales_orders',      COUNT(*) FROM [tenant].[sales_orders]
UNION ALL SELECT 'sales_order_items', COUNT(*) FROM [tenant].[sales_order_items]
UNION ALL SELECT 'price_lists',       COUNT(*) FROM [tenant].[price_lists];
"@ -DatabaseName $Database

Write-Host ""
Write-Host "=== HOAN TAT. Chay tiep pipeline: ===" -ForegroundColor Green
Write-Host "  python scripts\01_full_import_pipeline.py --server `"$Server`" --database `"$Database`""
exit 0
