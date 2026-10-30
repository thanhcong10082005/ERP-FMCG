// ============================================================
// ERP-FMCG Driver App — Type Definitions
// ------------------------------------------------------------
// TẤT CẢ ID ĐỔI TỪ `number` SANG `string`.
//
// Lý do (quyết định D1):
//   Backend C# dùng UNIQUEIDENTIFIER (Guid 128-bit). JS `number` chỉ
//   an toàn tới 2^53-1 (~9e15). Nếu để `number`, các ID Guid sẽ bị
//   mất precision → hai record khác nhau có thể cùng ID → ghi POD
//   nhầm đơn hàng.
//
//   Đây KHÔNG phải lý do thẩm mỹ — đây là lý do TRÙNG DỮ LIỆU.
//
// Contract với Backend C# (ERP.FMC.Application/DTOs/DriverDtos.cs):
//   - Tất cả field snake_case
//   - Tất cả ID là string
//   - status là string (không enum)
// ============================================================

export interface User {
  user_id: string;
  username: string;
  full_name: string;
  phone: string;
  email: string;
  role: string;
  role_name?: string;
  tenant_id: string;
  tenant_name?: string;
  route_id?: string;
}

export interface AuthResponse {
  access_token: string;
  refresh_token?: string;
  expires_at?: string;
  user: User;
}

// Partner / Customer
export interface Partner {
  partner_id: string;
  tenant_id: string;
  partner_code: string;
  partner_name: string;
  phone?: string;
  email?: string;
  address_line?: string;
  ward?: string;
  district?: string;
  province?: string;
  latitude?: number;
  longitude?: number;
  partner_type: string;
  current_debt: number;
  old_debt: number;
  old_debt_date?: string;
  route_code?: string;
  assigned_user_id?: string;
}

// Product
export interface Product {
  product_id: string;
  tenant_id: string;
  sku: string;
  product_name: string;
  barcode?: string;
  category_id?: string;
  uom_code: string;
  cost_price: number;
  selling_price: number;
  min_stock: number;
  max_stock: number;
  reorder_point: number;
  brand?: string;
  pack_type?: string;
}

// Trip Order (delivery stop)
export interface TripOrder {
  trip_order_id: string;
  trip_id: string;
  so_id: string;
  stop_order: number;
  status: TripOrderStatus;
  delivery_status?: string;
  failure_reason?: string;
  failure_count?: number;
  so_number: string;
  total_amount: number;
  paid_amount?: number;
  payment_status?: string;
  partner_id: string;
  partner_name: string;
  partner_phone?: string;
  partner_address?: string;
  partner_latitude?: number;
  partner_longitude?: number;
  phone?: string;
  address_line?: string;
  latitude?: number;
  longitude?: number;
  old_debt_amount: number;
  items?: SOItem[];
  notes?: string;
}

export type TripOrderStatus =
  | 'PENDING'
  | 'DELIVERED'
  | 'PARTIAL'
  | 'FAILED'
  | 'CANCELLED';

// Delivery Trip
export interface DeliveryTrip {
  trip_id: string;
  tenant_id: string;
  trip_number: string;
  trip_date: string;
  warehouse_id: string;
  warehouse_name?: string;
  driver_id?: string;
  driver_name?: string;
  vehicle_plate?: string;
  status: TripStatus;
  total_orders: number;
  total_amount: number;
  delivered_count: number;
  failed_count: number;
  total_cash: number;
  total_transfer: number;
  total_old_debt: number;
  total_current_debt?: number;
  started_at?: string;
  completed_at?: string;
  created_by?: string;
  created_at: string;
  updated_at?: string;
  notes?: string;
  orders?: TripOrder[];
}

export type TripStatus =
  | 'PREPARING'
  | 'LOADING'
  | 'DELIVERING'
  | 'COMPLETED'
  | 'CANCELLED'
  | 'PARTIAL_DELIVERED';

// Sales Order Item
export interface SOItem {
  so_item_id: string;
  so_id: string;
  product_id: string;
  sku?: string;
  product_name: string;
  uom_code: string;
  quantity: number;
  delivered_qty?: number;
  returned_qty?: number;
  unit_price: number;
  discount_amount: number;
  line_subtotal: number;
  line_total: number;
  notes?: string;
}

// ── POD (Proof of Delivery) ───────────────────────────────────

/**
 * Payload gửi lên server.
 *
 * `client_pod_id` BẮT BUỘC (idempotency key).
 *   Client sinh UUID v4 MỘT LẦN khi tài xế bấm "Giao hang" và dùng
 *   lại cho mọi lần retry. Server có UNIQUE index nên chỉ tạo 1 POD.
 *
 *   KHÔNG ĐƯỢC sinh mới client_pod_id mỗi lần retry — làm vô nghĩa
 *   idempotency key.
 */
export interface PODPayload {
  client_pod_id: string;
  trip_order_id: string;
  so_id: string;
  partner_id: string;
  delivery_status: 'DELIVERED' | 'PARTIAL' | 'FAILED';
  new_order_cash: number;
  new_order_transfer: number;
  new_order_credit: number;
  old_debt_cash: number;
  old_debt_transfer: number;
  old_debt_credit: number;
  total_collected: number;
  recipient_name?: string;
  notes?: string;
  failure_reason?: string;
  signature_url?: string;
  photos?: string[];
  delivered_items?: DeliveredItem[];

  // GPS
  delivery_latitude?: number;
  delivery_longitude?: number;
  delivery_gps_accuracy_m?: number;
  distance_to_partner_m?: number | null;
  gps_captured_at?: string;
}

/** Response từ POST /api/sales/pod */
export interface PodResponse {
  pod_id: string;
  client_pod_id: string;
  trip_order_id: string;
  so_id: string;
  partner_id: string;
  delivery_status: string;
  total_collected: number;
  /** true = POD mới; false = idempotent hit (đã tồn tại) */
  created: boolean;
  message?: string;
}

export interface DeliveredItem {
  product_id: string;
  product_name: string;
  ordered_qty: number;
  delivered_qty: number;
  returned_qty: number;
  unit_price: number;
  line_total: number;
}

// Partial delivery detail
export interface PartialDeliveryPayload extends PODPayload {
  delivered_items: DeliveredItem[];
  signature_data?: string;
  photo_data?: string;
}

// Sync status
export type SyncStatus = 'pending' | 'syncing' | 'synced' | 'failed';

export interface PendingPOD {
  /** Dexie auto-increment (local id) — KHÔNG phải ID nghiệp vụ. */
  id?: number;
  trip_order_id: string;
  so_id: string;
  payload: PODPayload;
  sync_status: SyncStatus;
  retry_count: number;
  created_at: string;
  error_message?: string;
}

// EOD Report
export interface EODReport {
  trip_id: string;
  trip_number: string;
  status: 'COMPLETED' | 'PARTIAL_DELIVERED';
  total_orders: number;
  delivered_count: number;
  failed_count: number;
  total_cash: number;
  total_transfer: number;
  total_old_debt: number;
  total_collected: number;
  total_returned_qty: number;
  items?: Array<{
    trip_order_id: string;
    so_number: string;
    partner_name: string;
    status: string;
    total_amount: number;
    total_collected: number;
    returned_qty?: number;
  }>;
}

// App route params
export interface DeliveryCoreParams {
  tripId: string;
  tripOrderId: string;
}
