# ERP FMCG — Backend .NET 8 · Database SQL Server · Driver App PWA

Bộ 3 sản phẩm bàn giao cho hệ thống ERP FMCG: Web API C#, database MS SQL Server 2022 và ứng dụng giao hàng cho tài xế.

---

## 1. Cấu trúc


| Thư mục                    | Nội dung                                                      | Tài liệu                                    |
| -------------------------- | ------------------------------------------------------------- | ------------------------------------------- |
| `erp-fmcg-database/`       | DDL (`01_schema.sql`), script đối soát, pipeline nhập dữ liệu | [README](erp-fmcg-database/README.md)       |
| `erp-fmcg-backend-csharp/` | ASP.NET Core 8 Web API, 4 lớp, EF Core                        | [README](erp-fmcg-backend-csharp/README.md) |
| `erp-fmcg-driver/`         | PWA giao hàng — React + TypeScript + Vite                     | [README](erp-fmcg-driver/README.md)         |


---

## 2. Yêu cầu môi trường


| Thành phần                 | Phiên bản     | Dùng cho                         |
| -------------------------- | ------------- | -------------------------------- |
| .NET SDK                   | 8.0+          | Backend                          |
| SQL Server                 | 2022 Express+ | Database                         |
| Node.js                    | 18+           | Driver App                       |
| Python                     | 3.10+         | Pipeline nhập dữ liệu (tuỳ chọn) |
| ODBC Driver for SQL Server | 17 hoặc 18    | Pipeline Python                  |


Script `.ps1` cần Windows. Riêng pipeline Python (`scripts/*.py`) chạy được trên
mọi hệ điều hành.

---

## 3. Quick start

### Bước 1 — Clone

```powershell
git clone https://github.com/thanhcong10082005/ERP-FMCG.git
cd ERP-FMCG
```

### Bước 2 — Dựng database

```powershell
cd erp-fmcg-database
sqlcmd -S ".\SQLEXPRESS" -E -f 65001 -i 01_schema.sql
```

Lệnh trên tạo database `ERP_FMCG`, schema `tenant`, 17 bảng nghiệp vụ + 5 bảng
history (temporal) và seed dữ liệu danh mục nền:


| Bảng            | Số dòng seed |
| --------------- | ------------ |
| `categories`    | 9            |
| `routes`        | 16           |
| `warehouses`    | 1            |
| `user_accounts` | 7            |


Kiểm tra:

```powershell
sqlcmd -S ".\SQLEXPRESS" -d ERP_FMCG -E -f 65001 -i 02_verify.sql
```

> ⚠️ `-f 65001` là bắt buộc để `sqlcmd` đọc đúng UTF-8. Thiếu tham số này, toàn bộ
> tiếng Việt trong seed sẽ bị lỗi font (`Cà phê` → `CÃ phÃª`).
>
> **Chưa có sản phẩm/khách hàng/đơn hàng sau bước này.** `products`,
> `partners`, `sales_orders`, `price_lists` đều rỗng — muốn có dữ liệu thật
> (141 SKU, 2.723 khách, 49.844 đơn) phải chạy pipeline nhập liệu, xem
> [erp-fmcg-database/README.md](erp-fmcg-database/README.md).

### Bước 3 — Chạy backend

```powershell
cd ..\erp-fmcg-backend-csharp
dotnet restore
dotnet run --project src/ERP.FMC.Api
```

