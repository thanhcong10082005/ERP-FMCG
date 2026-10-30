// ============================================================
// Zustand Store — Global app state
// ------------------------------------------------------------
// SỬA LỖI DOUBLE-POD (lỗi đã xảy ra THẬT trên
//   production Supabase: 6 nhóm POD trùng trong
//   Supabase_export/delivery_pod_rows.csv).
//
// Triệu chứng
//   1 lần giao hàng tạo ra 2 bản ghi POD trong DB.
//
// Nguyên nhân gốc
//   `submitFullDelivery` (và 2 hàm anh em) gọi `savePendingPOD()`
//   KHÔNG ĐIỀU KIỆN sau khi POST online thành công:
//       if (navigator.onLine) { try { await api.post(...) } catch {} }
//       await savePendingPOD({...});   // ← LUÔN enqueue
//   => Lần 1: POST online tạo POD. Lần 2: syncManager đọc queue và
//      POST LẠI cùng payload -> POD trùng.
//
// Cách sửa (3 lớp)
//   Lớp 1 (client): chỉ enqueue khi POST online THẤT BẠI.
//   Lớp 2 (client): mỗi lần giao sinh `client_pod_id` UUID v4 MỘT LẦN,
//           dùng lại cho mọi lần retry.
//   Lớp 3 (server): UNIQUE index UQ_delivery_pod_client — chốt chặn
//           cuối cùng, không phụ thuộc client.
//
// ID đổi `number` -> `string` (Guid).
// ============================================================

import { create } from 'zustand';
import type {
  User,
  DeliveryTrip,
  TripOrder,
  EODReport,
  PODPayload,
  PodResponse,
} from '../types';
import {
  db,
  upsertTrip,
  upsertOrders,
  getTripWithOrders,
  savePendingPOD,
  findPendingPodByClientId,
  savePendingTripAction,
  findPendingTripAction,
  ensureDbOpen,
} from '../services/offlineDB';
import api from '../api/apiClient';
import { syncManager } from '../services/syncQueue';
import { getCurrentPosition, haversineDistance } from '../services/gpsService';

// ── Extended order with local payment data ──────────────────
export interface OrderWithPayment extends TripOrder {
  _payment?: {
    cash: number;
    transfer: number;
    credit: number;
    oldDebtCash: number;
    oldDebtTransfer: number;
    oldDebtCredit: number;
    totalCollected: number;
  };
  _failureReason?: string;
  /**
   * tổng số lượng hàng RỚT (chưa giao được).
   *   Trước đây `total_returned_qty` trong EODReport hardcode `0` nên
   *   màn hình cuối ca không bao giờ hiện cảnh báo "hàng rớt cần trả kho".
   */
  _returnedQty?: number;
}

interface DriverState {
  // Auth
  user: User | null;
  token: string | null;

  // Current trip
  currentTrip: DeliveryTrip | null;
  orders: OrderWithPayment[];

  // Connectivity
  isOnline: boolean;
  pendingSyncCount: number;

  // Loading / errors
  isLoading: boolean;
  error: string | null;

  // Actions
  setUser: (user: User, token: string) => void;
  setOnline: (online: boolean) => void;
  setPendingSyncCount: (count: number) => void;

  // Trip actions
  loadCurrentTrip: (tripId: string) => Promise<void>;
  startTrip: (tripId: string) => Promise<void>;
  refreshOrders: () => Promise<void>;

  // POD actions
  submitFullDelivery: (tripOrderId: string, payment: PaymentSplit) => Promise<void>;
  submitPartialDelivery: (
    tripOrderId: string,
    payment: PaymentSplit,
    deliveredItems: DeliveredItem[],
    signatureData: string,
    photoData: string,
  ) => Promise<void>;
  submitFailedDelivery: (
    tripOrderId: string,
    reason: string,
    photoData: string,
  ) => Promise<void>;

  // internal helper (best-effort GPS capture)
  _captureGPSPayload: (order: OrderWithPayment) => Promise<{
    delivery_latitude: number;
    delivery_longitude: number;
    delivery_gps_accuracy_m: number;
    distance_to_partner_m: number | null;
    gps_captured_at: string;
  } | null>;

  // EOD
  completeTrip: () => Promise<EODReport>;
  logout: () => Promise<void>;
}

