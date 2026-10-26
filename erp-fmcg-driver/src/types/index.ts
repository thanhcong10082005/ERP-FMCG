// ============================================================
// ERP-FMCG Driver App — Type Definitions
// ============================================================

export interface User {
  user_id: number;
  username: string;
  full_name: string;
  phone: string;
  email: string;
  role_id: number;
  role_name?: string;
  tenant_id: string;
  tenant_name?: string;
  team_id?: number;
}

export interface AuthResponse {
  access_token: string;
  user: User;
}

// Partner / Customer
export interface Partner {
  partner_id: number;
  tenant_id: string;
  partner_code: string;
  partner_name: string;
  phone: string;
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
  assigned_user_id?: number;
}

// Product
export interface Product {
  product_id: number;
  tenant_id: string;
  sku: string;
  product_name: string;
  barcode?: string;
  category_id: number;
  unit_id: number;
  unit_name?: string;
  tax_id?: number;
  cost_price: number;
  selling_price: number;
  min_stock: number;
  reorder_point: number;
  brand?: string;
  pack_type?: string;
}

// Trip Order (delivery stop)
export interface TripOrder {
  trip_order_id: number;
  trip_id: number;
  so_id: number;
  stop_order: number;
  status: TripOrderStatus;
  delivery_status?: string;
  failure_reason?: string;
  so_number: string;
  total_amount: number;
  paid_amount?: number;
  payment_status?: string;
  partner_id: number;
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

export type TripOrderStatus = 'PENDING' | 'DELIVERED' | 'PARTIAL' | 'FAILED' | 'CANCELLED';

// Delivery Trip
export interface DeliveryTrip {
  trip_id: number;
  tenant_id: string;
  trip_number: string;
  trip_date: string;
  warehouse_id: number;
  warehouse_name?: string;
  driver_id: number;
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
  started_at?: string;
  completed_at?: string;
  created_by: string;
  created_at: string;
  orders?: TripOrder[];
}

export type TripStatus = 'PREPARING' | 'LOADING' | 'DELIVERING' | 'COMPLETED' | 'CANCELLED' | 'PARTIAL_DELIVERED';

// Sales Order Item
export interface SOItem {
  so_item_id: number;
  so_id: number;
  product_id: number;
  sku?: string;
  product_name: string;
  unit_id: number;
  unit_name?: string;
  quantity: number;
  delivered_qty?: number;
  unit_price: number;
  discount_pct: number;
  discount_amount: number;
  tax_rate: number;
  tax_amount: number;
  line_subtotal: number;
  line_total: number;
  notes?: string;
}

// POD (Proof of Delivery)
export interface PODPayload {
  trip_order_id: number;
  so_id: number;
  partner_id: number;
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
}

export interface DeliveredItem {
  product_id: number;
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
  id?: number;
  trip_order_id: number;
  so_id: number;
  payload: PODPayload;
  sync_status: SyncStatus;
  retry_count: number;
  created_at: string;
  error_message?: string;
}

// EOD Report
export interface EODReport {
  trip_id: number;
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
    trip_order_id: number;
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
  tripId: number;
  tripOrderId: number;
}
