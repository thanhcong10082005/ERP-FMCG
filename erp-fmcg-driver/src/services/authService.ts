// ============================================================
// Auth Service — Login + token management
// ------------------------------------------------------------
// user_id là string (Guid), không phải number.
// master data lấy từ endpoint chuyên biệt của
//   Backend C#: /api/driver/master/partners và
//   /api/driver/master/products (trả đúng shape snake_case,
//   id dạng string). Endpoint cũ /api/partners dùng shape khác.
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
  // identifier: user_id (GUID string) OR email OR phone
  let body: Record<string, string>;

  if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(identifier)) {
    body = { user_id: identifier };
  } else if (identifier.includes('@')) {
    body = { email: identifier };
  } else {
    body = { phone: identifier };
  }

  const response = await api.post<AuthResponse>('/api/auth/dev-login', body);
  // Backend trả trực tiếp { access_token, user } — hỗ trợ cả dạng
  // bọc { success, data } để tương thích ngược.
  const raw = response.data as any;
  const data: AuthResponse =
    raw && raw.success && raw.data ? raw.data : raw;

  localStorage.setItem('driver_token', data.access_token);
  localStorage.setItem('driver_user', JSON.stringify(data.user));

  return data;
}

/**
 * Đồng bộ master data (khách hàng + sản phẩm) về IndexedDB để dùng offline.
 *
 * trước đây gọi `/api/partners?limit=10000` và đọc
 *   `.data` — nhưng Backend C# không có endpoint đó và trả shape
 *   `{ items: [...] }`, nên kết quả LUÔN RỖNG => offline cache không có
 *   gì, tài xế offline không thấy tên khách/sản phẩm.
 *
 *   Nay dùng endpoint chuyên biệt `/api/driver/master/partners` và
 *   `/api/driver/master/products` (shape snake_case, id string) và
 *   `bulkPut` (KHÔNG `bulkAdd`) để không lỗi trùng primary key.
 */
export async function syncMasterData(tenantId?: string): Promise<void> {
  // Ensure DB is open before any query — protects against
  // DatabaseClosedError on second login / page reload after logout.
  await ensureDbOpen();

  const [partnersRes, productsRes] = await Promise.all([
    api.get('/api/driver/master/partners'),
    api.get('/api/driver/master/products'),
  ]);

  const unwrap = (res: any): any[] => {
    if (Array.isArray(res)) return res;
    if (Array.isArray(res?.data)) return res.data;
    if (Array.isArray(res?.data?.data)) return res.data.data;
    if (Array.isArray(res?.items)) return res.items;
    if (Array.isArray(res?.data?.items)) return res.data.items;
    return [];
  };

  const partners = unwrap(partnersRes);
  const products = unwrap(productsRes);

  await db.transaction('rw', [db.partners, db.products], async () => {
    await db.partners.clear();
    await db.products.clear();
    // bulkPut: ghi đè theo primary key, không lỗi "Key already exists".
    if (partners.length) await db.partners.bulkPut(partners as never[]);
    if (products.length) await db.products.bulkPut(products as never[]);
  });

  console.log(
    `[Auth] Master data synced: ${partners.length} partners, ${products.length} products` +
    (tenantId ? ` (tenant=${tenantId})` : ''),
  );
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