Mở [http://localhost:5000/swagger](http://localhost:5000/swagger) để xem danh sách API.
Kiểm tra kết nối DB: [http://localhost:5000/api/health](http://localhost:5000/api/health)

### Bước 4 — Chạy Driver App

```powershell
cd ..\erp-fmcg-driver
npm install
copy .env.example .env
npm run dev
```

Mở [http://localhost:5173](http://localhost:5173).

> ✅ Driver App gọi nhóm API `/api/auth/dev-login`, `/api/sales/trips`,
> `/api/sales/pod` — các route này **đã có** trong backend C#.
> Driver App dùng chung một backend, không còn phụ thuộc backend NestJS cũ.

---

## 4. Công nghệ


| Lớp        | Công nghệ                                                                                           |
| ---------- | --------------------------------------------------------------------------------------------------- |
| Backend    | ASP.NET Core 8, Entity Framework Core 8, AutoMapper, FluentValidation, Swashbuckle                  |
| Database   | MS SQL Server 2022, schema `tenant`, collation `Vietnamese_CI_AS`, system-versioned temporal tables |
| Driver App | React 18, TypeScript 5, Vite 5, Tailwind CSS 3, Zustand, Dexie, Axios                               |
| PWA       | vite-plugin-pwa (Workbox) — offline, cài đặt được, cache master data                                   |
| Nhập liệu  | Python 3 + pandas + pyodbc                                                                          |


---

## 5. Dữ liệu lớn (Google Drive)

Repo **không chứa** CSV nguồn. Tải tại:

👉 [erp-fmcg-database/migration_data/README.md](erp-fmcg-database/migration_data/README.md)

File trên Drive phục vụ mục đích nạp vào SQL Server qua pipeline Python.

---

## 6. Dữ liệu mẫu có sẵn

`01_schema.sql` chỉ seed dữ liệu danh mục nền — chạy xong app chạy được nhưng
danh mục sản phẩm và khách hàng còn trống:


| Bảng            | Số dòng sau `01_schema.sql` |
| --------------- | --------------------------- |
| `categories`    | 9                           |
| `routes`        | 16                          |
| `warehouses`    | 1                           |
| `user_accounts` | 7                           |
| `products`      | 0                           |
| `partners`      | 0                           |
| `price_lists`   | 0                           |
| `sales_orders`  | 0                           |


Bảng nghiệp vụ để trống chờ dữ liệu thật. Để có đủ 141 SKU, 2.723 khách hàng,
49.844 đơn và 176.332 dòng chi tiết, tải CSV từ Drive rồi chạy pipeline:

```powershell
cd erp-fmcg-database
python scripts\01_full_import_pipeline.py --data-dir ".\migration_data"
```

Xem [erp-fmcg-database/README.md](erp-fmcg-database/README.md) để biết chi tiết.

---

---

## 7. Anti-fraud, Vận hành & Hardening

Bổ sung 3 nhóm năng lực, tất cả **nằm trong `Pack/`**:

### 7.1. Chống gian lận & Idempotency

| Hạng mục                | Thành phần                                                                                |
| ----------------------- | ----------------------------------------------------------------------------------------- |
| Time-lock 05:00–22:00  | `AntiFraudService` — giờ VN (`Asia/Ho_Chi_Minh`)                                          |
| GPS Gate ≤ 100 m       | Haversine, bỏ qua khách chưa geocode                                                      |
| No Backdate             | Cảnh báo lệch timestamp > 15 phút                                                          |
| Idempotency POD        | `client_pod_id` UNIQUE + transaction atomic, bắt `DbUpdateException` / SQL 2601 / 2627      |
| Công nợ xác minh       | `old_debt - sum(old_collected)`, `current_debt + new_credit - new_paid`                      |
| Tồn kho                | Stock ledger opening balance + trigger đồng bộ `tenant.inventory_balances` + sinh ledger `SALE` |
| Audit log              | `AuditLogMiddleware` ghi `record_id` từ body, user id từ JWT claim                          |

### 7.2. Vận hành

| Hạng mục         | Thành phần                                                                  |
| ---------------- | --------------------------------------------------------------------------- |
| Dispatcher       | `DispatcherService` — tạo chuyến, gán đơn, bản đồ audit                   |
| Báo cáo          | `ReportService` — 6 báo cáo: doanh thu, tỉ lệ giao, aging nợ, hàng trả về, tiền mặt theo ca, tồn kho thấp |
| Đối soát EOD    | `EodReconciliationService` + 2 bảng `tenant.eod_reconciliation*`             |
| Xuất Excel       | `OperationsExportService` (ClosedXML) — 8 loại báo cáo `.xlsx`                   |

### 7.3. Hardening & DevOps

| Hạng mục         | Thành phần                                                                |
| ---------------- | --------------------------------------------------------------------------- |
| CI/CD            | `.github/workflows/ci.yml` — DB schema + .NET 8 build/test + PWA build        |
| Verify thống nhất| `scripts/verify_registry.py` + `07_unified_verify.py` (54 check)              |
| Regression data  | `scripts/08_data_regression.py` — hash SHA-256 chặn hồi quy                   |
| Rate limiting    | 30 req/phút/tài xế cho `POST /api/sales/pod`                               |
| Security         | CORS siết, không commit secret                                             |
| Dọn lịch sử      | `dbo.sp_purge_sales_orders_history` (temporal purge)                        |
| PWA              | `vite-plugin-pwa`, service worker, `PwaStatusBanner`                        |
| Health check sâu | `GET /api/health/deep`                                                     |
| Backup tự động   | `scripts/09_backup_db.ps1` (Express-safe, `RESTORE HEADERONLY` verify, xoay vòng) |

### 7.4. Chạy kiểm tra

```powershell
# Verify thống nhất (54 check)
python erp-fmcg-database\scripts\07_unified_verify.py

# Regression hash dữ liệu
python erp-fmcg-database\scripts\08_data_regression.py --check

# Unit test backend
cd erp-fmcg-backend-csharp
dotnet test tests/ERP.FMC.Tests
```

Xem báo cáo đầy đủ: [`erp-fmcg-database/README.md`](erp-fmcg-database/README.md)
