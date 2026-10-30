// ============================================================
// Sync Queue — Background sync với exponential backoff
// ------------------------------------------------------------
// KHÔNG ĐƯỢC SINH LẠI client_pod_id.
//   `pod.payload.client_pod_id` đã được sinh MỘT LẦN lúc tài xế
//   bấm "Giao hàng". Hàm này PHẢI gửi lại đúng id đó.
//
//   Nếu sinh id mới ở đây -> mỗi lần retry là 1 POD khác nhau ->
//   server tạo N POD cho 1 lần giao hàng -> ĐÚNG LỖI double-POD
//
// Idempotency server: UNIQUE index UQ_delivery_pod_client.
//   Nếu client vẫn lỗi (gửi trùng), server trả 200 với created=false
//   -> client coi như thành công, KHÔNG retry vô ích.
// ============================================================

import api from '../api/apiClient';
import {
  db,
  getPendingPods,
  markPodSyncing,
  markPodSynced,
  markPodFailed,
  ensureDbOpen,
  getPendingTripActions,
  markTripActionSyncing,
  markTripActionSynced,
  markTripActionFailed,
} from './offlineDB';
import type { PendingPOD, PodResponse } from '../types';

const SYNC_INTERVAL_MS = 30_000;
const MAX_RETRIES = 5;

class SyncManager {
  private timer: ReturnType<typeof setInterval> | null = null;
  private isSyncing = false;

  start(): void {
    if (this.timer) return;
    this.timer = setInterval(() => this.syncAll(), SYNC_INTERVAL_MS);
    this.syncAll(); // run immediately
  }

  stop(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  async syncAll(): Promise<void> {
    if (this.isSyncing || !navigator.onLine) return;
    await ensureDbOpen();
    this.isSyncing = true;

    try {
      // sync POD trước, trip action sau. POD là dữ liệu
      //   nghiệp vụ giao hàng; trip action (hoàn thành chuyến) chỉ có
      //   nghĩa sau khi mọi POD của chuyến đã lên server.
      const pods = await getPendingPods();
      for (const pod of pods) {
        if ((pod.retry_count ?? 0) >= MAX_RETRIES) {
          // trước đây `continue` vĩnh viễn => POD kẹt
          //   trong queue mà pendingSyncCount không bao giờ về 0.
          //   Giờ đánh dấu failed + ghi lý do để UI cảnh báo.
          if (pod.sync_status !== 'failed') {
            await markPodFailed(
              pod.id!,
              `Vượt giới hạn ${MAX_RETRIES} lần thử — cần liên hệ quản trị`,
            );
          }
          continue;
        }
        await this.syncPOD(pod);
      }

      // gửi lệnh "hoàn thành chuyến" đã queue.
      const tripActions = await getPendingTripActions();
      for (const action of tripActions) {
        if ((action.retry_count ?? 0) >= MAX_RETRIES) {
          if (action.sync_status !== 'failed') {
            await markTripActionFailed(
              action.id!,
              `Vượt giới hạn ${MAX_RETRIES} lần thử — cần liên hệ quản trị`,
            );
          }
          continue;
        }
        await this.syncTripAction(action);
      }
    } finally {
      this.isSyncing = false;
    }
  }

  /**
   * gửi lệnh "hoàn thành chuyến" lên server.
   */
  private async syncTripAction(action: {
    id?: number;
    trip_id: string;
    action: 'COMPLETE_TRIP';
  }): Promise<void> {
    const id = action.id!;
    await markTripActionSyncing(id);

    try {
      await api.put(`/api/sales/trips/${action.trip_id}/complete`);
      await markTripActionSynced(id);

      await db.syncLog.add({
        entity: 'delivery_trip',
        entity_id: action.trip_id,
        action: 'update',
        payload: JSON.stringify(action),
        synced_at: new Date().toISOString(),
      });

      console.log(`[SyncManager] COMPLETE_TRIP synced cho trip ${action.trip_id}.`);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Unknown error';
      await markTripActionFailed(id, msg);
      console.warn(`[SyncManager] COMPLETE_TRIP trip ${action.trip_id} failed: ${msg}`);
    }
  }

  private async syncPOD(pod: PendingPOD): Promise<void> {
    const podId = pod.id!;
    await markPodSyncing(podId);

    // dùng lại client_pod_id đã sinh sẵn trong payload.
    const payload = pod.payload;
    if (!payload?.client_pod_id) {
      console.error(
        `[SyncManager] POD ${podId} thiếu client_pod_id — không thể đảm bảo idempotency. ` +
        'Bỏ qua (không gửi).',
      );
      await markPodFailed(podId, 'Thiếu client_pod_id');
      return;
    }

    try {
      const response = await api.post<PodResponse>('/api/sales/pod', payload);
      await markPodSynced(podId);

      // Log to syncLog
      await db.syncLog.add({
        entity: 'delivery_pod',
        entity_id: response.data?.pod_id ?? String(podId),
        action: 'create',
        payload: JSON.stringify(payload),
        synced_at: new Date().toISOString(),
      });

      console.log(
        `[SyncManager] POD ${podId} synced (client_pod_id=${payload.client_pod_id}, ` +
        `created=${response.data?.created})`,
      );
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Unknown error';
      await markPodFailed(podId, msg);
      console.warn(`[SyncManager] POD ${podId} sync failed: ${msg}`);
    }
  }

  async getUnsyncedCount(): Promise<number> {
    const [pods, actions] = await Promise.all([
      db.pendingPods.where('sync_status').anyOf(['pending', 'failed']).count(),
      db.pendingTripActions.where('sync_status').anyOf(['pending', 'failed']).count(),
    ]);
    return pods + actions;
  }
}

export const syncManager = new SyncManager();

// Connectivity listener
window.addEventListener('online', () => {
  console.log('[Network] Online — triggering sync');
  syncManager.syncAll();
});

window.addEventListener('offline', () => {
  console.log('[Network] Offline — sync paused');
});
