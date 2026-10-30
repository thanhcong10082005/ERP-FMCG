// ============================================================
// DeliveryCoreScreen — Step 3+4: GPS check-in + 3 delivery buttons
// ============================================================
//
// Route: /deliver/:tripId/:tripOrderId
// Guards: Time lock (5h-22h), GPS gate (>100m = blocked)
// Actions: GIAO ĐỦ | GIAO MỘT PHẦN | KHÔNG GIAO ĐƯỢC
// ============================================================

import React, { useEffect, useState, useCallback, useRef } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useDriverStore } from '../store/driverStore';
import { GPSGuard } from '../components/GPSGuard';
import { checkTimeGuard, isSameDay } from '../utils/timeGuard';
import { formatCurrency } from '../utils/formatters';
import { getTripWithOrders, ensureDbOpen } from '../services/offlineDB';
import api from '../api/apiClient';
import type { TripOrder, DeliveryTrip } from '../types';

const SESSION_TRIP_KEY = 'driver_active_trip_id';

// API returns BigInt-like numeric fields as strings. Coerce once on read.
// ID là string (UUID) từ server.
//   KHÔNG parseInt: `parseInt('a1b2...-uuid')` => NaN => mất context.
//   Các trường đếm (total_orders...) vẫn là number.
const toInt = (v: unknown): number => {
  if (typeof v === 'number') return v;
  if (typeof v === 'string') return parseInt(v, 10) || 0;
  return 0;
};
const asId = (v: unknown): string => {
  if (typeof v === 'string') return v;
  if (typeof v === 'number') return String(v);
  return '';
};
const normalizeTrip = (raw: any): DeliveryTrip => ({
  ...raw,
  trip_id: asId(raw.trip_id),
  warehouse_id: asId(raw.warehouse_id),
  driver_id: asId(raw.driver_id),
  total_orders: toInt(raw.total_orders),
  delivered_count: toInt(raw.delivered_count),
  failed_count: toInt(raw.failed_count),
  orders: (raw.orders || []).map((o: any) => ({
    ...o,
    trip_order_id: asId(o.trip_order_id),
    so_id: asId(o.so_id),
    partner_id: asId(o.partner_id),
    warehouse_id: asId(o.warehouse_id),
    partner_latitude:
      typeof o.partner_latitude === 'string'
        ? parseFloat(o.partner_latitude)
        : o.partner_latitude,
    partner_longitude:
      typeof o.partner_longitude === 'string'
        ? parseFloat(o.partner_longitude)
        : o.partner_longitude,
  })),
});

