# ERP FMCG — Driver App (PWA)

Ứng dụng giao hàng cho tài xế: React 18 + TypeScript + Vite + Tailwind, chạy offline bằng Dexie (IndexedDB) + service worker.

---

## 1. Tính năng chính

- **Đăng nhập tài xế** bằng SĐT hoặc mã định danh, lưu JWT trong `localStorage`
- **Danh sách chuyến** được phân công, xem chi tiết từng điểm giao
- **Chống gian lận GPS** (`GPSGuard.tsx`, `timeGuard.ts`) — chặn cập nhật khi vị trí/giờ không hợp lệ
- **Chụp ảnh hiện trường** (`PhotoCapture.tsx`) làm bằng chứng giao hàng — ảnh được resize trước khi gửi
- **Giao một phần** — nhập số lượng thực giao, ghi lý do thiếu
- **Thanh toán** (`PaymentScreen.tsx`) — 6 ô: tiền mặt / chuyển khoản / ghi nợ, cho cả đơn mới và công nợ cũ
- **Offline-first** — dữ liệu lưu trong IndexedDB, `syncQueue.ts` tự đồng bộ khi có mạng
- **End of Day** (`EODScreen.tsx`) — tổng kết, nộp tiền, đóng chuyến
- **PWA thật** (6C.7) — cài được trên máy, mở offline được, có báo cáo trạng thái mạng

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

Mặc định chạy ở `http://localhost:3002` (proxy `/api` → `http://localhost:5000`).

---

## 4. Build production

```bash
npm run build
```

Kết quả trong thư mục `dist/`. Bản build có kèm PWA:

```
dist/
├── index.html
├── manifest.webmanifest     # manifest PWA
├── sw.js                    # service worker (Workbox)
├── workbox-*.js
├── favicon.svg
└── icons/
    ├── icon-192.png
    ├── icon-512.png
    └── icon-512-maskable.png
```

Có thể deploy tĩnh lên Nginx, Vercel, Netlify hoặc bất kỳ static host nào.

> ⚠️ Service worker chỉ hoạt động ở bản build (HTTPS hoặc `localhost`).
> Ở `npm run dev` không có service worker để tránh cache bản dev cũ.

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

| Biến           | Bắt buộc            | Ý nghĩa                                     |
| -------------- | ------------------- | ------------------------------------------- |
| `VITE_API_URL` | Không (có mặc định) | URL backend, ví dụ `http://localhost:5000`  |

`apiClient.ts` tự chuẩn hoá URL — có hay không có hậu tố `/api` đều được:

| `VITE_API_URL`              | Base URL thực tế        |
| --------------------------- | ----------------------- |
| `http://localhost:5000`     | `http://localhost:5000` |
| `http://localhost:5000/api` | `http://localhost:5000` |
| `https://x.com/api/`        | `https://x.com`         |

Nhờ vậy không xảy ra tình trạng request bị lặp `/api/api/...`.

> ⚠️ Biến `VITE_*` được nhúng vào bundle lúc **build**. Đổi `.env` phải build lại.

---

## 6. Cấu trúc `src/`

| Thư mục       | Nội dung                                                                                                  |
| ------------- | --------------------------------------------------------------------------------------------------------- |
| `api/`        | `apiClient.ts` — axios instance, gắn JWT, xử lý 401                                                       |
| `components/` | `GPSGuard`, `PhotoCapture`, `PaymentInput`, `SignaturePad`, `PwaStatusBanner`                              |
| `screens/`    | `LoginScreen`, `HomeScreen`, `DeliveryListScreen`, `DeliveryCoreScreen`, `PaymentScreen`, `EODScreen`       |
| `services/`   | `authService`, `offlineDB` (Dexie), `syncQueue`, `gpsService`                                              |
| `store/`      | `driverStore.ts` — Zustand, state chuyến giao hiện tại                                                    |
| `types/`      | Định nghĩa TypeScript dùng chung                                                                          |
| `utils/`      | `formatters.ts`, `timeGuard.ts`, `imageResize.ts`                                                          |

---

## 7. Dữ liệu offline

`services/offlineDB.ts` khai báo 7 bảng Dexie (schema v3):

| Bảng                 | Khoá chính      | Dùng cho                                     |
| -------------------- | --------------- | -------------------------------------------- |
| `partners`           | `partner_id`    | Danh mục khách hàng tải về sau khi đăng nhập |
| `products`           | `product_id`    | Danh mục sản phẩm                            |
| `trips`              | `trip_id`       | Chuyến giao                                  |
| `orders`             | `trip_order_id` | Điểm giao của chuyến hiện tại                |
| `pendingPods`        | `++id`          | POD chờ gửi lên server (offline queue)       |
| `pendingTripActions` | `++id`          | Lệnh "hoàn thành chuyến" khi offline         |
| `syncLog`            | `++id`          | Nhật ký đồng bộ                             |

Luồng hoạt động:

