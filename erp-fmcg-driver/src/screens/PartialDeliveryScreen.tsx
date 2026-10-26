// ============================================================
// PartialDeliveryScreen — Step 4b: Giao một phần
// ============================================================

import React, { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useDriverStore, type DeliveredItem } from '../store/driverStore';
import { QuantityAdjuster } from '../components/QuantityAdjuster';
import { SignaturePad } from '../components/SignaturePad';
import { PhotoCapture } from '../components/PhotoCapture';
import { formatCurrency } from '../utils/formatters';
import type { TripOrder, SOItem } from '../types';

export const PartialDeliveryScreen: React.FC = () => {
  const { tripId, tripOrderId } = useParams<{ tripId: string; tripOrderId: string }>();
  const navigate = useNavigate();
  const { orders } = useDriverStore();

  const tripOrderIdNum = parseInt(tripOrderId || '0', 10);
  const order: TripOrder | undefined = orders.find(
    (o) => Number(o.trip_order_id) === tripOrderIdNum,
  );

  const [quantities, setQuantities] = useState<Record<number, number>>(() => {
    // Initialize with ordered quantities
    const map: Record<number, number> = {};
    (order?.items || []).forEach((item: SOItem) => {
      map[item.product_id] = item.quantity;
    });
    return map;
  });

  const [signature, setSignature] = useState<string | null>(null);
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

  const handleQtyChange = (productId: number, newQty: number) => {
    setQuantities((prev) => ({ ...prev, [productId]: newQty }));
  };

  const isValid = signature && photo;

  const handleSubmit = async () => {
    if (!isValid) {
      setError('Bắt buộc có chữ ký và ảnh bằng chứng');
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      const deliveredItems: DeliveredItem[] = (order.items || []).map((item: SOItem) => {
        const deliveredQty = quantities[item.product_id] ?? item.quantity;
        const returnedQty = item.quantity - deliveredQty;
        return {
          product_id: item.product_id,
          product_name: item.product_name,
          ordered_qty: item.quantity,
          delivered_qty: deliveredQty,
          returned_qty: returnedQty,
          unit_price: item.unit_price,
          line_total: deliveredQty * item.unit_price,
        };
      });

      // Payment will be entered in the next screen
      navigate(`/deliver/${tripId}/pay/${tripOrderId}?mode=partial`, {
        state: {
          deliveredItems,
          signatureData: signature,
          photoData: photo,
          quantities,
        },
      });
      setSubmitting(false);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Lỗi');
      setSubmitting(false);
    }
  };

  const totalReturned = (order.items || []).reduce((sum: number, item: SOItem) => {
    const delivered = quantities[item.product_id] ?? item.quantity;
    return sum + (item.quantity - delivered) * item.unit_price;
  }, 0);

  // Currently unused but kept for future partial total display
  void (order.items || []).reduce((sum: number, item: SOItem) => {
    return sum + (quantities[item.product_id] ?? item.quantity) * item.unit_price;
  }, 0);

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
          <h1 className="header-title">Giao một phần</h1>
          <p className="text-sm text-blue-200">{order.so_number}</p>
        </div>
      </header>

      {/* Order summary */}
      <div className="px-4 pt-3">
        <div className="card p-3 flex justify-between items-center">
          <div>
            <p className="text-xs text-text-secondary">{order.partner_name}</p>
            <p className="text-sm font-semibold">Đơn hàng: {order.so_number}</p>
          </div>
          <div className="text-right">
            <p className="text-xs text-text-secondary">Tổng đơn</p>
            <p className="font-bold text-primary">{formatCurrency(order.total_amount)}</p>
          </div>
        </div>
      </div>

      {/* Product qty adjuster */}
      <div className="flex-1 overflow-y-auto px-4 py-3">
        <p className="text-sm font-semibold text-text-secondary mb-2">
          Điều chỉnh số lượng (chỉ giảm)
        </p>
        <div className="card">
          <div className="px-4 pt-3">
            <p className="text-xs text-text-secondary mb-1">
              Bấm nút <strong>−</strong> để giảm số lượng giao. Không được tăng.
            </p>
          </div>
          {(order.items || []).map((item: SOItem) => (
            <div key={item.so_item_id} className="px-4">
              <QuantityAdjuster
                orderedQty={item.quantity}
                currentQty={quantities[item.product_id] ?? item.quantity}
                productName={item.product_name}
                unitName={item.unit_name || 'đơn vị'}
                onChange={(newQty) => handleQtyChange(item.product_id, newQty)}
              />
            </div>
          ))}
        </div>

        {/* Returned summary */}
        {totalReturned > 0 && (
          <div className="mt-3 p-3 bg-yellow-50 border border-yellow-200 rounded-xl">
            <p className="text-sm font-semibold text-warning">Hàng rớt:</p>
            <p className="text-sm text-warning">{formatCurrency(totalReturned)}</p>
            <p className="text-xs text-yellow-700 mt-1">
              Số tiền hàng rớt sẽ được ghi nhận và trừ khỏi tổng thu.
            </p>
          </div>
        )}

        {/* Signature */}
        <div className="mt-4">
          <SignaturePad
            onSave={setSignature}
            onClear={() => setSignature(null)}
          />
        </div>

        {/* Photo */}
        <div className="mt-4">
          <PhotoCapture
            label="Chụp ảnh hàng rớt / hàng lỗi"
            required
            onCapture={setPhoto}
          />
        </div>

        {error && (
          <div className="mt-3 p-3 bg-danger-50 border border-danger-200 rounded-xl">
            <p className="text-sm text-danger">{error}</p>
          </div>
        )}
      </div>

      {/* Submit button */}
      <div className="px-4 pb-6 safe-bottom">
        <button
          type="button"
          className="btn btn-warning w-full text-lg py-4 rounded-2xl"
          onClick={handleSubmit}
          disabled={submitting || !isValid}
        >
          {submitting ? 'Đang xử lý...' : 'Tiếp tục thanh toán'}
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
