# Dữ liệu nguồn ERP-FMCG

> **Thư mục này KHÔNG commit CSV vào git**.

---

## 1. Danh sách file

### 1.1. Nguồn chính — 7 file RDT (**bắt buộc**)


| File          | Dòng        | Cột    | Ghi chú                                      |
| ------------- | ----------- | ------ | -------------------------------------------- |
| `RDT1.26.csv` | 24.458      | 68     | Tuần 1                                       |
| `RDT2.26.csv` | 19.596      | 68     | Tuần 2                                       |
| `RDT3.26.csv` | 27.884      | **66** | Thiếu cột `Thuế GTGT (%)` và `Loại Chứng Từ` |
| `RDT4.26.csv` | 25.721      | 68     | Tuần 4                                       |
| `RDT5.26.csv` | 28.015      | 68     | Tuần 5                                       |
| `RDT6.26.csv` | 27.022      | 68     | Tuần 6                                       |
| `RDT7.26.csv` | 24.935      | 68     | Tuần 7                                       |
| **Tổng**      | **177.631** |        |                                              |


RDT là **nguồn sự thật duy nhất** cho: master data (khách hàng, sản phẩm, tuyến, NVBH),
đơn hàng và chi tiết dòng.

### 1.2. File bổ sung (seed + đối soát)


| File                          | Vai trò      | Ghi chú                                                                         |
| ----------------------------- | ------------ | ------------------------------------------------------------------------------- |
| `products_clean.csv`          | 84 SKU       | Bổ sung `selling_price`, `weight_per_unit`, `pack_size_ml` cho 81 SKU trùng RDT |
| `partners_clean.csv`          | 2.452 KH     | Bổ sung MST (`tax_code`), email, địa chỉ                                        |
| `user_accounts.csv`           | 38 tài khoản | **Nguồn duy nhất** cho user (RDT không có)                                      |
| `sales_orders_clean.csv`      | 11 đơn seed  | `SO-T001-*`, ngày ISO, `partner_id` thật                                        |
| `sales_order_items_clean.csv` | 14 dòng seed | `line_total` **đã bao gồm thuế** → pipeline tự chuẩn hoá về pre-tax             |
| `warehouses.csv`              | 1 kho        | `KHO001`                                                                        |


### 1.3. File legacy — **KHÔNG dùng**


| File                                        | Lý do bỏ                                                     |
| ------------------------------------------- | ------------------------------------------------------------ |
| `sales_orders_rd.csv`                       | Header sai định dạng, số lượng phình 1000×, `partner_id = 0` |
| `sales_order_items_rd.csv`                  | Số lượng phình 1000×/10⁹×, giá trị sai                       |
| `sales_orders.csv`, `sales_order_items.csv` | Bản gộp cũ, đã mất chi tiết dòng                             |


---

## 2. Quy tắc làm sạch áp dụng (R01–R17)


| Mã      | Quy tắc                                                                                              | Phạm vi       |
| ------- | ---------------------------------------------------------------------------------------------------- | ------------- |
| **R01** | Chuẩn hoá Unicode **NFC** cho tên cột & giá trị (file nguồn dùng **NFD**)                            | Tất cả        |
| **R02** | Ánh xạ cột **theo TÊN**, không theo vị trí                                                           | Tất cả        |
| **R03** | Dấu thập phân là `,` (không phải `.`)                                                                | Số            |
| **R04** | Ngày: RDT = **M/D/YYYY** (US); seed = **ISO** `YYYY-MM-DD`                                           | Ngày          |
| **R05** | RDT3 thiếu `Thuế GTGT (%)` → fallback **theo SKU** từ 6 file còn lại (126/126 SKU chỉ có 1 mức thuế) | Thuế          |
| **R06** | `Mã KH` → `partners.partner_code` (chuẩn hoá khoảng trắng, hoa/thường)                               | Master        |
| **R07** | `Mã SP` → `products.sku`                                                                             | Master        |
| **R08** | `Mã Route` → `routes.route_code`; tuyến đa số cho mỗi khách                                          | Master        |
| **R09** | `Mã NVBH` → `user_accounts.username` (tạo mới nếu chưa có)                                           | Master        |
| **R10** | `Mã Kho`: `MAIN_WHS` → `KHO001`; **rỗng →** `KHO001` (DB chỉ có 1 kho)                               | Master        |
| **R11** | 1.061 đơn có nhiều giá trị KH/tuyến/kho/ngày → chọn **đa số**, ghi cảnh báo                          | Đơn           |
| **R12** | `Mã SP` trống (1.313 dòng) → **bỏ dòng**, ghi `import_errors`                                        | Dòng hàng     |
| **R13** | `Số Điện Thoại` → chuẩn hoá 10 số; không chuẩn hoá được thì giữ nguyên                               | Master        |
| **R14** | MST / email chỉ lấy từ `partners_clean.csv` (không có → `NULL`)                                      | Master        |
| **R15** | Bỏ 10 SKU demo `10000001`…`10000010` khỏi seed (chỉ giữ `10000003` vì có thật trong RDT)             | Seed          |
| **R16** | `quantity` vượt `DECIMAL(15,2)` → clamp + cảnh báo                                                   | Dòng hàng     |
| **R17** | **Mọi** lỗi / dòng bị bỏ đều ghi vào `tenant.import_errors` (kèm `suggested_fix`)                    | Toàn pipeline |


---

---

## 3. Nguồn tải dữ liệu gốc

Dữ liệu được trích từ hệ thống báo cáo RDT của đơn vị phân phối FMCG. 

[Link Google Drive](https://drive.google.com/drive/folders/1ebPpkh9H9lt9WXTiZQNOEFE7BB9RbI9X?usp=sharing)

> Đặt file vào đúng thư mục này với **đúng tên file**
> (pipeline ánh xạ cột theo tên, không theo vị trí — nhưng tên file thì cố định).

