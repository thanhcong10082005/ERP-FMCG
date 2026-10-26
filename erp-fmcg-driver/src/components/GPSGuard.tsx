// ============================================================
// GPSGuard — Anti-fraud GPS overlay
// ============================================================

import React, { useState, useEffect } from 'react';
import { validateGPSGate } from '../services/gpsService';

interface Props {
  partnerLat?: number;
  partnerLon?: number;
  partnerName: string;
  onValidated: () => void;
  onSkip?: () => void;
  maxDistanceMeters?: number;
}

const formatDistance = (m: number): string => {
  if (m < 1000) return `${Math.round(m)}m`;
  return `${(m / 1000).toFixed(1)}km`;
};

export const GPSGuard: React.FC<Props> = ({
  partnerLat,
  partnerLon,
  partnerName,
  onValidated,
  onSkip,
  maxDistanceMeters = 100,
}) => {
  const [status, setStatus] = useState<'checking' | 'valid' | 'invalid' | 'error'>('checking');
  const [distance, setDistance] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    async function check() {
      setStatus('checking');
      setError(null);

      // If no coordinates, skip GPS check
      if (partnerLat == null || partnerLon == null) {
        if (!cancelled) {
          setStatus('valid');
          onValidated();
        }
        return;
      }

      const result = await validateGPSGate(partnerLat, partnerLon, maxDistanceMeters);

      if (cancelled) return;

      if (result.isValid) {
        setStatus('valid');
        setDistance(result.distanceMeters ?? null);
        onValidated();
      } else {
        setStatus('invalid');
        setDistance(result.distanceMeters ?? null);
        setError(result.error ?? 'GPS không hợp lệ');
      }
    }

    check();
    return () => { cancelled = true; };
  }, [partnerLat, partnerLon, maxDistanceMeters, onValidated]);

  if (status === 'checking') {
    return (
      <div className="fixed inset-0 bg-black/60 flex flex-col items-center justify-center z-50">
        <div className="bg-white rounded-2xl p-6 m-4 text-center max-w-sm w-full shadow-2xl">
          <div className="w-16 h-16 border-4 border-primary border-t-transparent rounded-full animate-spin mx-auto mb-4" />
          <h2 className="text-lg font-bold text-text-primary mb-2">Đang xác minh vị trí</h2>
          <p className="text-sm text-text-secondary">
            Đang lấy tọa độ GPS của bạn...
          </p>
          <p className="text-xs text-text-secondary mt-2">
            Đảm bảo GPS đã bật và cho phép truy cập vị trí
          </p>
          {onSkip && (
            <button
              type="button"
              className="btn btn-ghost w-full mt-4"
              onClick={onSkip}
            >
              Bỏ qua GPS Gate
            </button>
          )}
        </div>
      </div>
    );
  }

  if (status === 'invalid') {
    return (
      <div className="fixed inset-0 bg-black/70 flex flex-col items-center justify-center z-50">
        <div className="bg-white rounded-2xl p-6 m-4 text-center max-w-sm w-full shadow-2xl">
          <div className="w-16 h-16 bg-danger-50 rounded-full flex items-center justify-center mx-auto mb-4">
            <WarningIcon />
          </div>
          <h2 className="text-lg font-bold text-danger mb-2">Chưa đến vị trí giao hàng!</h2>
          <p className="text-sm text-text-secondary mb-2">
              Bạn đang cách <strong>{distance != null ? formatDistance(distance) : '?'}</strong> khỏi{' '}
            <strong>{partnerName}</strong>.
          </p>
          <p className="text-xs text-text-secondary mb-4">
            Yêu cầu: phải cách dưới {maxDistanceMeters}m mới được giao hàng.
          </p>
          {error && (
            <p className="text-xs text-danger bg-danger-50 rounded-lg p-2 mb-4">
              {error}
            </p>
          )}
          <div className="bg-yellow-50 border border-yellow-200 rounded-xl p-3 mb-4">
            <p className="text-xs text-yellow-700">
              Di chuyển đến đúng vị trí cửa hàng rồi bấm "Kiểm tra lại"
            </p>
          </div>
          <button
            type="button"
            className="btn btn-primary w-full mb-3"
            onClick={() => {
              setStatus('checking');
              setTimeout(() => {
                validateGPSGate(partnerLat, partnerLon, maxDistanceMeters).then((result) => {
                  if (result.isValid) {
                    setStatus('valid');
                    setDistance(result.distanceMeters ?? null);
                    onValidated();
                  } else {
                    setStatus('invalid');
                    setDistance(result.distanceMeters ?? null);
                    setError(result.error ?? null);
                  }
                });
              }, 100);
            }}
          >
            Kiểm tra lại GPS
          </button>
          {onSkip && (
            <button type="button" className="btn btn-ghost w-full" onClick={onSkip}>
              Bỏ qua GPS Gate
            </button>
          )}
        </div>
      </div>
    );
  }

  if (status === 'valid') {
    return (
      <div className="fixed inset-0 bg-black/60 flex flex-col items-center justify-center z-50">
        <div className="bg-white rounded-2xl p-6 m-4 text-center max-w-sm w-full shadow-2xl">
          <div className="w-16 h-16 bg-success-50 rounded-full flex items-center justify-center mx-auto mb-4">
            <CheckIcon />
          </div>
          <h2 className="text-lg font-bold text-success mb-2">Đã xác minh vị trí!</h2>
          <p className="text-sm text-text-secondary">
            Bạn đang cách <strong>{distance != null ? formatDistance(distance) : 'gần'}</strong> vị trí giao hàng.
          </p>
          {distance != null && distance <= 30 && (
            <p className="text-xs text-success mt-1">Vị trí chính xác — OK!</p>
          )}
        </div>
      </div>
    );
  }

  return null;
};

const WarningIcon = () => (
  <svg className="w-8 h-8 text-danger" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
  </svg>
);

const CheckIcon = () => (
  <svg className="w-8 h-8 text-success" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M5 13l4 4L19 7" />
  </svg>
);
