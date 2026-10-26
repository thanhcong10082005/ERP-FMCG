// ============================================================
// HomeScreen — Step 1b: Trip assignment + Xuất phát
// ============================================================

import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useDriverStore } from '../store/driverStore';
import { formatCurrency, formatDate } from '../utils/formatters';
import api from '../api/apiClient';
import type { DeliveryTrip } from '../types';
import { LoadingOverlay } from '../components/LoadingOverlay';

const SESSION_TRIP_KEY = 'driver_active_trip_id';

export const HomeScreen: React.FC = () => {
  const navigate = useNavigate();
  const { user, currentTrip, loadCurrentTrip, startTrip, isOnline, pendingSyncCount } = useDriverStore();
  const [assignedTrip, setAssignedTrip] = useState<DeliveryTrip | null>(null);
  const [loading, setLoading] = useState(true);
  const [starting, setStarting] = useState(false);

  useEffect(() => {
    if (!user) {
      navigate('/login', { replace: true });
      return;
    }
    loadAssignedTrip();
  }, [user]);

  const loadAssignedTrip = async () => {
    setLoading(true);
    try {
      // Fetch trip assigned to this driver with DELIVERING status
      const response = await api.get('/api/sales/trips', {
        params: { driver_id: user?.user_id, status: 'DELIVERING' },
      });
      const trips: DeliveryTrip[] = response.data;
      if (trips.length > 0) {
        const trip = trips[0];
        setAssignedTrip(trip);
        sessionStorage.setItem(SESSION_TRIP_KEY, String(trip.trip_id));
        await loadCurrentTrip(trip.trip_id);
      } else {
        // No active trip — try PREPARING
        const prepRes = await api.get('/api/sales/trips', {
          params: { driver_id: user?.user_id, status: 'PREPARING' },
        });
        const prepTrips: DeliveryTrip[] = prepRes.data;
        if (prepTrips.length > 0) {
          setAssignedTrip(prepTrips[0]);
          sessionStorage.setItem(SESSION_TRIP_KEY, String(prepTrips[0].trip_id));
        }
      }
    } catch {
      // Offline — will use cached trip
    } finally {
      setLoading(false);
    }
  };

  const handleStartTrip = async () => {
    if (!assignedTrip) return;
    setStarting(true);
    try {
      await startTrip(assignedTrip.trip_id);
      navigate('/deliveries', { replace: true });
    } catch {
      setStarting(false);
    }
  };

  const handleLogout = async () => {
    const { logout } = useDriverStore.getState();
    await logout();
    sessionStorage.removeItem(SESSION_TRIP_KEY);
    navigate('/login', { replace: true });
  };

  if (loading) return <LoadingOverlay message="Đang tải chuyến xe..." />;

  return (
    <div className="screen">
      {/* Header */}
      <header className="header safe-top">
        <div>
          <h1 className="header-title">Driver App</h1>
          <p className="text-sm text-blue-200">{user?.full_name} · {user?.phone}</p>
        </div>
        <button
          type="button"
          onClick={handleLogout}
          className="ml-auto text-sm text-blue-200 hover:text-white"
        >
          Đăng xuất
        </button>
      </header>

      {/* Offline banner */}
      {!isOnline && (
        <div className="offline-banner">
          Đang offline — Dữ liệu sẽ đồng bộ khi có mạng
          {pendingSyncCount > 0 && ` (${pendingSyncCount} chờ sync)`}
        </div>
      )}

      {/* Main content */}
      <div className="flex-1 p-4">
        {!assignedTrip ? (
          <div className="text-center py-12">
            <div className="w-24 h-24 bg-slate-100 rounded-full flex items-center justify-center mx-auto mb-4">
              <NoTripIcon />
            </div>
            <h2 className="text-lg font-semibold text-text-primary mb-2">
              Không có chuyến xe được gán
            </h2>
            <p className="text-text-secondary text-sm">
              Vui lòng liên hệ Dispatcher để được phân công chuyến giao hàng.
            </p>
            <button
              type="button"
              className="btn btn-outline mt-6"
              onClick={loadAssignedTrip}
            >
              Tải lại
            </button>
          </div>
        ) : currentTrip?.status === 'DELIVERING' ? (
          /* Already delivering — go to delivery list */
          <div className="space-y-4">
            <div className="card p-4 border-l-4 border-l-success">
              <p className="text-sm text-success font-semibold">Chuyến đang giao</p>
              <h2 className="text-xl font-bold text-text-primary mt-1">{currentTrip.trip_number}</h2>
              <p className="text-sm text-text-secondary mt-1">
                {currentTrip.orders?.filter(o => o.status !== 'PENDING').length} / {currentTrip.orders?.length} điểm giao hoàn thành
              </p>
            </div>
            <button
              type="button"
              className="btn btn-primary w-full text-lg py-4 rounded-2xl"
              onClick={() => navigate('/deliveries')}
            >
              Tiếp tục giao hàng
            </button>
          </div>
        ) : (
          /* PREPARING — show trip info + start button */
          <div className="space-y-4">
            {/* Trip info card */}
            <div className="card">
              <div className="card-section border-b border-border">
                <p className="text-xs text-text-secondary uppercase tracking-wide">Chuyến xe được gán</p>
                <h2 className="text-2xl font-extrabold text-text-primary mt-1">{assignedTrip.trip_number}</h2>
                <p className="text-sm text-text-secondary mt-1">
                  {formatDate(assignedTrip.trip_date)} · {assignedTrip.warehouse_name}
                </p>
              </div>

              <div className="card-section space-y-3">
                <div className="flex justify-between">
                  <span className="text-text-secondary text-sm">Tổng điểm giao</span>
                  <span className="font-semibold">{assignedTrip.total_orders} điểm</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-text-secondary text-sm">Tổng tiền dự kiến</span>
                  <span className="font-bold text-lg text-primary">
                    {formatCurrency(assignedTrip.total_amount)}
                  </span>
                </div>
                {assignedTrip.vehicle_plate && (
                  <div className="flex justify-between">
                    <span className="text-text-secondary text-sm">Biển số xe</span>
                    <span className="font-semibold">{assignedTrip.vehicle_plate}</span>
                  </div>
                )}
              </div>
            </div>

            {/* Start button */}
            <button
              type="button"
              className="btn btn-success w-full text-xl py-6 rounded-2xl shadow-lg shadow-success/30"
              onClick={handleStartTrip}
              disabled={starting}
            >
              {starting ? (
                <span className="flex items-center justify-center gap-2">
                  <span className="w-6 h-6 border-3 border-white border-t-transparent rounded-full animate-spin" />
                  Đang xuất phát...
                </span>
              ) : (
                '🚚  XUẤT PHÁT'
              )}
            </button>

            {/* Offline hint */}
            {!isOnline && (
              <div className="bg-yellow-50 border border-yellow-200 rounded-xl p-3">
                <p className="text-xs text-yellow-700">
                  Bạn đang offline. Dữ liệu Master đã được lưu sẵn trên máy.
                  Thao tác sẽ được đồng bộ khi có mạng.
                </p>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};

const NoTripIcon = () => (
  <svg className="w-12 h-12 text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5}
      d="M8.25 18.75a1.5 1.5 0 01-3 0m3 0a1.5 1.5 0 00-3 0m3 0h6m-9 0H3.375a1.125 1.125 0 01-1.125-1.125V14.25m17.25 4.5a1.5 1.5 0 01-3 0m3 0a1.5 1.5 0 00-3 0m3 0h1.125c.621 0 1.129-.504 1.09-1.124a17.902 17.902 0 00-3.213-9.193 2.056 2.056 0 00-1.58-.86H14.25M16.5 18.75h-2.25m0-11.177v-.958c0-.568-.422-1.048-.987-1.106a48.554 48.554 0 00-10.026 0 1.106 1.106 0 00-.987 1.106v7.635m12-6.677v6.677m0 4.5v-4.5m0 0h-12" />
  </svg>
);
