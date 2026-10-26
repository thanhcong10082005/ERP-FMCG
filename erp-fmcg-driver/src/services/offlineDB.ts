// ============================================================
// Offline Database — Dexie.js (SQLite-like)
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
  entity_id: number;
  action: 'create' | 'update';
  payload: string;
  synced_at: string;
}

export class DriverDB extends Dexie {
  partners!: Table<Partner, number>;
  products!: Table<Product, number>;
  trips!: Table<DeliveryTrip, number>;
  orders!: Table<TripOrder, number>;
  pendingPods!: Table<PendingPOD, number>;
  syncLog!: Table<SyncLog, number>;

  constructor() {
    super('DriverAppDB');

    this.version(1).stores({
      partners: 'partner_id, tenant_id, partner_code, route_code',
      products: 'product_id, tenant_id, sku, category_id',
      trips: 'trip_id, tenant_id, driver_id, status, trip_date',
      orders: 'trip_order_id, trip_id, so_id, status, partner_id',
      pendingPods: '++id, trip_order_id, sync_status, created_at',
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
}

export async function getTripWithOrders(tripId: number): Promise<DeliveryTrip | undefined> {
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
  tripOrderId: number,
  status: 'DELIVERED' | 'PARTIAL' | 'FAILED' | 'PENDING' | 'CANCELLED',
  extra?: Partial<TripOrder>,
): Promise<void> {
  await db.orders.update(tripOrderId, { status, ...extra });
}

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
