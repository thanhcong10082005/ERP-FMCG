# ERP-FMCG Database — MS SQL Server 2022

Gói cài đặt database cho hệ thống ERP-FMCG, target **MS SQL Server 2022** (Express),  
schema `tenant`, collation `Vietnamese_CI_AS`.

---

## 1. Cấu trúc thư mục

```
Pack/erp-fmcg-database/
├── README.md                       # File này
├── 01_schema.sql                   # ★ DDL tổng hợp (UTF-8 KHÔNG BOM)
├── 02_verify.sql                   # ★ Báo cáo kiểm tra dữ liệu (read-only)
├── requirements.txt                # Dependency Python
├── database/
│   ├── 01_schema/                  # ★ File DDL nguồn (00..07)
│   └── 02_migration/               # ★ Migration (07..10)
├── scripts/
│   ├── 01_full_import_pipeline.py  # ★ Pipeline nhập dữ liệu duy nhất
│   ├── 02_verify_data.py           # Đối soát + xuất báo cáo (CSV/JSON)
│   ├── 03_clean_db.ps1             # Reset dữ liệu nghiệp vụ (giữ master data)
│   ├── 03_import_partner_debt.py   # Nhập công nợ khách hàng
│   ├── 04_verify_pod_idempotency.py# Kiểm tra idempotency POD
│   ├── 05_apply_migration.py       # Áp dụng migration dispatcher/EOD
│   ├── 06_seed_opening_stock.py    # Tạo opening balance stock ledger
│   ├── 07_unified_verify.py        # ★ Verify thống nhất 54 check
│   ├── 08_data_regression.py       # ★ Hash regression dữ liệu
│   ├── 09_backup_db.ps1            # ★ Backup tự động + verify
│   ├── verify_registry.py          # ★ Nguồn sự thật 54 check
│   ├── build_schema.py             # Sinh lại 01_schema.sql từ file nguồn
│   └── build_verify_sql.py         # Sinh lại 02_verify.sql từ registry
└── migration_data/
    └── README.md                   # Mô tả 11 file CSV + quy tắc làm sạch
```

> **Lưu ý:** Thư mục `migration_data/` **không** commit CSV. Xem `migration_data/README.md` để biết cách tải/chuẩn bị dữ liệu.

---

## 2. Yêu cầu hệ thống


| Thành phần  | Phiên bản            | Ghi chú                               |
| ----------- | -------------------- | ------------------------------------- |
| SQL Server  | 2022 Express trở lên | Instance mặc định `.\SQLEXPRESS`      |
| .NET SDK    | 8.0                  | Cho backend `erp-fmcg-backend-csharp` |
| Python      | 3.10+                | Pipeline nhập dữ liệu                 |
| ODBC Driver | 17/18 for SQL Server | Bắt buộc cho `pyodbc`                 |


### Cài đặt dependency Python

```powershell
python -m pip install -r requirements.txt
```

---

## 3. Quy trình cài đặt (3 bước)

### Bước 1 — Tạo database + schema

```powershell
sqlcmd -S ".\SQLEXPRESS" -E -Q "CREATE DATABASE [ERP_FMCG] COLLATE Vietnamese_CI_AS"
sqlcmd -S ".\SQLEXPRESS" -d "ERP_FMCG" -E -f 65001 -i 01_schema.sql
```

> ⚠️ `-f 65001` **là BẮT BUỘC.**
> `01_schema.sql` lưu UTF-8 **không BOM**. `sqlcmd` mặc định đọc file theo ANSI
> (cp1252) → toàn bộ chuỗi tiếng Việt trong seed sẽ thành mojibake
> (`Cà phê` → `CÃ phÃª`). `-f 65001` buộc `sqlcmd` đọc UTF-8.

Tạo lại từ đầu (xoá sạch DB):

```powershell
sqlcmd -S ".\SQLEXPRESS" -E -Q "ALTER DATABASE [ERP_FMCG] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [ERP_FMCG];"
sqlcmd -S ".\SQLEXPRESS" -E -Q "CREATE DATABASE [ERP_FMCG] COLLATE Vietnamese_CI_AS"
```

### Bước 2 — Chuẩn bị dữ liệu nguồn

Đặt 11 file CSV vào `migration_data/` (xem `migration_data/README.md`).

### Bước 3 — Nhập dữ liệu

```powershell
python scripts\01_full_import_pipeline.py `
    --server ".\SQLEXPRESS" `
    --database "ERP_FMCG" `
    --data-dir ".\migration_data"
```

Pipeline tự động:

1. Đọc + chuẩn hoá 7 file RDT (NFC, dấu thập phân `,`, ngày `M/D/YYYY`)
2. Trích xuất master data (categories, products, routes, warehouses, users, partners)
3. Tính lại toàn bộ số tiền từ cấp dòng (không tin giá trị sẵn có)
4. Xoá dữ liệu nghiệp vụ cũ (idempotent) rồi nạp lại
5. Nạp dữ liệu seed (11 đơn `SO-T001-*` + 14 dòng)
6. Sinh bảng giá (583 bản ghi theo SKU × segment)
7. Ghi `import_jobs` / `import_errors`
8. Chạy 26 kiểm tra tự động (A1–A26)

### Kiểm tra kết quả

```powershell
sqlcmd -S ".\SQLEXPRESS" -d "ERP_FMCG" -E -f 65001 -i 02_verify.sql
```

hoặc bằng Python (xuất CSV/JSON):

```powershell
python scripts\02_verify_data.py --server ".\SQLEXPRESS" --database "ERP_FMCG" --output reports/
```

---

## 4. Chạy thử nhanh (dry-run)

```powershell
# Chỉ đọc + tính toán, KHÔNG ghi DB
python scripts\01_full_import_pipeline.py --dry-run --data-dir ".\migration_data"

