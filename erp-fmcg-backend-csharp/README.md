# ERP FMCG — Backend .NET 8 (ASP.NET Core Web API)

Web API cho hệ thống ERP FMCG, kiến trúc 4 lớp (Domain → Application → Infrastructure → Api), dùng Entity Framework Core trên MS SQL Server.

---

## 1. Yêu cầu hệ thống


| Thành phần | Phiên bản            | Ghi chú                                    |
| ---------- | -------------------- | ------------------------------------------ |
| .NET SDK   | 8.0 trở lên          | `dotnet --version` phải in ra `8.x`        |
| SQL Server | 2022 Express trở lên | Instance mặc định `.\SQLEXPRESS`           |
| Database   | `ERP_FMCG`           | Tạo bằng `erp-fmcg-database/01_schema.sql` |


> ⚠️ Backend **không tự tạo database**. Phải chạy schema trước, xem
> `[../erp-fmcg-database/README.md](../erp-fmcg-database/README.md)`.

---



## 2. Kiến trúc


| Project                  | Vai trò                                                                   |
| ------------------------ | ------------------------------------------------------------------------- |
| `ERP.FMC.Domain`         | Entity, enum, interface repository — không phụ thuộc project nào          |
| `ERP.FMC.Application`    | DTO, service nghiệp vụ, AutoMapper profile, validator                     |
| `ERP.FMC.Infrastructure` | EF Core `AppDbContext`, repository, service hạ tầng (import/export/audit) |
| `ERP.FMC.Api`            | Controller, DI container, cấu hình Kestrel + Swagger                      |
| `ERP.FMC.Tests`          | Unit test (xUnit + Moq) — 44 test                                         |


---



## 3. Cấu hình

Connection string nằm ở `src/ERP.FMC.Api/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=.\\SQLEXPRESS;Database=ERP_FMCG;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True"
}
```



### Đổi sang tài khoản SQL Server (không dùng Windows Authentication)

Thay `Trusted_Connection=True` bằng `User Id` + `Password`:

```json
"DefaultConnection": "Server=.\\SQLEXPRESS;Database=ERP_FMCG;User Id=sa;Password=YourPassword;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True"
```



### Ghi đè mà không sửa file (khuyến nghị)

Đặt biến môi trường, giá trị sẽ thắng `appsettings.json`:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=.\SQLEXPRESS;Database=ERP_FMCG;User Id=sa;Password=YourPassword;TrustServerCertificate=True;Encrypt=False"
```

> 🔒 Không commit mật khẩu. `appsettings.Local.json` và `appsettings.Production.json`
> đã nằm trong `.gitignore`.

---



## 4. Chạy backend

```powershell
cd erp-fmcg-backend-csharp
dotnet restore
dotnet build
dotnet run --project src/ERP.FMC.Api
```

Mặc định lắng nghe ở:

- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:5001`
- Swagger UI: `http://localhost:5000/swagger` (chỉ ở môi trường `Development`)

Kiểm tra nhanh:

```powershell
curl http://localhost:5000/
curl http://localhost:5000/api/health
```

`/api/health` trả về `status: UP` kèm số dòng thực tế của các bảng chính — đây là
cách nhanh nhất để xác nhận backend đã nối đúng database.

### Đổi cổng

`appsettings.json` khai báo cứng endpoint trong mục `Kestrel`. Mục này **ghi đè**
tham số `--urls`, nên muốn đổi cổng thì sửa file hoặc dùng biến môi trường:

```powershell
$env:Kestrel__Endpoints__Http__Url = "http://localhost:5050"
dotnet run --project src/ERP.FMC.Api
```

> Nếu cổng 5000 bị chiếm (một số VPN như TunnelBear dùng cổng này), backend sẽ
> báo `AddressInUseException`. Đổi cổng như trên hoặc tắt phần mềm đang giữ cổng.

---



## 5. Test

```powershell
dotnet test tests/ERP.FMC.Tests
```

Kỳ vọng: `Failed: 0, Passed: 44`.

---



## 6. Nhóm endpoint


| Nhóm                  | Tiền tố                                 | Ví dụ                                  |
| --------------------- | --------------------------------------- | -------------------------------------- |
| Health                | `/api/health`                           | `GET /api/health`                      |
| Partners              | `/api/partners`                         | `GET /api/partners?page=1&pageSize=20` |
| Products              | `/api/products`                         | `GET /api/products/by-sku/10000003`    |
| Categories            | `/api/categories`                       | `GET /api/categories`                  |
| Warehouses            | `/api/warehouses`                       | `GET /api/warehouses`                  |
| Routes                | `/api/routes`                           | `GET /api/routes`                      |
| Sales Orders          | `/api/sales-orders`                     | `POST /api/sales-orders/{id}/confirm`  |
| Inventory             | `/api/inventory`                        | `GET /api/inventory/balances`          |
| Price Lists           | `/api/price-lists`                      | `GET /api/price-lists/by-product/{id}` |
| Import                | `/api/imports`                          | `POST /api/imports/upload`             |
| Export                | `/api/exports`                          | `GET /api/exports/templates`           |
| Audit                 | `/api/audit`                            | `GET /api/audit/temporal/{table}/{id}` |
| UAT / Smoke / Go-Live | `/api/uat`, `/api/smoke`, `/api/golive` | `GET /api/golive/status`               |
| Monitoring            | `/api/monitoring`                       | `GET /api/monitoring/metrics`          |


