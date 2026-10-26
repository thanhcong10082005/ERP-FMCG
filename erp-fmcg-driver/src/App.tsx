// ============================================================
// App.tsx — Router + global providers
// ============================================================

import React, { useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { useDriverStore } from './store/driverStore';
import { getStoredToken, getStoredUser } from './services/authService';
import { syncManager } from './services/syncQueue';

// Screens
import { LoginScreen } from './screens/LoginScreen';
import { HomeScreen } from './screens/HomeScreen';
import { DeliveryListScreen } from './screens/DeliveryListScreen';
import { DeliveryCoreScreen } from './screens/DeliveryCoreScreen';
import { PartialDeliveryScreen } from './screens/PartialDeliveryScreen';
import { FailDeliveryScreen } from './screens/FailDeliveryScreen';
import { PaymentScreen } from './screens/PaymentScreen';
import { EODScreen } from './screens/EODScreen';
import './index.css';

// ── Auth Guard ──────────────────────────────────────────────
function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user } = useDriverStore();
  const token = getStoredToken();

  if (!user && !token) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
}

// ── App ────────────────────────────────────────────────────
export default function App() {
  const { setUser, setOnline, setPendingSyncCount } = useDriverStore();

  // Restore session on boot
  useEffect(() => {
    const token = getStoredToken();
    const user = getStoredUser();

    if (token && user) {
      setUser(user, token);
      // Start sync manager
      syncManager.start();
    }

    // Network listeners
    const handleOnline = () => setOnline(true);
    const handleOffline = () => setOnline(false);
    window.addEventListener('online', handleOnline);
    window.addEventListener('offline', handleOffline);

    // Poll pending sync count
    const interval = setInterval(async () => {
      const count = await syncManager.getUnsyncedCount();
      setPendingSyncCount(count);
    }, 5000);

    return () => {
      window.removeEventListener('online', handleOnline);
      window.removeEventListener('offline', handleOffline);
      clearInterval(interval);
    };
  }, []);

  return (
    <BrowserRouter>
      <Routes>
        {/* Public */}
        <Route path="/login" element={<LoginScreen />} />

        {/* Protected */}
        <Route
          path="/home"
          element={
            <RequireAuth>
              <HomeScreen />
            </RequireAuth>
          }
        />

        <Route
          path="/deliveries"
          element={
            <RequireAuth>
              <DeliveryListScreen />
            </RequireAuth>
          }
        />

        {/* Delivery core flow */}
        <Route
          path="/deliver/:tripId/:tripOrderId"
          element={
            <RequireAuth>
              <DeliveryCoreScreen />
            </RequireAuth>
          }
        />

        <Route
          path="/deliver/:tripId/partial/:tripOrderId"
          element={
            <RequireAuth>
              <PartialDeliveryScreen />
            </RequireAuth>
          }
        />

        <Route
          path="/deliver/:tripId/fail/:tripOrderId"
          element={
            <RequireAuth>
              <FailDeliveryScreen />
            </RequireAuth>
          }
        />

        <Route
          path="/deliver/:tripId/pay/:tripOrderId"
          element={
            <RequireAuth>
              <PaymentScreen />
            </RequireAuth>
          }
        />

        {/* EOD */}
        <Route
          path="/eod"
          element={
            <RequireAuth>
              <EODScreen />
            </RequireAuth>
          }
        />

        {/* Default redirect */}
        <Route path="/" element={<Navigate to="/login" replace />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    </BrowserRouter>
  );
}
