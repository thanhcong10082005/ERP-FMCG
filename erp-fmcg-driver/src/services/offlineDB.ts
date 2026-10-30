// ============================================================
// Offline Database — Dexie.js (SQLite-like)
// ------------------------------------------------------------
// Dexie schema v2: đổi khoá chính từ `number` sang `string`.
//
// Lý do:
//   Backend C# dùng UNIQUEIDENTIFIER (Guid 128-bit). JS `number` chỉ
//   an toàn tới 2^53-1. Nếu giữ `number`, ID Guid bị mất precision ->
//   va chạm (collision) giữa các bản ghi khác nhau -> ghi POD nhầm
//   đơn hàng.
//
//   Dexie v2 dùng `++id` cho `pendingPods` (id local tự tăng) và các
//   bảng còn lại dùng khoá chính chuỗi (UUID từ server).
// ============================================================

import Dexie, { type Table } from 'dexie';
import type {
  Partner,
  Product,
  DeliveryTrip,
  TripOrder,
  PendingPOD,
  SyncStatus,
} from '../types';

export interface SyncLog {
  id?: number;
  entity: string;
  entity_id: string;
  action: 'create' | 'update';
  payload: string;
  synced_at: string;
}

/**
 * Hành động cần gửi lên server khi offline.
 * Hiện chỉ có `COMPLETE_TRIP` (hoàn thành chuyến xe).
 *
 * `completeTrip()` gọi API chỉ khi online; offline thì
 * trạng thái "đã kết thúc chuyến" bị MẤT HOÀN TOÀN => server không bao
 * giờ biết chuyến đó đã xong, công nợ chưa chốt, báo cáo EOD sai.
 */
export interface PendingTripAction {
  /** Dexie auto-increment (id local tự tăng). */
  id?: number;
  trip_id: string;
  action: 'COMPLETE_TRIP';
  sync_status: SyncStatus;
  retry_count: number;
  created_at: string;
  error_message?: string;
}

export class DriverDB extends Dexie {
  partners!: Table<Partner, string>;
  products!: Table<Product, string>;
  trips!: Table<DeliveryTrip, string>;
  orders!: Table<TripOrder, string>;
  pendingPods!: Table<PendingPOD, number>;
  pendingTripActions!: Table<PendingTripAction, number>;
  syncLog!: Table<SyncLog, number>;

  constructor() {
    super('DriverAppDB');

    // ── v1: schema gốc (ID là number) ──────────────────────────
    this.version(1).stores({
      partners: 'partner_id, tenant_id, partner_code, route_code',
      products: 'product_id, tenant_id, sku, category_id',
      trips: 'trip_id, tenant_id, driver_id, status, trip_date',
      orders: 'trip_order_id, trip_id, so_id, status, partner_id',
      pendingPods: '++id, trip_order_id, sync_status, created_at',
      syncLog: '++id, entity, entity_id, synced_at',
    });

    // ── ID chuyển sang string (UUID) ──────────
    //   Dexie KHÔNG đổi được khoá chính của bảng cũ. Cách duy nhất
    //   an toàn: xoá & tạo lại các bảng chứa ID server.
    //   Dữ liệu offline cache có thể tải lại từ server nên KHÔNG mất
    //   dữ liệu nghiệp vụ nào (chỉ mất cache, tự sync lại).
    this.version(2)
      .stores({
        partners: 'partner_id, tenant_id, partner_code, route_code',
        products: 'product_id, tenant_id, sku, category_id',
        trips: 'trip_id, tenant_id, driver_id, status, trip_date',
        orders: 'trip_order_id, trip_id, so_id, status, partner_id',
        pendingPods: '++id, trip_order_id, sync_status, created_at',
        syncLog: '++id, entity, entity_id, synced_at',
      })
      .upgrade(async (tx) => {
        // Xoá cache cũ (ID number không dùng được với schema string).
        // pendingPods & syncLog GIỮ NGUYÊN — chứa POD chưa sync, mất
        // đi là mất phiếu giao hàng chưa gửi lên server.
        await tx.table('partners').clear();
        await tx.table('products').clear();
        await tx.table('trips').clear();
        await tx.table('orders').clear();
        console.log(
          '[DB] Dexie v2: da xoa cache ID-number (partners/products/trips/orders). ' +
          'pendingPods & syncLog giu nguyen.',
        );
      });

    // ── v3: thêm bảng queue hành động chuyến xe ──
    //   `pendingTripActions` giữ lệnh "hoàn thành chuyến" khi offline.
    this.version(3)
      .stores({
        partners: 'partner_id, tenant_id, partner_code, route_code',
        products: 'product_id, tenant_id, sku, category_id',
        trips: 'trip_id, tenant_id, driver_id, status, trip_date',
        orders: 'trip_order_id, trip_id, so_id, status, partner_id',
        pendingPods: '++id, trip_order_id, sync_status, created_at',
        pendingTripActions: '++id, trip_id, action, sync_status, created_at',
        syncLog: '++id, entity, entity_id, synced_at',
      });
  }
}

export const db = new DriverDB();