Danh sách đầy đủ (kèm schema request/response): mở `http://localhost:5000/swagger`.
---

## 6.1. Nhóm endpoint Phase 5 (giao hàng)

| Nhóm        | Tiền tố       | Mô tả                                          |
| ----------- | ------------- | ---------------------------------------------- |
| Auth        | `/api/auth`   | `dev-login` sinh JWT cho tài xế                 |
| Trips       | `/api/sales`  | trips / trips/{id} / start / complete           |
| POD         | `/api/sales`  | `POST /api/sales/pod` (idempotent)              |
| Master data | `/api/master` | products, partners cho app offline              |

---

## 6.2. Nhóm endpoint Phase 6

| Nhóm             | Tiền tố               | Mô tả                                                          |
| ---------------- | --------------------- | -------------------------------------------------------------- |
| Dispatcher       | `/api/dispatcher`     | Tạo chuyến, gán đơn, xem trip đầy đủ, đối soát EOD           |
| Reports          | `/api/reports`        | Doanh thu, tỉ lệ giao thành công, aging công nợ, hàng trả về…   |
| Exports (Excel)  | `/api/exports/phase6` | Xuất 8 loại báo cáo vận hành ra `.xlsx` (ClosedXML)             |
| Health check sâu | `/api/health/deep`    | Kiểm tra DB, bảng delivery, stock ledger, EOD                   |
| Rate limiting    | `/api/sales/pod`      | 30 lần/phút / tài xế cho POD (chống spam)                      |

### Chốt chặn anti-fraud (6A.1)

`POST /api/sales/pod` **tự kiểm tra lại** dữ liệu client gửi lên:

| Chốt        | Quy tắc                                     | Mã lỗi                   |
| ----------- | ------------------------------------------- | ------------------------ |
| Time-lock   | Giờ VN trong 05:00-22:00                    | `OUT_OF_DELIVERY_WINDOW` |
| GPS Gate    | Cách khách <= 100 m (Haversine)             | `GPS_GATE_FAILED`        |
| No Backdate | Lệch `gps_captured_at` với server > 15 phút | `TIMESTAMP_OUT_OF_RANGE` |

`POST /api/sales/pod` có **idempotency** nhờ `client_pod_id` UNIQUE — gửi lại
cùng POD trả `200` với POD cũ (`created: false`), không tạo bản ghi trùng.


---



## 7. Lưu ý quan trọng về database

Database dùng **snake_case** (`partner_code`, `created_at`, `quantity_on_hand`),
trong khi entity C# dùng **PascalCase** (`PartnerCode`, `CreatedAt`, `OnHand`).
Việc chuyển đổi được làm tự động trong `AppDbContext.OnModelCreating`.

Một số cột lệch convention được khai báo tường minh ở
`src/ERP.FMC.Infrastructure/Configurations/SchemaMappings.cs`:

- `InventoryBalance.OnHand` → cột `quantity_on_hand` (không phải `on_hand`)
- `PriceList.EffectiveFrom` → cột `effective_date`
- `StockLedger` lưu số lượng tách thành `quantity_in` / `quantity_out`
- Cột temporal `SysStartTime` / `SysEndTime` giữ nguyên PascalCase và do SQL Server tự sinh

> Nếu sửa entity, chạy lại `dotnet test` và gọi thử endpoint tương ứng — lỗi
> `Invalid column name` xuất hiện ngay khi thiếu mapping.

---



## 8. Cấu trúc thư mục

```
erp-fmcg-backend-csharp/
├── ERP-FMCG.sln
├── src/
│   ├── ERP.FMC.Domain/          # Entity, enum, interface
│   ├── ERP.FMC.Application/     # DTO, service nghiệp vụ
│   ├── ERP.FMC.Infrastructure/  # EF Core, repository, service hạ tầng
│   └── ERP.FMC.Api/             # Controller, Program.cs, appsettings.json
└── tests/
    └── ERP.FMC.Tests/           # 44 unit test
```

---



## 9. Kết nối Driver App

| Ứng dụng          | Giá trị                              |
| ----------------- | ------------------------------------ |
| Backend API       | `http://localhost:5000`              |
| Driver App `.env` | `VITE_API_URL=http://localhost:5000` |

> ✅ Driver App trong pack này đã **có đủ route** `/api/auth/dev-login`,
> `/api/sales/trips`, `/api/sales/pod` — được implement trong `ERP.FMC.Api`
> (Phase 5B). Driver App dùng chung backend C#, không còn phụ thuộc
> backend NestJS cũ.

---

## 10. Kiểm thử Phase 6

| Bộ kiểm thử                        | Lệnh                                         | Kết quả    |
| ----------------------------------- | -------------------------------------------- | ---------- |
| Unit test backend (.NET 8)         | `dotnet test tests/ERP.FMC.Tests`            | 44/44 PASS |
| Verify dữ liệu (54 check)          | `python ../erp-fmcg-database/scripts/07_unified_verify.py` | 52 PASS + 2 INFO |
| Regression hash (dữ liệu)           | `python ../erp-fmcg-database/scripts/08_data_regression.py --check` | PASS |
| Backup tự động                     | `powershell ../erp-fmcg-database/scripts/09_backup_db.ps1` | PASS |
