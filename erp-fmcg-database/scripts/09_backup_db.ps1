<#
.SYNOPSIS
    ★ PHASE 6C.9 — Sao luu tu dong ERP_FMCG (BACKUP DATABASE ... WITH COMPRESS, COPY_ONLY).

.DESCRIPTION
    Script "03_clean_db.ps1" hien tai chi lam chuc nang CLEAN (xoa du lieu).
    Phase 6C.9 yeu cau them script BACKUP + lich tu dong.

    Tinh nang:
      1. Sao luu FULL database ERP_FMCG ra file .bak (COMPRESS + COPY_ONLY).
         - COPY_ONLY: khong duoc ghi vao differential backup chain -> an toan
           khi co job restore tang hop khac.
         - COMPRESS: SQL Server 2019+ ho tro, tiet kiem ~40-60% dung luong.
      2. Ghi log ro rang: ket qua, thoi luong, kich thuoc, duong dan.
      3. Xoa cac file backup cu hon $KeepDays ngay (mac dinh 14 ngay).
      4. Sinh file .ps1 de Task Scheduler Windows goi hang ngay.
      5. Verify backup: kiem tra file .bak > 0 byte + doc RESTORE HEADER.

    Lich chay (Task Scheduler):
      Tao task chay moi ngay luc 02:00:
        powershell -ExecutionPolicy Bypass -File <duong dan>\09_backup_db.ps1

    Khoi phuc:
        sqlcmd -S ".\SQLEXPRESS" -E -Q "RESTORE DATABASE ERP_FMCG FROM DISK = 'D:\...bak' WITH MOVE ..."
      (xem file README hoac runbook trong PHASE_6_REPORT.md)

.PARAMETER Server
    SQL Server instance. Mac dinh: .\SQLEXPRESS

.PARAMETER Database
    Ten database can sao luu. Mac dinh: ERP_FMCG

.PARAMETER BackupPath
    Thu muc luu file .bak. Mac dinh: <pack root>\..\Backup\ERP_FMCG

.PARAMETER KeepDays
    So ngay giu lai file backup cu. Mac dinh: 14

.PARAMETER InstallScheduledTask
    Tao (hoac cap nhat) Task Scheduler chay moi ngay luc 02:00.

.PARAMETER Verify
    Chi verify cac file .bak hien co (doc RESTORE HEADER) ma khong tao backup moi.

.EXAMPLE
    .\scripts\09_backup_db.ps1
    .\scripts\09_backup_db.ps1 -BackupPath "D:\Backups\ERP_FMCG" -KeepDays 30
    .\scripts\09_backup_db.ps1 -InstallScheduledTask
    .\scripts\09_backup_db.ps1 -Verify
#>
[CmdletBinding()]
param(
    [string]$Server   = ".\SQLEXPRESS",
    [string]$Database = "ERP_FMCG",
    [string]$BackupPath = "",
    [int]$KeepDays   = 14,
    [switch]$InstallScheduledTask,
    [switch]$Verify
)

$ErrorActionPreference = "Stop"

# --- Thuong muc mac dinh: <pack root>\Backup\ERP_FMCG ---
if ([string]::IsNullOrWhiteSpace($BackupPath)) {
    # <workspace>\Backup\ERP_FMCG  (KHONG dat trong Pack/ de anh bao cao
    # du lieu khong bi lan vao git repo)
    $packRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)  # .../Pack
    $BackupPath = Join-Path (Split-Path -Parent $packRoot) "Backup\ERP_FMCG"
}

if (-not (Test-Path $BackupPath)) {
    New-Item -ItemType Directory -Path $BackupPath -Force | Out-Null
    Write-Host "Da tao thu muc backup: $BackupPath" -ForegroundColor DarkGray
}

function Invoke-Sql {
    param(
        [Parameter(Mandatory = $true)][string]$Query,
        [string]$DatabaseName = "master"
    )
    $out = sqlcmd -S $Server -d $DatabaseName -E -b -h -1 -W -Q $Query
    if ($LASTEXITCODE -ne 0) {
        throw "sqlcmd that bai (exit $LASTEXITCODE): $Query"
    }
    return $out
}