1. Đăng nhập thành công → tải master data (`/api/products`, `/api/partners`) ghi vào Dexie
2. Mất mạng → app đọc Dexie, hiển thị banner "Đang offline" (service worker giữ app mở được)
3. Giao hàng offline → POD vào `pendingPods`, `client_pod_id` **sinh đúng 1 lần**
4. Có mạng lại → `syncQueue` gửi các POD đang chờ lên `/api/sales/pod`
5. Server trả `created: false` nếu trùng → client đánh dấu đã sync, không retry vô ích

---

## 8. Phạm vi bàn giao

> ✅ **Phase 6 đã đóng giao kết nối giữa Driver App và backend C#.**
> Các route dưới đây **ĐÃ CÓ** trong `ERP.FMC.Api` (Phase 5B + Phase 6A/6B).

| Endpoint                             | Mục đích                               | Hiện thực tại                  |
| ------------------------------------ | -------------------------------------- | ------------------------------ |
| `POST /api/auth/dev-login`           | Đăng nhập tài xế (JWT)                 | `AuthController.cs` (5B.1)     |
| `GET /api/sales/trips`               | Danh sách chuyến được phân công        | `Phase5Controllers.cs` (5B.5)  |
| `GET /api/sales/trips/{id}`          | Chi tiết chuyến + các điểm giao        | `Phase5Controllers.cs` (5B.5)  |
| `PUT /api/sales/trips/{id}/start`    | Bắt đầu chuyến                         | `Phase5Controllers.cs` (5B.5)  |
| `PUT /api/sales/trips/{id}/complete` | Kết thúc chuyến                        | `Phase5Controllers.cs` (5B.5)  |
| `POST /api/sales/pod`                | Ghi nhận giao hàng (Proof of Delivery) | `Phase5Controllers.cs` (5B.5)  |

Ngoài ra backend C# còn cung cấp (Phase 6):

| Endpoint                             | Mục đích                                | Hiện thực tại                         |
| ------------------------------------ | --------------------------------------- | ------------------------------------- |
| `GET /api/health/deep`               | Health check sâu (DB, delivery, ledger)  | `HealthController.cs` (6C.8)          |
| `POST /api/dispatcher/trips`         | Dispatcher tạo chuyến                   | `DispatcherController.cs` (6B.1)      |
| `GET /api/dispatcher/trips/{id}/full` | Trip + POD + GPS (bản đồ audit)        | `DispatcherController.cs` (6B.1/6B.3) |
| `GET /api/reports/*`                 | 6 báo cáo vận hành                      | `ReportController.cs` (6B.2)          |
| `POST /api/dispatcher/trips/{id}/eod` | Đối soát cuối ngày                     | `DispatcherController.cs` (6B.4)      |
| `POST /api/exports/phase6/{type}`    | Xuất Excel báo cáo vận hành             | `ExportsController.cs` (6B.5)         |

### 8.1. Chốt chặn server-side (6A.1)

Backend C# **tự kiểm tra lại** mọi thứ client gửi lên, nên tài xế không thể
vượt qua bằng cách sửa code trên máy:

| Chốt        | Quy tắc                                         | Lỗi trả về               |
| ----------- | ----------------------------------------------- | ------------------------- |
| Time-lock   | Giờ VN trong 05:00–22:00                        | `OUT_OF_DELIVERY_WINDOW`  |
| GPS Gate    | Cách khách ≤ 100 m (bỏ qua nếu KH chưa geocode) | `GPS_GATE_FAILED`         |
| No Backdate | `gps_captured_at` lệch server > 15 phút         | `TIMESTAMP_OUT_OF_RANGE`  |

Ngoài ra `POST /api/sales/pod` có **idempotency** (`client_pod_id` UNIQUE): gửi
lại cùng POD → server trả `200` với POD cũ, **không** tạo bản ghi trùng.

### 8.2. PWA (6C.7)

App **cài được** và **mở offline được**:

- Manifest: tên, icon 192/512 + maskable, `display: standalone`, `theme_color`
- Service worker (Workbox): precache app shell, `navigateFallback` → `index.html`
- Cache runtime: master data (`StaleWhileRevalidate`), chuyến xe (`NetworkFirst`)
- **Không cache** POST `/api/sales/pod` — phải qua server để chống double-POD
- `PwaStatusBanner.tsx`: báo "Cập nhật" / "Offline" / nút "Cài app"

Sinh lại icon khi đổi thiết kế:

```bash
python scripts/generate_pwa_icons.py
```

---

## 9. Scripts

| Lệnh              | Tác dụng                                 |
| ----------------- | ---------------------------------------- |
| `npm run dev`     | Dev server có hot reload                 |
| `npm run build`   | Typecheck + build production (kèm PWA)  |
| `npm run preview` | Chạy thử bản build                       |
| `npm run lint`    | ESLint (cần cấu hình ESLint riêng)       |

| Script Python                   | Tác dụng                             |
| ------------------------------- | ------------------------------------ |
| `scripts/generate_pwa_icons.py` | Sinh lại icon PWA (192/512/maskable) |
