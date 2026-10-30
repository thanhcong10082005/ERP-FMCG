import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';
import path from 'path';

export default defineConfig({
  plugins: [
    react(),
    // ★ PHASE 6C.7 — PWA that (manifest + service worker).
    //   Truoc Phase 6C.7 chi co meta tag `mobile-web-app-capable`:
    //   - KHONG co manifest.json -> Android Chrome KHONG hien "Cai app".
    //   - KHONG co service worker -> mat mang la app trang tron, mat
    //     toan bo queue POD offline (Dexie) vi trang chua kip load.
    //
    //   Chien luoc cache:
    //   - `navigateFallback` -> index.html: mo app offline van vao duoc
    //     (React Router xu ly sau).
    //   - `runtimeCaching` cho API GET -> CacheFirst (du lieu master tinh).
    //   - API POST (giao POD) -> KHONG cache (NetworkOnly) vi phai di qua
    //     server de chong double-POD; offline thi queue Dexie lo.
    //   - `globPatterns` loai bo `sw.js`/`workbox-*.js` khoi precache de
    //     tranh vong lap tu cache.
    VitePWA({
      registerType: 'autoUpdate',
      injectRegister: 'auto',
      includeAssets: ['favicon.svg', 'icons/*.png'],
      manifest: {
        name: 'ERP FMCG — Driver App',
        short_name: 'ERP Driver',
        description:
          'Ung dung tai xe giao hang ERP FMCG: chuyen xe, giao hang, ' +
          'thu tien, cong no, doi soat cuoi ngay — hoat dong offline.',
        lang: 'vi',
        dir: 'ltr',
        theme_color: '#1E40AF',
        background_color: '#0F172A',
        display: 'standalone',
        orientation: 'portrait',
        scope: '/',
        start_url: '/',
        categories: ['business', 'productivity'],
        icons: [
          {
            src: 'icons/icon-192.png',
            sizes: '192x192',
            type: 'image/png',
            purpose: 'any',
          },
          {
            src: 'icons/icon-512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'any',
          },
          {
            src: 'icons/icon-512-maskable.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,svg,png,ico,woff2}'],
        navigateFallback: 'index.html',
        navigateFallbackDenylist: [/^\/api\//],
        cleanupOutdatedCaches: true,
        clientsClaim: true,
        skipWaiting: true,
        runtimeCaching: [
          {
            // Master data (products / partners) — thay doi hiem -> cache lau
            urlPattern: ({ url }) =>
              url.pathname.startsWith('/api/products') ||
              url.pathname.startsWith('/api/partners'),
            handler: 'StaleWhileRevalidate',
            options: {
              cacheName: 'erp-fmcg-master-data',
              expiration: { maxEntries: 200, maxAgeSeconds: 60 * 60 * 24 },
              cacheableResponse: { statuses: [0, 200] },
            },
          },
          {
            // Don hang cua chuyen — can duoi moi hon
            urlPattern: ({ url }) => url.pathname.startsWith('/api/sales/trips'),
            handler: 'NetworkFirst',
            options: {
              cacheName: 'erp-fmcg-trips',
              networkTimeoutSeconds: 5,
              expiration: { maxEntries: 50, maxAgeSeconds: 60 * 60 * 12 },
              cacheableResponse: { statuses: [0, 200] },
            },
          },
          {
            // Font/icon tai nguyen
            urlPattern: ({ request }) =>
              request.destination === 'image' ||
              request.destination === 'font',
            handler: 'CacheFirst',
            options: {
              cacheName: 'erp-fmcg-static-assets',
              expiration: { maxEntries: 100, maxAgeSeconds: 60 * 60 * 24 * 30 },
            },
          },
        ],
      },
      devOptions: {
        enabled: false,
        type: 'module',
      },
    }),
  ],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 3002,
    proxy: {
      '/api': {
        // Backend C# (.NET 8) chay port 5000.
        //   Truoc day troi 3001 (NestJS cu, khong con trong Pack).
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
});