# ===================================================================
# VERIFY: doc RESTORE HEADER cua cac file .bak
# ===================================================================
function Test-BackupFiles {
    param([string]$Path)

    $files = Get-ChildItem -Path $Path -Filter "*.bak" -ErrorAction SilentlyContinue |
             Sort-Object LastWriteTime -Descending

    if (-not $files) {
        Write-Host "  Khong tim thay file .bak nao trong $Path" -ForegroundColor Yellow
        return $false
    }

    $allOk = $true
    foreach ($f in $files) {
        $sizeMB = [math]::Round($f.Length / 1MB, 2)

        # Doc RESTORE HEADERONLY. `-h -1 -W` bo ten cot -> phai kiem tra
        # bang noi dung chuoi (khong phai ten cot "DatabaseName" nhu ban dau).
        $header = $null
        $exit = 1
        try {
            $header = sqlcmd -S $Server -d $Database -E -b -h -1 -W `
                -Q "RESTORE HEADERONLY FROM DISK = N'$($f.FullName.Replace("'", "''"))';" 2>&1
            $exit = $LASTEXITCODE
        } catch {
            $header = $null
        }

        $txt = ($header | Out-String)
        # File backup hop le khi: sqlcmd exit 0 VA header co ten database
        $ok = ($exit -eq 0) -and ($txt -match [regex]::Escape($Database)) -and
               ($txt -match "^\s*\S+") -and ($f.Length -gt 0)

        if (-not $ok) { $allOk = $false }
        Write-Host ("  [{0}] {1,-45} {2,10} MB  {3}" -f `
            $(if ($ok) { "PASS" } else { "FAIL" }),
            $f.Name, $sizeMB, $f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"))
    }
    return $allOk
}

if ($Verify) {
    Write-Host "=== VERIFY BACKUP '$Database' ===" -ForegroundColor Cyan
    $ok = Test-BackupFiles -Path $BackupPath
    Write-Host ""
    if ($ok) { Write-Host "TAT CA FILE BACKUP HOP LE." -ForegroundColor Green }
    else     { Write-Host "CO FILE BACKUP KHONG DOC DUOC." -ForegroundColor Red }
    exit $(if ($ok) { 0 } else { 1 })
}

# ===================================================================
# BACKUP
# ===================================================================
Write-Host "=== BACKUP '$Database' ($Server) ===" -ForegroundColor Cyan

$stamp   = Get-Date -Format "yyyyMMdd_HHmmss"
$fileName = "${Database}_FULL_${stamp}.bak"
$bakFile = Join-Path $BackupPath $fileName

$sw = [System.Diagnostics.Stopwatch]::StartNew()

# --- Phat hien edition: COMPRESS chi co tu SQL Server 2019 va KHONG co tren
#     Express. Khi khong ho tro -> bo qua (backup van dung).
# ---
# ROOT CAUSE (sau khi gap loi thuc te):
#   Script backup v1 luon them `WITH COMPRESS`. Khi chay tren SQL Server
#   EXPRESS (ban deploy thuc te) -> Msg 155: "'COMPRESS' is not a recognized
#   BACKUP option" -> backup that bai, KHONG co file .bak nao duoc tao.
#   => Phai phat hien edition truoc khi them option.
$edition = (Invoke-Sql -Query "SELECT SERVERPROPERTY('Edition') AS e;" -DatabaseName "master") -join ""
$supportsCompress = ($edition -notmatch "Express") -and
                    ((Invoke-Sql -Query "SELECT CAST(SERVERPROPERTY('ProductVersion') AS VARCHAR(50)) AS v;" -DatabaseName "master") -join "") -match "^(1[5-9]|[2-9][0-9])"

$withOptions = @("COPY_ONLY", "INIT", "STATS = 5", "CHECKSUM")
if ($supportsCompress) {
    $withOptions = @("COMPRESS") + $withOptions
    Write-Host "  Edition: $edition -> ho tro COMPRESS." -ForegroundColor DarkGray
} else {
    Write-Host "  Edition: $edition -> KHONG ho tro COMPRESS (Express/<=2016). Bo qua option nay." -ForegroundColor Yellow
}

# WITH COMPRESS   : nen (SQL 2019+, khong co tren Express)
# WITH COPY_ONLY  : khong dung differential backup chain
# WITH INIT       : ghi du lai toan bo, khong noi chuoi
# WITH CHECKSUM   : phat hien corruption ngay khi backup
$sql = @"
BACKUP DATABASE [$Database]
TO DISK = N'$($bakFile.Replace("'", "''"))'
WITH $($withOptions -join ', ');
"@

Write-Host "  > $sql" -ForegroundColor DarkGray
sqlcmd -S $Server -d master -E -b -Q $sql
if ($LASTEXITCODE -ne 0) {
    Write-Host "BACKUP THAT BAI." -ForegroundColor Red
    exit 1
}

$sw.Stop()

if (-not (Test-Path $bakFile)) {
    Write-Host "BACKUP THAT BAI: khong tim thay file $bakFile" -ForegroundColor Red
    exit 1
}

$sizeMB = [math]::Round((Get-Item $bakFile).Length / 1MB, 2)
Write-Host ""
Write-Host "  BACKUP THANH CONG" -ForegroundColor Green
Write-Host "    File      : $bakFile"
Write-Host "    Kich thuoc: $sizeMB MB"
Write-Host "    Thoi luong: $($sw.Elapsed.TotalSeconds.ToString('0.0')) giay"

# --- Verify ngay file vua tao ---
Write-Host ""
Write-Host "=== VERIFY FILE BACKUP ===" -ForegroundColor Cyan
$verifyOk = Test-BackupFiles -Path $BackupPath

# --- Don dep file cu ---
Write-Host ""
Write-Host "=== DON DEP FILE BACKUP CU (> $KeepDays ngay) ===" -ForegroundColor Cyan
$cutoff = (Get-Date).AddDays(-$KeepDays)
$old = Get-ChildItem -Path $BackupPath -Filter "*.bak" -ErrorAction SilentlyContinue |
       Where-Object { $_.LastWriteTime -lt $cutoff }
if ($old) {
    foreach ($f in $old) {
        Write-Host "  Xoa: $($f.Name) ($([math]::Round($f.Length/1MB,2)) MB)" -ForegroundColor DarkGray
        Remove-Item $f.FullName -Force
    }
} else {
    Write-Host "  Khong co file nao can xoa."
}

# --- Cai dat Task Scheduler ---
if ($InstallScheduledTask) {
    Write-Host ""
    Write-Host "=== CAI DAT TASK SCHEDULER (02:00 moi ngay) ===" -ForegroundColor Cyan

    $taskName = "ERP_FMCG_Backup_Daily"
    $scriptPath = Join-Path $PSScriptRoot "09_backup_db.ps1"
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Server `"$Server`" -Database `"$Database`" -BackupPath `"$BackupPath`" -KeepDays $KeepDays"

    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $argList
    $trigger = New-ScheduledTaskTrigger -Daily -At "02:00"
    $settings = New-ScheduledTaskSettingsSet `
        -StartWhenAvailable `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
        -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 15)

    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Register-ScheduledTask -TaskName $taskName `
        -Action $action -Trigger $trigger -Settings $settings `
        -Description "ERP_FMCG daily full backup (COMPRESS, COPY_ONLY)" `
        -RunLevel Highest | Out-Null

    Write-Host "  Da tao task '$taskName' chay moi ngay luc 02:00." -ForegroundColor Green
    Write-Host "  Kiem tra: Get-ScheduledTask -TaskName '$taskName'"
}

Write-Host ""
if ($verifyOk) {
    Write-Host "HOAN TAT: backup + verify thanh cong." -ForegroundColor Green
    exit 0
} else {
    Write-Host "HOAN TAT: backup tao duoc nhung VERIFY THAT BAI." -ForegroundColor Red
    exit 1
}