// ── Payment split ────────────────────────────────────────
export interface PaymentSplit {
  newOrderCash: number;
  newOrderTransfer: number;
  newOrderCredit: number;
  oldDebtCash: number;
  oldDebtTransfer: number;
  oldDebtCredit: number;
  totalCollected: number;
  recipientName?: string;
  notes?: string;
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

// ============================================================
// Sinh UUID v4 cho client_pod_id
// ------------------------------------------------------------
// Dùng crypto.randomUUID() nếu có (HTTPS / localhost).
// Fallback sang Math.random() cho trường hợp trình duyệt cũ /
// truy cập qua http trong môi trường LAN (secure context = false).
//
// LƯU Ý: id này phải được sinh MỘT LẦN cho 1 lần giao hàng và
// dùng lại cho mọi lần retry. TUYỆT ĐỐI không sinh lại trong
// syncQueue.
// ============================================================
export function generateClientPodId(): string {
  const cryptoObj = globalThis.crypto as Crypto | undefined;
  if (cryptoObj && typeof cryptoObj.randomUUID === 'function') {
    return cryptoObj.randomUUID();
  }

  // Fallback: UUID v4 thủ công
  const bytes = new Uint8Array(16);
  if (cryptoObj && typeof cryptoObj.getRandomValues === 'function') {
    cryptoObj.getRandomValues(bytes);
  } else {
    for (let i = 0; i < 16; i += 1) {
      bytes[i] = Math.floor(Math.random() * 256);
    }
  }
  // version 4
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  // variant 10xx
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
  return [
    hex.slice(0, 8),
    hex.slice(8, 12),
    hex.slice(12, 16),
    hex.slice(16, 20),
    hex.slice(20, 32),
  ].join('-');
}

// ============================================================
// Helper gửi POD an toàn (1 nguồn sự thật)
// ------------------------------------------------------------
// Quy tắc duy nhất:
//   1. Sinh client_pod_id MỘT LẦN.
//   2. Nếu đã có POD pending với client_pod_id này -> KHÔNG enqueue
//      lại (tránh trùng trong queue).
//   3. Nếu online và POST thành công -> KHÔNG enqueue.
//   4. Nếu offline hoặc POST lỗi -> enqueue để sync sau.
// ============================================================
async function submitPod(
  payload: PODPayload,
): Promise<{ synced: boolean; response?: PodResponse }> {
  await ensureDbOpen();

  // (2) đã queue rồi -> không enqueue lại
  const existing = await findPendingPodByClientId(payload.client_pod_id);
  if (existing) {
    console.log(
      `[POD] client_pod_id ${payload.client_pod_id} đã có trong queue — bỏ qua enqueue.`,
    );
    return { synced: false };
  }

  // (3) online -> thử POST
  if (navigator.onLine) {
    try {
      const response = await api.post<PodResponse>('/api/sales/pod', payload);
      console.log(
        `[POD] POST online thành công (created=${response.data?.created}) — KHÔNG enqueue.`,
      );
      return { synced: true, response: response.data };
    } catch (err) {
      console.warn('[POD] POST online thất bại — chuyển sang queue offline.', err);
    }
  }

  // (4) offline / lỗi -> enqueue
  await savePendingPOD({
    trip_order_id: payload.trip_order_id,
    so_id: payload.so_id,
    payload,
    sync_status: 'pending',
    retry_count: 0,
    created_at: new Date().toISOString(),
  });
  console.log(
    `[POD] Đã lưu queue offline (client_pod_id=${payload.client_pod_id}).`,
  );
  return { synced: false };
}

export const useDriverStore = create<DriverState>((set, get) => ({
  user: null,
  token: null,
  currentTrip: null,
  orders: [],
  isOnline: navigator.onLine,
  pendingSyncCount: 0,
  isLoading: false,
  error: null,

  setUser: (user, token) => {
    set({ user, token });
  },

  setOnline: (online) => {
    set({ isOnline: online });
    if (online) syncManager.syncAll();
  },

  setPendingSyncCount: (count) => set({ pendingSyncCount: count }),

  loadCurrentTrip: async (tripId) => {
    set({ isLoading: true, error: null });
    await ensureDbOpen();

    // ID đã là string (UUID) — KHÔNG cần normalize int.
    // Vẫn giữ hàm nhẹ để an toàn nếu server trả số.
    const asId = (v: unknown): string => {
      if (typeof v === 'string') return v;
      if (typeof v === 'number') return String(v);
      return '';
    };
    const normalize = (raw: any): DeliveryTrip => ({
      ...raw,
      trip_id: asId(raw.trip_id),
      warehouse_id: asId(raw.warehouse_id),
      driver_id: asId(raw.driver_id),
      total_orders: Number(raw.total_orders) || 0,
      delivered_count: Number(raw.delivered_count) || 0,
      failed_count: Number(raw.failed_count) || 0,
      orders: (raw.orders || []).map((o: any) => ({
        ...o,
        trip_order_id: asId(o.trip_order_id),
        so_id: asId(o.so_id),
        partner_id: asId(o.partner_id),
        partner_latitude:
          typeof o.partner_latitude === 'string'
            ? parseFloat(o.partner_latitude)
            : o.partner_latitude,
        partner_longitude:
          typeof o.partner_longitude === 'string'
            ? parseFloat(o.partner_longitude)
            : o.partner_longitude,
      })),
    });

    try {
      const response = await api.get(`/api/sales/trips/${tripId}`);
      const trip = normalize(response.data);
      const orders: OrderWithPayment[] = (trip.orders || []) as OrderWithPayment[];

      await db.transaction('rw', [db.trips, db.orders], async () => {
        await upsertTrip(trip);
        await upsertOrders(orders);
      });

      set({ currentTrip: trip, orders, isLoading: false });
    } catch {
      const localTrip = await getTripWithOrders(tripId);
      if (localTrip) {
        const trip = normalize(localTrip);
        set({ currentTrip: trip, orders: trip.orders as OrderWithPayment[] || [], isLoading: false });
      } else {
        set({ error: 'Không thể tải chuyến xe', isLoading: false });
      }
    }
  },

  startTrip: async (tripId) => {
    set({ isLoading: true, error: null });

    // Offline vẫn cho tài xế bắt đầu chuyến (đánh dấu
    //   DELIVERING cục bộ, server đồng bộ sau). Đây KHÔNG phải
    //   lỗi, nên không rethrow.
    if (!navigator.onLine) {
      const trip = get().currentTrip;
      if (trip) {
        set({
          currentTrip: {
            ...trip,
            status: 'DELIVERING',
            started_at: new Date().toISOString(),
          },
          error: null,
          isLoading: false,
        });
        console.warn('[Trip] startTrip offline — đánh dấu DELIVERING cục bộ.');
        return;
      }
    }

    try {
      await api.put(`/api/sales/trips/${tripId}/start`);
      await get().loadCurrentTrip(tripId);
    } catch (err) {
      // PHẢI rethrow. Trước đây hàm nuốt lỗi => HomeScreen
      //   vẫn navigate('/deliveries') dù chuyến chưa hề bắt đầu.
      set({
        error: 'Không thể bắt đầu chuyến xe — kiểm tra kết nối',
        isLoading: false,
      });
      throw err;
    }
  },

  refreshOrders: async () => {
    const { currentTrip } = get();
    if (!currentTrip) return;
    await get().loadCurrentTrip(currentTrip.trip_id);
  },

  // GPS capture cho POD (helper, không export) ───
  // Best-effort: nếu không lấy được GPS (timeout, browser không hỗ trợ),
  // vẫn submit POD bình thường — không block business flow.
  // (Đặt trong closure để truy cập `get()` của zustand)
  _captureGPSPayload: async (order: OrderWithPayment) => {
    try {
      const pos = await getCurrentPosition();
      let distance: number | null = null;
      if (typeof order.partner_latitude === 'number' && typeof order.partner_longitude === 'number') {
        distance = Math.round(haversineDistance(
          pos.latitude, pos.longitude,
          order.partner_latitude, order.partner_longitude,
        ));
      }
      return {
        delivery_latitude: pos.latitude,
        delivery_longitude: pos.longitude,
        delivery_gps_accuracy_m: pos.accuracy ?? 0,
        distance_to_partner_m: distance,
        gps_captured_at: new Date().toISOString(),
      };
    } catch (err) {
      console.warn('[POD GPS] capture failed, submitting without GPS:', err);
      return null;
    }
  },

  submitFullDelivery: async (tripOrderId, payment) => {
    const order = get().orders.find((o) => o.trip_order_id === tripOrderId);
    const { currentTrip } = get();
    if (!order || !currentTrip) throw new Error('Order not found');

    // capture GPS tại điểm giao
    const gps = await get()._captureGPSPayload(order);

    // sinh client_pod_id MỘT LẦN
    const clientPodId = generateClientPodId();

    const payload: PODPayload = {
      client_pod_id: clientPodId,
      trip_order_id: tripOrderId,
      so_id: order.so_id,
      partner_id: order.partner_id,
      delivery_status: 'DELIVERED',
      new_order_cash: payment.newOrderCash,
      new_order_transfer: payment.newOrderTransfer,
      new_order_credit: payment.newOrderCredit,
      old_debt_cash: payment.oldDebtCash,
      old_debt_transfer: payment.oldDebtTransfer,
      old_debt_credit: payment.oldDebtCredit,
      total_collected: payment.totalCollected,
      recipient_name: payment.recipientName,
      notes: payment.notes,
      ...(gps || {}),  // spread GPS nếu có, không có thì bỏ qua
    };

    await submitPod(payload);

    // Update local state with payment data
    const updatedOrder: OrderWithPayment = {
      ...order,
      status: 'DELIVERED',
      delivery_status: 'DELIVERED',
      _payment: {
        cash: payment.newOrderCash,
        transfer: payment.newOrderTransfer,
        credit: payment.newOrderCredit,
        oldDebtCash: payment.oldDebtCash,
        oldDebtTransfer: payment.oldDebtTransfer,
        oldDebtCredit: payment.oldDebtCredit,
        totalCollected: payment.totalCollected,
      },
    };

    set((s) => ({
      orders: s.orders.map((o) => o.trip_order_id === tripOrderId ? updatedOrder : o),
    }));
  },

  submitPartialDelivery: async (tripOrderId, payment, items, signatureData, photoData) => {
    const order = get().orders.find((o) => o.trip_order_id === tripOrderId);
    const { currentTrip } = get();
    if (!order || !currentTrip) throw new Error('Order not found');

    // capture GPS
    const gps = await get()._captureGPSPayload(order);

    // sinh client_pod_id MỘT LẦN
    const clientPodId = generateClientPodId();

    const payload: PODPayload = {
      client_pod_id: clientPodId,
      trip_order_id: tripOrderId,
      so_id: order.so_id,
      partner_id: order.partner_id,
      delivery_status: 'PARTIAL',
      new_order_cash: payment.newOrderCash,
      new_order_transfer: payment.newOrderTransfer,
      new_order_credit: payment.newOrderCredit,
      old_debt_cash: payment.oldDebtCash,
      old_debt_transfer: payment.oldDebtTransfer,
      old_debt_credit: payment.oldDebtCredit,
      total_collected: payment.totalCollected,
      recipient_name: payment.recipientName,
      notes: payment.notes,
      signature_url: signatureData,
      photos: [photoData],
      delivered_items: items,
      ...(gps || {}),
    };

    await submitPod(payload);

    const updatedOrder: OrderWithPayment = {
      ...order,
      status: 'PARTIAL',
      delivery_status: 'PARTIAL',
      // lưu số lượng hàng rớt để EOD báo cáo.
      _returnedQty: items.reduce((sum, i) => sum + (i.returned_qty ?? 0), 0),
      _payment: {
        cash: payment.newOrderCash,
        transfer: payment.newOrderTransfer,
        credit: payment.newOrderCredit,
        oldDebtCash: payment.oldDebtCash,
        oldDebtTransfer: payment.oldDebtTransfer,
        oldDebtCredit: payment.oldDebtCredit,
        totalCollected: payment.totalCollected,
      },
    };

    set((s) => ({
      orders: s.orders.map((o) => o.trip_order_id === tripOrderId ? updatedOrder : o),
    }));
  },

  submitFailedDelivery: async (tripOrderId, reason, photoData) => {
    const order = get().orders.find((o) => o.trip_order_id === tripOrderId);
    const { currentTrip } = get();
    if (!order || !currentTrip) throw new Error('Order not found');

    // capture GPS (kể cả khi fail delivery, vẫn ghi nhận vị trí tài xế)
    const gps = await get()._captureGPSPayload(order);

    // sinh client_pod_id MỘT LẦN
    const clientPodId = generateClientPodId();

    const payload: PODPayload = {
      client_pod_id: clientPodId,
      trip_order_id: tripOrderId,
      so_id: order.so_id,
      partner_id: order.partner_id,
      delivery_status: 'FAILED',
      failure_reason: reason,
      new_order_cash: 0,
      new_order_transfer: 0,
      new_order_credit: 0,
      old_debt_cash: 0,
      old_debt_transfer: 0,
      old_debt_credit: 0,
      total_collected: 0,
      notes: reason,
      photos: [photoData],
      ...(gps || {}),
    };

    await submitPod(payload);

    const updatedOrder: OrderWithPayment = {
      ...order,
      status: 'FAILED',
      delivery_status: 'FAILED',
      _failureReason: reason,
      // giao thất bại => toàn bộ số lượng là hàng rớt.
      _returnedQty: (order.items || []).reduce(
        (sum, item) => sum + (item.quantity ?? 0), 0,
      ),
    };

    set((s) => ({
      orders: s.orders.map((o) => o.trip_order_id === tripOrderId ? updatedOrder : o),
    }));
  },

  completeTrip: async () => {
    const { currentTrip, orders } = get();
    if (!currentTrip) throw new Error('No active trip');

    const delivered = orders.filter(
      (o) => o.status === 'DELIVERED' || o.status === 'PARTIAL',
    );
    const failed = orders.filter((o) => o.status === 'FAILED');

    const totalCash = orders.reduce(
      (sum, o) => sum + (o._payment?.cash ?? 0), 0,
    );
    const totalTransfer = orders.reduce(
      (sum, o) => sum + (o._payment?.transfer ?? 0), 0,
    );
    // total_old_debt phải gồm CẢ tiền mặt lẫn chuyển
    //   khoản thu được cho công nợ cũ (trước đây chỉ tính cash).
    const totalOldDebt = orders.reduce(
      (sum, o) => sum + (o._payment?.oldDebtCash ?? 0) + (o._payment?.oldDebtTransfer ?? 0), 0,
    );

    // tổng hàng rớt — trước đây hardcode 0 nên
    //   EOD không bao giờ cảnh báo "hàng rớt cần trả kho".
    const totalReturnedQty = orders.reduce(
      (sum, o) => sum + (o._returnedQty ?? 0), 0,
    );

    const report: EODReport = {
      trip_id: currentTrip.trip_id,
      trip_number: currentTrip.trip_number,
      status: failed.length === 0 ? 'COMPLETED' : 'PARTIAL_DELIVERED',
      total_orders: orders.length,
      delivered_count: delivered.length,
      failed_count: failed.length,
      total_cash: totalCash,
      total_transfer: totalTransfer,
      total_old_debt: totalOldDebt,
      total_collected: totalCash + totalTransfer + totalOldDebt,
      total_returned_qty: totalReturnedQty,
    };

    // hoàn thành chuyến phải được queue khi offline,
    //   nếu không trạng thái "đã kết thúc" bị MẤT HOÀN TOÀN.
    await ensureDbOpen();
    const tripId = currentTrip.trip_id;

    // Tránh queue trùng cho cùng 1 trip.
    const alreadyQueued = await findPendingTripAction(tripId, 'COMPLETE_TRIP');
    if (alreadyQueued) {
      console.log(`[Trip] COMPLETE_TRIP đã queue cho trip ${tripId} — bỏ qua.`);
      return report;
    }

    if (navigator.onLine) {
      try {
        await api.put(`/api/sales/trips/${tripId}/complete`);
      } catch (err) {
        console.warn('[Trip] completeTrip online thất bại — chuyển sang queue offline.', err);
        await savePendingTripAction({
          trip_id: tripId,
          action: 'COMPLETE_TRIP',
          sync_status: 'pending',
          retry_count: 0,
          created_at: new Date().toISOString(),
        });
      }
    } else {
      await savePendingTripAction({
        trip_id: tripId,
        action: 'COMPLETE_TRIP',
        sync_status: 'pending',
        retry_count: 0,
        created_at: new Date().toISOString(),
      });
      console.log(`[Trip] completeTrip offline — đã queue COMPLETE_TRIP cho trip ${tripId}.`);
    }

    return report;
  },

  logout: async () => {
    syncManager.stop();
    localStorage.removeItem('driver_token');
    localStorage.removeItem('driver_user');
    // Clear cached tables WITHOUT destroying the DB instance.
    // Calling `db.delete()` would close the singleton Dexie connection
    // and trigger DatabaseClosedError on the next login / query.
    try {
      await db.transaction(
        'rw',
        [db.partners, db.products, db.trips, db.orders, db.pendingPods, db.syncLog],
        async () => {
          await db.partners.clear();
          await db.products.clear();
          await db.trips.clear();
          await db.orders.clear();
          await db.pendingPods.clear();
          await db.syncLog.clear();
        },
      );
    } catch (err) {
      console.warn('[Logout] clear tables failed (non-fatal):', err);
    }
    set({
      user: null, token: null, currentTrip: null,
      orders: [], isLoading: false, error: null,
    });
  },
}));

// Network listeners
window.addEventListener('online', () => useDriverStore.getState().setOnline(true));
window.addEventListener('offline', () => useDriverStore.getState().setOnline(false));
