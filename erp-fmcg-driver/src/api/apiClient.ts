// ============================================================
// API Client — Axios instance với JWT interceptors
// ============================================================

import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';

/// <reference types="vite/client" />

/**
 * URL backend, ví dụ:
 *   http://localhost:5000        ← Backend C# (.NET 8) trong Pack
 *   http://localhost:5000/api
 *   https://erp-fmcg-backend.onrender.com/api
 *
 * backend trong Pack/erp-fmcg-backend-csharp (ASP.NET Core
 *   .NET 8) cung cấp đầy đủ các route app cần:
 *   /api/auth/dev-login, /api/sales/trips, /api/sales/pod,
 *   /api/driver/master/partners, /api/driver/master/products.
 *
 *   Trước đây default là http://localhost:3001 (NestJS cũ đã bị loại khỏi
 *   Pack) => mọi request fail khi chạy đúng stack trong Pack.
 */
const RAW_BASE_URL: string =
  import.meta.env.VITE_API_URL || 'http://localhost:5000';

/**
 * Chuẩn hoá base URL: bỏ dấu "/" ở cuối và bỏ hậu tố "/api" (nếu có).
 *
 * Lý do: mọi lời gọi trong app đều đã viết đầy đủ "/api/..." (ví dụ
 * `api.get('/api/products')`). Nếu baseURL cũng có "/api" thì request sẽ
 * thành "/api/api/products" và backend trả 404.
 */
function normalizeBaseUrl(url: string): string {
  let base = url.trim().replace(/\/+$/, '');
  base = base.replace(/\/api$/i, '');
  return base;
}

export const BASE_URL: string = normalizeBaseUrl(RAW_BASE_URL);

const api = axios.create({
  baseURL: BASE_URL,
  timeout: 15000,
  headers: { 'Content-Type': 'application/json' },
});

// Request interceptor: attach JWT
api.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = localStorage.getItem('driver_token');
    if (token && config.headers) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (err) => Promise.reject(err),
);

// Response interceptor: auto-refresh or logout
api.interceptors.response.use(
  (res) => res,
  async (err: AxiosError) => {
    if (err.response?.status === 401) {
      localStorage.removeItem('driver_token');
      localStorage.removeItem('driver_user');
      window.location.href = '/login';
    }
    return Promise.reject(err);
  },
);

export default api;