# Chỉ 1 file RDT
python scripts\01_full_import_pipeline.py --dry-run --files RDT1.26.csv
```

Dry-run mất **~40 giây** cho 177.631 dòng và cho kết quả **giống hệt** import thật
(cùng số đơn, số dòng, cùng phân bố lỗi) — chỉ khác ở các check cần đọc `sys.tables` (A13–A19).

---

## 5. Tuỳ chọn CLI của pipeline


| Tham số      | Mặc định                     | Ý nghĩa                                      |
| ------------ | ---------------------------- | -------------------------------------------- |
| `--server`   | `.\SQLEXPRESS`               | SQL Server instance                          |
| `--database` | `ERP_FMCG`                   | Tên database                                 |
| `--data-dir` | `migration_data` cạnh script | Thư mục chứa CSV                             |
| `--files`    | cả 7 file RDT                | Danh sách file cần nạp                       |
| `--dry-run`  | off                          | Chỉ đọc, không ghi DB                        |
| `--no-seed`  | off                          | Bỏ 11 đơn seed                               |
| `--no-clean` | off                          | Không xoá dữ liệu nghiệp vụ cũ trước khi nạp |
| `--log-file` | none                         | Ghi log ra file                              |


---

## 6. Cấu trúc schema `tenant`

### Master data (Temporal)


| Bảng            | Ghi chú                              |
| --------------- | ------------------------------------ |
| `categories`    | 9 nhóm từ RDT (Cà phê, Nước, Trà, …) |
| `products`      | 141 SKU — **Temporal**               |
| `routes`        | 16 tuyến giao nhân viên bán hàng     |
| `warehouses`    | 1 kho (`KHO001`)                     |
| `user_accounts` | 55 tài khoản — **Temporal**          |
| `partners`      | 2.723 khách hàng — **Temporal**      |


### Nghiệp vụ


| Bảng                 | Số dòng | Ghi chú                                 |
| -------------------- | ------- | --------------------------------------- |
| `sales_orders`       | 49.844  | **Temporal**                            |
| `sales_order_items`  | 176.332 | Chi tiết dòng (FK → orders CASCADE)     |
| `price_lists`        | 583     | **Temporal** — giá theo SKU × segment   |
| `uom_conversions`    | 124     | Quy đổi đơn vị tính                     |
| `stock_ledger`       | 0       | Chờ nguồn dữ liệu tồn kho            |
| `inventory_balances` | 0       | Chờ nguồn dữ liệu tồn kho            |


### Staging / Import / Audit


| Bảng                    | Mục đích                                                          |
| ----------------------- | ----------------------------------------------------------------- |
| `import_jobs`           | Lịch sử mỗi lần import (job_code, file hash, số dòng, trạng thái) |
| `import_staging`        | Vùng đệm dữ liệu thô chờ validate                                 |
| `import_errors`         | Chi tiết lỗi từng dòng (có `suggested_fix`)                       |
| `import_field_mappings` | Ánh xạ cột nguồn → cột đích                                       |
| `audit_logs`            | Nhật ký thay đổi nghiệp vụ                                        |


---

---

## 7. Migration, Verify & Backup

### 7.1. Bộ migration

| File                                        | Nội dung                                                     |
| ------------------------------------------- | ------------------------------------------------------------ |
| `database/02_migration/07_migration.sql` | 4 bảng delivery, `client_pod_id` UNIQUE, GPS, công nợ     |
| `database/02_migration/08_user_phone.sql` | Thêm `phone` cho `user_accounts`                        |
| `database/02_migration/09_dispatcher_eod.sql` | Bảng dispatcher, EOD reconciliation, index báo cáo |
| `database/02_migration/10_stock_trigger_fix.sql` | Sửa trigger stock ledger + CHECK `average_cost >= 0` |

Áp dụng lên DB đang chạy:

```powershell
python scripts\05_apply_migration.py --server ".\SQLEXPRESS" --database "ERP_FMCG"
```

Tạo opening balance tồn kho (nếu tồn kho âm do lịch sử import):

```powershell
python scripts\06_seed_opening_stock.py
```

### 7.2. Verify thống nhất (54 check)

`verify_registry.py` là **nguồn sự thật** duy nhất. Từ đó sinh ra:

- `07_unified_verify.py` — runner Python, in PASS/FAIL từng check
- `02_verify.sql` — bản SQL cho `sqlcmd` (sinh bằng `build_verify_sql.py`)

```powershell
python scripts\07_unified_verify.py
python scripts\build_verify_sql.py      # sinh lại 02_verify.sql
```

Nhóm check: **IMPORT** (import), **DELIVERY** (schema nghiệp vụ giao hàng), **OPS** (anti-fraud / stock / EOD / hardening).

### 7.3. Regression dữ liệu (hash)

```powershell
python scripts\08_data_regression.py            # tạo baseline
python scripts\08_data_regression.py --check    # so với baseline
```

Tính SHA-256 theo chunk cho `sales_orders`, `sales_order_items`, `partners`,
`products`, `stock_ledger`, `inventory_balances`, `delivery_pods` — phát hiện hồi quy
khi ai đó sửa dữ liệu ngoài ý muốn.

### 7.4. Backup tự động

```powershell
powershell scripts\09_backup_db.ps1
```

- Tự phát hiện SQL Server Express (bỏ `WITH COMPRESS` vì Express không hỗ trợ)
- `RESTORE HEADERONLY` để xác nhận file backup đọc được
- Xoay vòng theo `-RetentionDays` (mặc định 14 ngày)
- Trả exit code khác 0 nếu backup lỗi (dùng cho CI/cron)
