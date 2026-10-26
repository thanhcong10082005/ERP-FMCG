// ============================================================
// Auth Service — Login + token management
// ============================================================

import api from '../api/apiClient';
import { db, clearAllData, ensureDbOpen } from './offlineDB';
import { syncManager } from './syncQueue';
import type { AuthResponse, User } from '../types';

export function getStoredToken(): string | null {
  return localStorage.getItem('driver_token');
}

export function getStoredUser(): User | null {
  const raw = localStorage.getItem('driver_user');
  if (!raw) return null;
  try {
    return JSON.parse(raw) as User;
  } catch {
    return null;
  }
}

export async function devLogin(identifier: string): Promise<AuthResponse> {
  // identifier: user_id number string OR phone number
  // Try as user_id first (most reliable for driver app)
  let body: Record<string, string | number>;

  if (/^\d+$/.test(identifier)) {
    body = { user_id: parseInt(identifier, 10) };
  } else if (identifier.includes('@')) {
    body = { email: identifier };
  } else {
    // Default: treat as phone
    body = { phone: identifier };
  }

  const response = await api.post<AuthResponse>('/api/auth/dev-login', body);
  // Backend wraps response in { success, data: { access_token, user } }
  const raw = response.data;
  const data: AuthResponse = (raw as any).success && (raw as any).data
    ? (raw as any).data
    : raw;

  localStorage.setItem('driver_token', data.access_token);
  localStorage.setItem('driver_user', JSON.stringify(data.user));

  return data;
}

export async function syncMasterData(tenantId: string): Promise<void> {
  // Ensure DB is open before any query — protects against
  // DatabaseClosedError on second login / page reload after logout.
  await ensureDbOpen();

  const [partnersRes, productsRes] = await Promise.all([
    api.get(`/api/partners?limit=10000&tenant_id=${tenantId}`),
    api.get(`/api/products?limit=10000&tenant_id=${tenantId}`),
  ]);

  await db.transaction('rw', [db.partners, db.products], async () => {
    await db.partners.clear();
    await db.products.clear();
    const partners = Array.isArray(partnersRes.data)
      ? partnersRes.data
      : (partnersRes.data as { data?: unknown[] }).data || [];
    const products = Array.isArray(productsRes.data)
      ? productsRes.data
      : (productsRes.data as { data?: unknown[] }).data || [];
    if (partners.length) await db.partners.bulkAdd(partners as never[]);
    if (products.length) await db.products.bulkAdd(products as never[]);
  });

  console.log(`[Auth] Master data synced`);
}

export async function logout(): Promise<void> {
  syncManager.stop();
  localStorage.removeItem('driver_token');
  localStorage.removeItem('driver_user');
  try {
    await clearAllData();
  } catch (err) {
    console.warn('[Auth] clearAllData failed (non-fatal):', err);
  }
}