export const DeliveryCoreScreen: React.FC = () => {
  const { tripId, tripOrderId } = useParams<{ tripId: string; tripOrderId: string }>();
  const navigate = useNavigate();
  const { orders, currentTrip } = useDriverStore();

  // trip_id / trip_order_id là string (UUID).
  const tripOrderIdStr = tripOrderId || '';
  const tripIdStr = tripId || '';

  // Guard against re-entering bootstrap more than once per mount per tripId.
  const bootStartedRef = useRef<string | null>(null);

  // ── Ensure trip/orders are loaded ─────────────────────────
  const [bootstrapping, setBootstrapping] = useState(
    !currentTrip ||
      currentTrip.trip_id !== tripIdStr ||
      orders.length === 0,
  );

  const bootstrapTrip = useCallback(async () => {
    // Already loaded for this trip?
    const state = useDriverStore.getState();
    if (
      state.currentTrip &&
      state.currentTrip.trip_id === tripIdStr &&
      state.orders.length > 0
    ) {
      setBootstrapping(false);
      return;
    }
    try {
      await ensureDbOpen();
      // Try online first
      try {
        const res = await api.get(`/api/sales/trips/${tripIdStr}`);
        const trip = normalizeTrip(res.data);
        useDriverStore.setState({
          currentTrip: trip,
          orders: (trip.orders || []) as any,
        });
        sessionStorage.setItem(SESSION_TRIP_KEY, tripIdStr);
      } catch {
        // Fallback offline
        const local = await getTripWithOrders(tripIdStr);
        if (local) {
          const trip = normalizeTrip(local);
          useDriverStore.setState({
            currentTrip: trip,
            orders: (trip.orders || []) as any,
          });
          sessionStorage.setItem(SESSION_TRIP_KEY, tripIdStr);
        }
      }
    } finally {
      setBootstrapping(false);
    }
  }, [tripIdStr]);

  useEffect(() => {
    if (bootStartedRef.current === tripIdStr) return;
    bootStartedRef.current = tripIdStr;
    bootstrapTrip();
  }, [tripIdStr, bootstrapTrip]);

  // Get order from store
  const order: TripOrder | undefined = orders.find(
    (o) => o.trip_order_id === tripOrderIdStr,
  );

  const [gpsValidated, setGpsValidated] = useState(false);
  const [timeBlocked, setTimeBlocked] = useState(false);
  const [timeMessage, setTimeMessage] = useState('');
  const [selectedAction, setSelectedAction] = useState<'full' | 'partial' | 'fail' | null>(null);

  // Time guard check on mount
  useEffect(() => {
    const result = checkTimeGuard();
    if (!result.allowed) {
      setTimeBlocked(true);
      setTimeMessage(result.message || 'Ngoài giờ giao hàng');
    }
  }, []);

  // time-lock phải kiểm tra LIÊN TỤC, không chỉ 1
  //   lần lúc mount. Trước đây tài xế mở màn hình lúc 21:59, giao xong
  //   22:05 vẫn qua chốt chặn.
  useEffect(() => {
    const recheck = () => {
      const result = checkTimeGuard();
      if (!result.allowed) {
        setTimeBlocked(true);
        setTimeMessage(result.message || 'Ngoài giờ giao hàng');
      } else {
        setTimeBlocked(false);
        setTimeMessage('');
      }
    };

    const intervalId = setInterval(recheck, 60_000);
    document.addEventListener('visibilitychange', recheck);
    window.addEventListener('focus', recheck);

    return () => {
      clearInterval(intervalId);
      document.removeEventListener('visibilitychange', recheck);
      window.removeEventListener('focus', recheck);
    };
  }, []);

  // No Backdate — chặn giao lại chuyến của ngày hôm
  //   qua. Server-side check bắt buộc
  useEffect(() => {
    if (!currentTrip?.trip_date) return;
    if (!isSameDay(currentTrip.trip_date)) {
      setTimeBlocked(true);
      setTimeMessage(
        'Chuyến xe này thuộc ngày trước — không được giao lại. ' +
        'Vui lòng liên hệ Dispatcher.',
      );
    }
  }, [currentTrip?.trip_date]);

  // Redirect if already delivered
  useEffect(() => {
    if (order && order.status !== 'PENDING') {
      navigate('/deliveries', { replace: true });
    }
  }, [order, navigate]);

  // Navigate to sub-screen when action is selected — must be declared
  // BEFORE all conditional early returns so hook count is stable.
  useEffect(() => {
    if (!selectedAction) return;
    if (selectedAction === 'partial') {
      navigate(`/deliver/${tripIdStr}/partial/${tripOrderIdStr}`, { replace: true });
    } else if (selectedAction === 'fail') {
      navigate(`/deliver/${tripIdStr}/fail/${tripOrderIdStr}`, { replace: true });
    } else if (selectedAction === 'full') {
      navigate(`/deliver/${tripIdStr}/pay/${tripOrderIdStr}?mode=full`, { replace: true });
    }
  }, [selectedAction, navigate, tripIdStr, tripOrderIdStr]);

  // ── Conditional render guards (hooks above, JSX below) ────

  // Loading state
  if (bootstrapping) {
    return (
      <div className="screen flex items-center justify-center">
        <p className="text-text-secondary">Đang tải đơn hàng...</p>
      </div>
    );
  }

  if (!order) {
    return (
      <div className="screen flex items-center justify-center p-4">
        <div className="card p-6 text-center max-w-sm w-full">
          <p className="text-text-secondary mb-4">Không tìm thấy đơn hàng</p>
          <p className="text-xs text-text-secondary mb-4">
            Đơn này có thể đã hoàn thành hoặc không thuộc chuyến hiện tại (id #{tripOrderIdStr}).
          </p>
          <button
            type="button"
            className="btn btn-primary w-full"
            onClick={() => navigate('/deliveries')}
          >
            ← Quay lại danh sách
          </button>
        </div>
      </div>
    );
  }

  const totalPayable = (order.total_amount || 0) + (order.old_debt_amount || 0);

  // ── Time blocked ──────────────────────────────────────────
  if (timeBlocked) {
    return (
      <div className="screen flex items-center justify-center p-4">
        <div className="card p-6 text-center max-w-sm w-full">
          <div className="w-16 h-16 bg-danger-50 rounded-full flex items-center justify-center mx-auto mb-4">
            <ClockIcon />
          </div>
          <h2 className="text-xl font-bold text-danger mb-2">Ngoài giờ giao hàng</h2>
          <p className="text-text-secondary text-sm mb-4">{timeMessage}</p>
          <button
            type="button"
            className="btn btn-primary w-full"
            onClick={() => navigate('/deliveries')}
          >
            Quay lại danh sách
          </button>
        </div>
      </div>
    );
  }

  // ── GPS Guard overlay ────────────────────────────────────
  // chỉ truyền `onSkip` khi khách KHÔNG có tọa độ.
  //   Khách có GPS => không có đường bypass.
  if (!gpsValidated) {
    const partnerHasGps =
      order.partner_latitude != null &&
      order.partner_longitude != null &&
      Number.isFinite(order.partner_latitude) &&
      Number.isFinite(order.partner_longitude);

    return (
      <GPSGuard
        partnerLat={order.partner_latitude}
        partnerLon={order.partner_longitude}
        partnerName={order.partner_name}
        onValidated={() => setGpsValidated(true)}
        onSkip={partnerHasGps ? undefined : () => setGpsValidated(true)}
      />
    );
  }

  // ── Core 3-button screen ────────────────────────────────
  return (
    <div className="screen flex flex-col">
      {/* Header */}
      <header className="header safe-top">
        <button
          type="button"
          onClick={() => navigate('/deliveries')}
          className="text-white/80 hover:text-white"
        >
          <BackIcon />
        </button>
        <div className="flex-1">
          <h1 className="header-title">Giao hàng</h1>
          <p className="text-sm text-blue-200">{order.so_number}</p>
        </div>
      </header>

      {/* Order summary */}
      <div className="px-4 pt-4">
        <div className="card p-4">
          <div className="flex items-start gap-3">
            <div className="w-12 h-12 bg-primary-100 rounded-full flex items-center justify-center flex-shrink-0">
              <StoreIcon />
            </div>
            <div className="flex-1 min-w-0">
              <p className="font-bold text-text-primary">{order.partner_name}</p>
              <p className="text-sm text-text-secondary truncate">{order.partner_address || 'Không có địa chỉ'}</p>
              <p className="text-sm text-text-secondary">{order.partner_phone}</p>
            </div>
          </div>
          <div className="mt-3 flex gap-4">
            <div>
              <p className="text-xs text-text-secondary">Tiền đơn</p>
              <p className="font-bold text-primary">{formatCurrency(order.total_amount)}</p>
            </div>
            {(order.old_debt_amount ?? 0) > 0 && (
              <div>
                <p className="text-xs text-text-secondary">Công nợ cũ</p>
                <p className="font-bold text-danger">{formatCurrency(order.old_debt_amount)}</p>
              </div>
            )}
            <div className="ml-auto text-right">
              <p className="text-xs text-text-secondary">Phải thu</p>
              <p className="font-extrabold text-lg text-primary">{formatCurrency(totalPayable)}</p>
            </div>
          </div>
        </div>
      </div>

      {/* Guard indicators */}
      <div className="px-4 py-3 flex gap-2">
        <div className="flex items-center gap-1 text-xs text-success">
          <CheckCircleIcon /> 5h–22h ✓
        </div>
        <div className="flex items-center gap-1 text-xs text-success">
          <CheckCircleIcon /> GPS OK ✓
        </div>
      </div>

      {/* 3 BUTTONS — Core UI */}
      <div className="flex-1 flex flex-col px-4 pb-6">
        <p className="text-sm font-semibold text-text-secondary text-center mb-3">
          Chọn tình trạng giao hàng
        </p>

        <div className="flex-1 flex flex-col gap-3">
          {/* GIAO ĐỦ */}
          <button
            type="button"
            className="btn-delivery-success text-lg"
            onClick={() => setSelectedAction('full')}
          >
            <span className="text-3xl">✓</span>
            <span>GIAO ĐỦ</span>
            <span className="text-sm font-normal opacity-80">~90% trường hợp</span>
          </button>

          {/* GIAO MỘT PHẦN */}
          <button
            type="button"
            className="btn-delivery-warning text-lg"
            onClick={() => setSelectedAction('partial')}
          >
            <span className="text-3xl">~</span>
            <span>GIAO MỘT PHẦN</span>
            <span className="text-sm font-normal opacity-80">Chỉnh sửa số lượng</span>
          </button>

          {/* KHÔNG GIAO ĐƯỢC */}
          <button
            type="button"
            className="btn-delivery-danger text-lg"
            onClick={() => setSelectedAction('fail')}
          >
            <span className="text-3xl">✗</span>
            <span>KHÔNG GIAO ĐƯỢC</span>
            <span className="text-sm font-normal opacity-80">~2% trường hợp</span>
          </button>
        </div>
      </div>
    </div>
  );
};

const BackIcon = () => (
  <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
  </svg>
);

const StoreIcon = () => (
  <svg className="w-6 h-6 text-primary" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4" />
  </svg>
);

const ClockIcon = () => (
  <svg className="w-8 h-8 text-danger" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
  </svg>
);

const CheckCircleIcon = () => (
  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
  </svg>
);
