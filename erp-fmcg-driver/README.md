# ERP FMCG — Driver App (PWA)

Ứng dụng giao hàng cho tài xế: React 18 + TypeScript + Vite + Tailwind, chạy offline bằng Dexie (IndexedDB).

---

## 1. Tính năng chính

- **Đăng nhập tài xế** bằng SĐT hoặc mã định danh, lưu JWT trong `localStorage`
- **Danh sách chuyến** được phân công, xem chi tiết từng điểm giao
- **Chống gian lận GPS** (`GPSGuard.tsx`, `timeGuard.ts`) — chặn cập nhật khi vị trí/giờ không hợp lệ
- **Chụp ảnh hiện trường** (`PhotoCapture.tsx`) làm bằng chứng giao hàng
- **Giao một phần** — nhập số lượng thực giao, ghi lý do thiếu
- **Thanh toán** (`PaymentScreen.tsx`) — ghi nhận tiền mặt / chuyển khoản
- **Offline-first** — dữ liệu lưu trong IndexedDB, `syncQueue.ts` tự đồng bộ khi có mạng
- **End of Day** (`EODScreen.tsx`) — tổng kết, nộp tiền, đóng chuyến

---



## 2. Yêu cầu


| Thành phần | Phiên bản                      |
| ---------- | ------------------------------ |
| Node.js    | 18 trở lên (đã test trên 24.x) |
| npm        | 9 trở lên                      |


---



## 3. Chạy ở môi trường phát triển

```bash
npm install
cp .env.example .env        # Windows: copy .env.example .env
# mở .env và sửa VITE_API_URL cho trỏ về backend
npm run dev
```

Mặc định chạy ở `http://localhost:5173`.

---



## 4. Build production

```bash
npm run build
```

Kết quả trong thư mục `dist/` (~380 KB JS + 30 KB CSS). Có thể deploy tĩnh lên
Nginx, Vercel, Netlify hoặc bất kỳ static host nào.

Xem thử bản build:

```bash
npm run preview
```



### Kiểm tra kiểu trước khi build

```bash
npx tsc --noEmit
```

---



## 5. Cấu hình `.env`


| Biến           | Bắt buộc            | Ý nghĩa                                    |
| -------------- | ------------------- | ------------------------------------------ |
| `VITE_API_URL` | Không (có mặc định) | URL backend, ví dụ `http://localhost:3001` |


`apiClient.ts` tự chuẩn hoá URL — có hay không có hậu tố `/api` đều được:


| `VITE_API_URL`              | Base URL thực tế        |
| --------------------------- | ----------------------- |
| `http://localhost:3001`     | `http://localhost:3001` |
| `http://localhost:3001/api` | `http://localhost:3001` |
| `https://x.com/api/`        | `https://x.com`         |


Nhờ vậy không xảy ra tình trạng request bị lặp `/api/api/...`.

> ⚠️ Biến `VITE_*` được nhúng vào bundle lúc **build**. Đổi `.env` phải build lại.

---



## 6. Cấu trúc `src/`


| Thư mục       | Nội dung                                                                                              |
| ------------- | ----------------------------------------------------------------------------------------------------- |
| `api/`        | `apiClient.ts` — axios instance, gắn JWT, xử lý 401                                                   |
| `components/` | `GPSGuard`, `PhotoCapture`, `PaymentInput`                                                            |
| `screens/`    | `LoginScreen`, `HomeScreen`, `DeliveryListScreen`, `DeliveryCoreScreen`, `PaymentScreen`, `EODScreen` |
| `services/`   | `authService`, `offlineDB` (Dexie), `syncQueue`                                                       |
| `store/`      | `driverStore.ts` — Zustand, state chuyến giao hiện tại                                                |
| `types/`      | Định nghĩa TypeScript dùng chung                                                                      |
| `utils/`      | `formatters.ts`, `timeGuard.ts`                                                                       |


---



## 7. Dữ liệu offline

`services/offlineDB.ts` khai báo 3 bảng Dexie:


| Bảng       | Khoá chính      | Dùng cho                                     |
| ---------- | --------------- | -------------------------------------------- |
| `partners` | `partner_id`    | Danh mục khách hàng tải về sau khi đăng nhập |
| `products` | `product_id`    | Danh mục sản phẩm                            |
| `orders`   | `trip_order_id` | Điểm giao của chuyến hiện tại                |


Luồng hoạt động:

1. Đăng nhập thành công → tải master data (`/api/partners`, `/api/products`) ghi vào Dexie
2. Mất mạng → app đọc Dexie, hiển thị banner "Đang offline"
3. Có mạng lại → `syncQueue` gửi các POD đang chờ lên `/api/sales/pod`

---



## 8. Phạm vi bàn giao

> ⚠️ **Đọc kỹ mục này trước khi demo.**

Driver App gọi các API chuyên biệt cho nghiệp vụ giao hàng:


| Endpoint                             | Mục đích                               |
| ------------------------------------ | -------------------------------------- |
| `POST /api/auth/dev-login`           | Đăng nhập tài xế                       |
| `GET /api/sales/trips`               | Danh sách chuyến được phân công        |
| `GET /api/sales/trips/{id}`          | Chi tiết chuyến + các điểm giao        |
| `PUT /api/sales/trips/{id}/start`    | Bắt đầu chuyến                         |
| `PUT /api/sales/trips/{id}/complete` | Kết thúc chuyến                        |
| `POST /api/sales/pod`                | Ghi nhận giao hàng (Proof of Delivery) |


Các route này thuộc **backend NestJS** (`/api/sales/trips`, `/api/sales/pod`,
`/api/auth/dev-login`) và **chưa được implement trong backend C#** của pack này.

Hệ quả và cách xử lý:


| Tình huống                        | Kết quả                                                         |
| --------------------------------- | --------------------------------------------------------------- |
| Chỉ demo giao diện, không cần API | Chạy `npm run dev`, app hiển thị màn hình đăng nhập bình thường |
| Cần đăng nhập thật                | Trỏ `VITE_API_URL` về backend NestJS đang chạy                  |
| Cần dùng backend C#               | Phải bổ sung 6 route trên vào `ERP.FMC.Api` trước               |


Nói cách khác: **backend C# và Driver App hiện là 2 deliverable độc lập.** Backend C# phục vụ quản trị/dữ liệu (partners, products, sales orders, inventory, import/export, audit); Driver App là PWA giao hàng cần backend có nghiệp vụ chuyến giao.

---



## 9. Scripts


| Lệnh              | Tác dụng                                 |
| ----------------- | ---------------------------------------- |
| `npm run dev`     | Dev server có hot reload                 |
| `npm run build`   | Typecheck + build production vào `dist/` |
| `npm run preview` | Chạy thử bản build                       |
| `npm run lint`    | ESLint (cần cấu hình ESLint riêng)       |


