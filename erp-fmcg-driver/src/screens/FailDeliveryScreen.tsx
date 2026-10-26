// ============================================================
// FailDeliveryScreen — Step 4c: Không giao được / Hủy
// ============================================================

import React, { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useDriverStore } from '../store/driverStore';
import { PhotoCapture } from '../components/PhotoCapture';
import type { TripOrder } from '../types';

const FAILURE_REASONS = [
  { id: 'REJECTED', label: 'Từ chối nhận', description: 'Khách hàng từ chối nhận hàng' },
  { id: 'LOST', label: 'Thất lạc', description: 'Không tìm được địa chỉ / hàng bị thất lạc' },
  { id: 'RESCHEDULE', label: 'Hẹn giao lại', description: 'Khách hàng yêu cầu hẹn giao ngày khác' },
  { id: 'OTHER', label: 'Lý do khác', description: 'Lý do khác (vui lòng ghi chú)' },
];

export const FailDeliveryScreen: React.FC = () => {
  const { tripId, tripOrderId } = useParams<{ tripId: string; tripOrderId: string }>();
  const navigate = useNavigate();
  const { orders, submitFailedDelivery } = useDriverStore();

  const tripOrderIdNum = parseInt(tripOrderId || '0', 10);
  const order: TripOrder | undefined = orders.find(
    (o) => Number(o.trip_order_id) === tripOrderIdNum,
  );

  const [selectedReason, setSelectedReason] = useState<string | null>(null);
  const [otherReason, setOtherReason] = useState('');
  const [photo, setPhoto] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!order) {
    return (
      <div className="screen flex items-center justify-center">
        <p className="text-text-secondary">Không tìm thấy đơn hàng</p>
      </div>
    );
  }

  const isValid = selectedReason && photo && (selectedReason !== 'OTHER' || otherReason.trim());

  const handleSubmit = async () => {
    if (!isValid) {
      setError('Vui lòng chọn lý do và chụp ảnh bằng chứng');
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      const reason = selectedReason === 'OTHER' ? otherReason.trim() : selectedReason;
      await submitFailedDelivery(tripOrderIdNum, reason, photo || '');
      navigate('/deliveries');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Lỗi khi hủy đơn');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="screen flex flex-col">
      {/* Header */}
      <header className="header safe-top">
        <button
          type="button"
          onClick={() => navigate(`/deliver/${tripId}/${tripOrderId}`)}
          className="text-white/80 hover:text-white"
        >
          <BackIcon />
        </button>
        <div className="flex-1">
          <h1 className="header-title text-danger">Không giao được</h1>
          <p className="text-sm text-red-200">{order.so_number}</p>
        </div>
      </header>

      {/* Order info */}
      <div className="px-4 pt-3">
        <div className="card p-3">
          <p className="font-semibold text-text-primary">{order.partner_name}</p>
          <p className="text-sm text-text-secondary">{order.address_line || '—'}</p>
        </div>
      </div>

      {/* Reason selector */}
      <div className="flex-1 overflow-y-auto px-4 py-3">
        <p className="text-sm font-semibold text-text-secondary mb-2">
          Chọn lý do <span className="text-danger">*</span>
        </p>
        <div className="space-y-2">
          {FAILURE_REASONS.map((r) => (
            <label
              key={r.id}
              className={`card p-4 flex items-start gap-3 cursor-pointer transition-all ${
                selectedReason === r.id
                  ? 'border-danger bg-danger-50 ring-1 ring-danger'
                  : 'hover:border-slate-300'
              }`}
            >
              <input
                type="radio"
                name="reason"
                value={r.id}
                checked={selectedReason === r.id}
                onChange={() => setSelectedReason(r.id)}
                className="mt-1 accent-danger"
              />
              <div>
                <p className={`font-semibold ${
                  selectedReason === r.id ? 'text-danger' : 'text-text-primary'
                }`}>
                  {r.label}
                </p>
                <p className="text-xs text-text-secondary mt-0.5">{r.description}</p>
              </div>
            </label>
          ))}
        </div>

        {/* Other reason text */}
        {selectedReason === 'OTHER' && (
          <div className="mt-3 input-group">
            <label className="input-label">Mô tả lý do <span className="text-danger">*</span></label>
            <textarea
              className="input resize-none"
              rows={3}
              placeholder="Nhập lý do..."
              value={otherReason}
              onChange={(e) => setOtherReason(e.target.value)}
            />
          </div>
        )}

        {/* Photo — required */}
        <div className="mt-4">
          <PhotoCapture
            label="Chụp ảnh bằng chứng"
            required
            onCapture={setPhoto}
          />
          <p className="text-xs text-text-secondary mt-1">
            VD: Chụp cửa hàng đóng cửa, biên lai từ chối, v.v.
          </p>
        </div>

        {error && (
          <div className="mt-3 p-3 bg-danger-50 border border-danger-200 rounded-xl">
            <p className="text-sm text-danger">{error}</p>
          </div>
        )}
      </div>

      {/* Submit */}
      <div className="px-4 pb-6 safe-bottom">
        <button
          type="button"
          className="btn btn-danger w-full text-lg py-4 rounded-2xl"
          onClick={handleSubmit}
          disabled={submitting || !isValid}
        >
          {submitting ? 'Đang xử lý...' : 'Xác nhận hủy đơn hàng'}
        </button>
      </div>
    </div>
  );
};

const BackIcon = () => (
  <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
  </svg>
);
