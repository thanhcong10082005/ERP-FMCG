// ============================================================
// PWA Status Banner (6C.7)
// ------------------------------------------------------------
// Hien thi trang thai PWA cho tai xe:
//   1. "Dang cap nhat" — co version moi da tai, can reload.
//   2. "Offline" — du lieu se duoc gui khi co mang.
//   3. Nut "Cai app" (beforeinstallprompt).
//
// Vi sao can:
//   Phase 5C.2 sua loi double-POD bang cach sinh `client_pod_id` 1 lan
//   va queue offline. Nhung neu app KHONG co service worker, mo app khi
//   mat mang = trang trang -> tai xe KHONG giao duoc hang trong vung
//   phu yeu (vung nong thon, khu cong nghiep).
// ============================================================

import { useEffect, useState } from 'react';
import { useRegisterSW } from 'virtual:pwa-register/react';

type BeforeInstallPromptEvent = Event & {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>;
};

export function PwaStatusBanner() {
  const [online, setOnline] = useState<boolean>(
    typeof navigator === 'undefined' ? true : navigator.onLine,
  );
  const [installPrompt, setInstallPrompt] =
    useState<BeforeInstallPromptEvent | null>(null);
  const [dismissed, setDismissed] = useState(false);

  const {
    needRefresh: [needRefresh],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(_swUrl, registration) {
      // Kiem tra cap nhat moi 1 gio (app tai xe mo ca ngay).
      if (registration) {
        setInterval(() => {
          void registration.update();
        }, 60 * 60 * 1000);
      }
    },
  });

  useEffect(() => {
    const goOnline = () => setOnline(true);
    const goOffline = () => setOnline(false);
    window.addEventListener('online', goOnline);
    window.addEventListener('offline', goOffline);
    return () => {
      window.removeEventListener('online', goOnline);
      window.removeEventListener('offline', goOffline);
    };
  }, []);

  useEffect(() => {
    const handler = (e: Event) => {
      e.preventDefault();
      setInstallPrompt(e as BeforeInstallPromptEvent);
    };
    window.addEventListener('beforeinstallprompt', handler);
    return () => window.removeEventListener('beforeinstallprompt', handler);
  }, []);

  const install = async () => {
    if (!installPrompt) return;
    await installPrompt.prompt();
    const choice = await installPrompt.userChoice;
    if (choice.outcome === 'accepted') {
      setInstallPrompt(null);
    }
  };

  if (needRefresh) {
    return (
      <div
        data-testid="pwa-update-banner"
        className="fixed bottom-0 left-0 right-0 z-50 flex items-center justify-between gap-3 bg-amber-500 px-4 py-2 text-sm font-semibold text-white shadow-lg"
      >
        <span>Da co ban cap nhat moi. Cap nhat de tranh loi khi giao hang.</span>
        <button
          type="button"
          onClick={() => void updateServiceWorker(true)}
          className="rounded bg-white px-3 py-1 font-bold text-amber-700"
        >
          Cap nhat
        </button>
      </div>
    );
  }

  if (!online) {
    return (
      <div
        data-testid="pwa-offline-banner"
        className="fixed bottom-0 left-0 right-0 z-50 bg-slate-800 px-4 py-2 text-center text-sm font-semibold text-amber-300 shadow-lg"
      >
        Dang offline — du lieu se duoc gui tu dong khi co mang
      </div>
    );
  }

  if (installPrompt && !dismissed) {
    return (
      <div
        data-testid="pwa-install-banner"
        className="fixed bottom-0 left-0 right-0 z-50 flex items-center justify-between gap-3 bg-blue-700 px-4 py-2 text-sm font-semibold text-white shadow-lg"
      >
        <span>Cai app de dung offline khi mat mang.</span>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => setDismissed(true)}
            className="rounded px-3 py-1 font-medium text-blue-100"
          >
            De sau
          </button>
          <button
            type="button"
            onClick={() => void install()}
            className="rounded bg-white px-3 py-1 font-bold text-blue-700"
          >
            Cai app
          </button>
        </div>
      </div>
    );
  }

  return null;
}