/**
 * Ensure database is open. Dexie's `db` instance is a singleton — once
 * closed (e.g. via `db.delete()` during logout, or by the browser after
 * a version downgrade / storage pressure) any subsequent query throws
 * `DatabaseClosedError: Database has been closed`. Call this helper
 * before any DB-touching flow that might run on a fresh page load.
 */
export async function ensureDbOpen(): Promise<void> {
  if (!db.isOpen()) {
    await db.open();
  }
}

// ============================================================
// Helper functions
// ============================================================

export async function clearAllData(): Promise<void> {
  await db.transaction('rw',
    [
      db.partners,
      db.products,
      db.trips,
      db.orders,
      db.pendingPods,
      db.pendingTripActions,
      db.syncLog,
    ],
    async () => {
      await db.partners.clear();
      await db.products.clear();
      await db.trips.clear();
      await db.orders.clear();
      await db.pendingPods.clear();
      await db.pendingTripActions.clear();
      await db.syncLog.clear();
    },
  );
}

export async function getTripWithOrders(tripId: string): Promise<DeliveryTrip | undefined> {
  const trip = await db.trips.get(tripId);
  if (!trip) return undefined;
  const orders = await db.orders.where('trip_id').equals(tripId).sortBy('stop_order');
  return { ...trip, orders };
}

export async function upsertTrip(trip: DeliveryTrip): Promise<void> {
  await db.trips.put(trip);
}

export async function upsertOrders(orders: TripOrder[]): Promise<void> {
  await db.orders.bulkPut(orders);
}

export async function updateOrderStatus(
  tripOrderId: string,
  status: 'DELIVERED' | 'PARTIAL' | 'FAILED' | 'PENDING' | 'CANCELLED',
  extra?: Partial<TripOrder>,
): Promise<void> {
  await db.orders.update(tripOrderId, { status, ...extra });
}

// ============================================================
// Pending POD — quản lý queue offline
// ============================================================

/**
 * Lưu POD vào queue offline.
 *
 * `client_pod_id` NẰM TRONG `payload` và được sinh MỘT
 *   LẦN. Hàm này KHÔNG tự sinh lại — nếu sinh lại ở đây thì mỗi lần
 *   retry sẽ có id khác nhau => server tạo POD trùng.
 */
export async function savePendingPOD(pod: Omit<PendingPOD, 'id'>): Promise<number> {
  return db.pendingPods.add(pod as PendingPOD);
}

export async function getPendingPods(): Promise<PendingPOD[]> {
  return db.pendingPods.where('sync_status').anyOf(['pending', 'failed']).toArray();
}

export async function markPodSyncing(id: number): Promise<void> {
  await db.pendingPods.update(id, { sync_status: 'syncing' as SyncStatus });
}

export async function markPodSynced(id: number): Promise<void> {
  await db.pendingPods.update(id, { sync_status: 'synced' as SyncStatus });
}

export async function markPodFailed(id: number, error: string): Promise<void> {
  const pod = await db.pendingPods.get(id);
  if (!pod) return;
  await db.pendingPods.update(id, {
    sync_status: 'failed' as SyncStatus,
    retry_count: (pod.retry_count || 0) + 1,
    error_message: error,
  });
}

/**
 * tìm POD đã queue theo client_pod_id.
 * Dùng để tránh enqueue trùng cùng 1 lần giao hàng.
 */
export async function findPendingPodByClientId(
  clientPodId: string,
): Promise<PendingPOD | undefined> {
  const all = await db.pendingPods.toArray();
  return all.find((p) => p.payload?.client_pod_id === clientPodId);
}

// ============================================================
// Pending trip actions (hoàn thành chuyến offline)
// ============================================================

export async function savePendingTripAction(
  action: Omit<PendingTripAction, 'id'>,
): Promise<number> {
  return db.pendingTripActions.add(action as PendingTripAction);
}

export async function getPendingTripActions(): Promise<PendingTripAction[]> {
  return db.pendingTripActions
    .where('sync_status')
    .anyOf(['pending', 'failed'])
    .toArray();
}

/** Tránh queue trùng lệnh "hoàn thành chuyến" cho cùng 1 trip. */
export async function findPendingTripAction(
  tripId: string,
  action: PendingTripAction['action'],
): Promise<PendingTripAction | undefined> {
  const all = await db.pendingTripActions.toArray();
  return all.find((a) => a.trip_id === tripId && a.action === action);
}

export async function markTripActionSyncing(id: number): Promise<void> {
  await db.pendingTripActions.update(id, { sync_status: 'syncing' as SyncStatus });
}

export async function markTripActionSynced(id: number): Promise<void> {
  await db.pendingTripActions.update(id, { sync_status: 'synced' as SyncStatus });
}

export async function markTripActionFailed(id: number, error: string): Promise<void> {
  const row = await db.pendingTripActions.get(id);
  if (!row) return;
  await db.pendingTripActions.update(id, {
    sync_status: 'failed' as SyncStatus,
    retry_count: (row.retry_count || 0) + 1,
    error_message: error,
  });
}
