// ============================================================
// Zustand Store — Global app state
// ============================================================

import { create } from 'zustand';
import type {
  User,
  DeliveryTrip,
  TripOrder,
  EODReport,
} from '../types';
import {
  db,
  upsertTrip,
  upsertOrders,
  getTripWithOrders,
  savePendingPOD,
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
  loadCurrentTrip: (tripId: number) => Promise<void>;
  startTrip: (tripId: number) => Promise<void>;
  refreshOrders: () => Promise<void>;

  // POD actions
  submitFullDelivery: (tripOrderId: number, payment: PaymentSplit) => Promise<void>;
  submitPartialDelivery: (
    tripOrderId: number,
    payment: PaymentSplit,
    deliveredItems: DeliveredItem[],
    signatureData: string,
    photoData: string,
  ) => Promise<void>;
  submitFailedDelivery: (
    tripOrderId: number,
    reason: string,
    photoData: string,
  ) => Promise<void>;

  // Phase 4 — internal helper (best-effort GPS capture)
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
  product_id: number;
  product_name: string;
  ordered_qty: number;
  delivered_qty: number;
  returned_qty: number;
  unit_price: number;
  line_total: number;
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

    // Normalize helper — coerces BigInt-string fields from pg driver to real numbers.
    const toInt = (v: unknown): number => {
      if (typeof v === 'number') return v;
      if (typeof v === 'string') return parseInt(v, 10) || 0;
      return 0;
    };
    const normalize = (raw: any): DeliveryTrip => ({
      ...raw,
      trip_id: toInt(raw.trip_id),
      warehouse_id: toInt(raw.warehouse_id),
      driver_id: toInt(raw.driver_id),
      total_orders: toInt(raw.total_orders),
      delivered_count: toInt(raw.delivered_count),
      failed_count: toInt(raw.failed_count),
      orders: (raw.orders || []).map((o: any) => ({
        ...o,
        trip_order_id: toInt(o.trip_order_id),
        so_id: toInt(o.so_id),
        partner_id: toInt(o.partner_id),
        warehouse_id: toInt(o.warehouse_id),
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
    set({ isLoading: true });
    try {
      await api.put(`/api/sales/trips/${tripId}/start`);
      await get().loadCurrentTrip(tripId);
    } catch {
      set({ error: 'Không thể bắt đầu chuyến xe — kiểm tra kết nối', isLoading: false });
    }
  },

  refreshOrders: async () => {
    const { currentTrip } = get();
    if (!currentTrip) return;
    await get().loadCurrentTrip(currentTrip.trip_id);
  },

  // ── Phase 4: GPS capture cho POD (helper, không export) ───
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
    const order = get().orders.find((o) => Number(o.trip_order_id) === tripOrderId);
    const { currentTrip } = get();
    if (!order || !currentTrip) throw new Error('Order not found');

    // Phase 4: capture GPS tại điểm giao
    const gps = await get()._captureGPSPayload(order);

    const payload = {
      trip_order_id: tripOrderId,
      so_id: order.so_id,
      partner_id: order.partner_id,
      delivery_status: 'DELIVERED' as const,
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

    if (navigator.onLine) {
      try {
        await api.post('/api/sales/pod', payload);
      } catch {
        // queue offline
      }
    }

    await savePendingPOD({
      trip_order_id: tripOrderId,
      so_id: order.so_id,
      payload,
      sync_status: 'pending',
      retry_count: 0,
      created_at: new Date().toISOString(),
    });

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
      orders: s.orders.map((o) => Number(o.trip_order_id) === tripOrderId ? updatedOrder : o),
    }));
  },

  submitPartialDelivery: async (tripOrderId, payment, items, signatureData, photoData) => {
    const order = get().orders.find((o) => Number(o.trip_order_id) === tripOrderId);
    const { currentTrip } = get();
    if (!order || !currentTrip) throw new Error('Order not found');

    // Phase 4: capture GPS
    const gps = await get()._captureGPSPayload(order);

    const payload = {
      trip_order_id: tripOrderId,
      so_id: order.so_id,
      partner_id: order.partner_id,
      delivery_status: 'PARTIAL' as const,
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

    if (navigator.onLine) {
      try {
        await api.post('/api/sales/pod', payload);
      } catch {
        // queue offline
      }
    }

    await savePendingPOD({
      trip_order_id: tripOrderId,
      so_id: order.so_id,
      payload,
      sync_status: 'pending',
      retry_count: 0,
      created_at: new Date().toISOString(),
    });

    const updatedOrder: OrderWithPayment = {
      ...order,
      status: 'PARTIAL',
      delivery_status: 'PARTIAL',
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
      orders: s.orders.map((o) => Number(o.trip_order_id) === tripOrderId ? updatedOrder : o),
    }));
  },

  submitFailedDelivery: async (tripOrderId, reason, photoData) => {
    const order = get().orders.find((o) => Number(o.trip_order_id) === tripOrderId);
    const { currentTrip } = get();
    if (!order || !currentTrip) throw new Error('Order not found');

    // Phase 4: capture GPS (kể cả khi fail delivery, vẫn ghi nhận vị trí tài xế)
    const gps = await get()._captureGPSPayload(order);

    const payload = {
      trip_order_id: tripOrderId,
      so_id: order.so_id,
      partner_id: order.partner_id,
      delivery_status: 'FAILED' as const,
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

    if (navigator.onLine) {
      try {
        await api.post('/api/sales/pod', payload);
      } catch {
        // queue offline
      }
    }

    await savePendingPOD({
      trip_order_id: tripOrderId,
      so_id: order.so_id,
      payload,
      sync_status: 'pending',
      retry_count: 0,
      created_at: new Date().toISOString(),
    });

    const updatedOrder: OrderWithPayment = {
      ...order,
      status: 'FAILED',
      delivery_status: 'FAILED',
      _failureReason: reason,
    };

    set((s) => ({
      orders: s.orders.map((o) => Number(o.trip_order_id) === tripOrderId ? updatedOrder : o),
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
    const totalOldDebt =
      orders.reduce((sum, o) => sum + (o._payment?.oldDebtCash ?? 0) + (o._payment?.oldDebtTransfer ?? 0), 0);

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
      total_returned_qty: 0,
    };

    if (navigator.onLine) {
      try {
        await api.put(`/api/sales/trips/${currentTrip.trip_id}/complete`);
      } catch {
        // queued offline
      }
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
