// ============================================================
// Sync Queue — Background sync với exponential backoff
// ============================================================

import api from '../api/apiClient';
import {
  db,
  getPendingPods,
  markPodSyncing,
  markPodSynced,
  markPodFailed,
  ensureDbOpen,
} from './offlineDB';
import type { PendingPOD } from '../types';

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
      const pods = await getPendingPods();
      for (const pod of pods) {
        if (pod.retry_count >= MAX_RETRIES) continue;
        await this.syncPOD(pod);
      }
    } finally {
      this.isSyncing = false;
    }
  }

  private async syncPOD(pod: PendingPOD): Promise<void> {
    const podId = pod.id!;
    await markPodSyncing(podId);

    try {
      const response = await api.post('/api/sales/pod', pod.payload);
      await markPodSynced(podId);

      // Log to syncLog
      await db.syncLog.add({
        entity: 'delivery_pod',
        entity_id: response.data.pod_id || podId,
        action: 'create',
        payload: JSON.stringify(pod.payload),
        synced_at: new Date().toISOString(),
      });

      console.log(`[SyncManager] POD ${podId} synced successfully`);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Unknown error';
      await markPodFailed(podId, msg);
      console.warn(`[SyncManager] POD ${podId} sync failed: ${msg}`);
    }
  }

  async getUnsyncedCount(): Promise<number> {
    return db.pendingPods.where('sync_status').anyOf(['pending', 'failed']).count();
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
