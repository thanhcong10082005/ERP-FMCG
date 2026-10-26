// ============================================================
// DeliveryListScreen — Step 2: Danh sách điểm giao
// ============================================================

import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useDriverStore, type OrderWithPayment } from '../store/driverStore';
import { DeliveryCard } from '../components/DeliveryCard';
import { formatCurrency } from '../utils/formatters';
import { getTripWithOrders, ensureDbOpen } from '../services/offlineDB';
import api from '../api/apiClient';

const SESSION_TRIP_KEY = 'driver_active_trip_id';

export const DeliveryListScreen: React.FC = () => {
  const navigate = useNavigate();
  const { currentTrip, orders, isOnline, completeTrip } = useDriverStore();
  const [bootstrapping, setBootstrapping] = useState(!currentTrip);

  useEffect(() => {
    if (currentTrip) {
      setBootstrapping(false);
      return;
    }
    const tripIdStr = sessionStorage.getItem(SESSION_TRIP_KEY);
    const tripId = tripIdStr ? parseInt(tripIdStr, 10) : NaN;
    if (!tripId || isNaN(tripId)) {
      navigate('/home', { replace: true });
      return;
    }
    (async () => {
      try {
        await ensureDbOpen();
        // Try online first
        try {
          const res = await api.get(`/api/sales/trips/${tripId}`);
          useDriverStore.setState({
            currentTrip: res.data,
            orders: res.data.orders || [],
          });
          setBootstrapping(false);
          return;
        } catch {
          // Fallback offline
          const local = await getTripWithOrders(tripId);
          if (local) {
            useDriverStore.setState({
              currentTrip: local,
              orders: local.orders || [],
            });
            setBootstrapping(false);
            return;
          }
        }
        navigate('/home', { replace: true });
      } catch {
        navigate('/home', { replace: true });
      }
    })();
  }, [currentTrip, navigate]);

  useEffect(() => {
    if (!currentTrip && !bootstrapping) {
      navigate('/home', { replace: true });
    }
  }, [currentTrip, bootstrapping, navigate]);

  if (bootstrapping) {
    return (
      <div className="screen flex items-center justify-center">
        <p className="text-text-secondary">Đang tải chuyến xe...</p>
      </div>
    );
  }
  if (!currentTrip) return null;

  const typedOrders = orders as OrderWithPayment[];
  const pending = typedOrders.filter((o) => o.status === 'PENDING');
  const completed = typedOrders.filter((o) => o.status !== 'PENDING');
  const totalCollected = completed.reduce(
    (sum, o) => sum + (o._payment?.totalCollected ?? 0), 0);

  const handleFinishTrip = async () => {
    if (pending.length > 0) {
      if (!confirm(`Còn ${pending.length} điểm giao chưa hoàn thành. Bạn có chắc muốn kết thúc chuyến?`)) {
        return;
      }
    }
    const report = await completeTrip();
    navigate('/eod', { state: { report } });
  };

  return (
    <div className="screen">
      {/* Header */}
      <header className="header safe-top">
        <button
          type="button"
          onClick={() => navigate('/home')}
          className="text-white/80 hover:text-white"
        >
          <BackIcon />
        </button>
        <div className="flex-1">
          <h1 className="header-title">{currentTrip.trip_number}</h1>
          <p className="text-sm text-blue-200">
            {completed.length} / {orders.length} điểm giao
          </p>
        </div>
        <div className="text-right">
          <p className="text-sm text-blue-200">Đã thu</p>
          <p className="font-bold text-white">{formatCurrency(totalCollected)}</p>
        </div>
      </header>

      {/* Offline banner */}
      {!isOnline && (
        <div className="offline-banner">
          Đang offline — Dữ liệu sẽ đồng bộ khi có mạng
        </div>
      )}

      {/* Progress bar */}
      <div className="px-4 pt-3 pb-2">
        <div className="flex gap-2 items-center text-xs text-text-secondary mb-1">
          <span>Tiến độ</span>
          <span className="ml-auto font-semibold text-primary">{completed.length}/{orders.length}</span>
        </div>
        <div className="h-2 bg-slate-200 rounded-full overflow-hidden">
          <div
            className="h-full bg-success rounded-full transition-all duration-500"
            style={{ width: `${orders.length > 0 ? (completed.length / orders.length) * 100 : 0}%` }}
          />
        </div>
      </div>

      {/* Pending deliveries */}
      <div className="flex-1 overflow-y-auto px-4 pb-4">
        {pending.length > 0 && (
          <div className="mb-4">
            <h3 className="text-sm font-semibold text-text-secondary uppercase tracking-wide mb-3">
              Cần giao ({pending.length})
            </h3>
            {pending.map((order, i) => (
              <DeliveryCard
                key={order.trip_order_id}
                order={order}
                index={orders.indexOf(order)}
                isActive={i === 0}
              />
            ))}
          </div>
        )}

        {completed.length > 0 && (
          <div className="mb-4">
            <h3 className="text-sm font-semibold text-text-secondary uppercase tracking-wide mb-3">
              Đã hoàn thành ({completed.length})
            </h3>
            {completed.map((order) => (
              <DeliveryCard
                key={order.trip_order_id}
                order={order}
                index={orders.indexOf(order)}
              />
            ))}
          </div>
        )}

        {/* All done — finish trip */}
        {pending.length === 0 && (
          <div className="card p-6 text-center">
            <div className="w-16 h-16 bg-success-50 rounded-full flex items-center justify-center mx-auto mb-4">
              <CheckIcon />
            </div>
            <h2 className="text-lg font-bold text-success mb-2">Tất cả đã giao xong!</h2>
            <p className="text-sm text-text-secondary mb-4">
              Tổng thu: <strong>{formatCurrency(totalCollected)}</strong>
            </p>
            <button
              type="button"
              className="btn btn-success w-full text-lg py-4 rounded-2xl"
              onClick={handleFinishTrip}
            >
              Kết thúc chuyến
            </button>
          </div>
        )}
      </div>

      {/* Quick FAB for next delivery */}
      {pending.length > 0 && (
        <div className="fixed bottom-6 right-6 left-6">
          <button
            type="button"
            className="btn btn-primary w-full text-lg py-4 rounded-2xl shadow-2xl shadow-primary/30 flex items-center justify-center gap-2"
            onClick={() => navigate(`/deliver/${currentTrip.trip_id}/${pending[0].trip_order_id}`)}
          >
            <span className="w-8 h-8 bg-white/20 rounded-full flex items-center justify-center text-lg font-bold">
              {orders.indexOf(pending[0]) + 1}
            </span>
            Giao điểm tiếp theo
          </button>
        </div>
      )}
    </div>
  );
};

const BackIcon = () => (
  <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
  </svg>
);

const CheckIcon = () => (
  <svg className="w-8 h-8 text-success" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
  </svg>
);
