// ============================================================
// PaymentScreen — Step 5: Thanh toán đa hình thức
// ============================================================
// Route: /deliver/:tripId/pay/:tripOrderId?mode=full|partial
// ============================================================

import React, { useState } from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { useDriverStore, type DeliveredItem } from '../store/driverStore';
import { PaymentInput } from '../components/PaymentInput';
import { LoadingOverlay } from '../components/LoadingOverlay';
import { formatCurrency } from '../utils/formatters';
import type { TripOrder, SOItem } from '../types';

export const PaymentScreen: React.FC = () => {
  const { tripId, tripOrderId } = useParams<{ tripId: string; tripOrderId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const { orders, submitFullDelivery, submitPartialDelivery } = useDriverStore();

  const tripOrderIdNum = parseInt(tripOrderId || '0', 10);
  const mode = new URLSearchParams(location.search).get('mode') || 'full';

  const order: TripOrder | undefined = orders.find(
    (o) => Number(o.trip_order_id) === tripOrderIdNum,
  );

  // From PartialDeliveryScreen navigation state
  const locationState = location.state as {
    deliveredItems?: Array<{
      product_id: number;
      product_name: string;
      ordered_qty: number;
      delivered_qty: number;
      returned_qty: number;
      unit_price: number;
      line_total: number;
    }>;
    signatureData?: string;
    photoData?: string;
    quantities?: Record<number, number>;
  } | null;

  const [payment, setPayment] = useState<{
    newOrderCash: number;
    newOrderTransfer: number;
    newOrderCredit: number;
    totalCollected: number;
    recipientName: string;
    notes: string;
  }>({
    newOrderCash: 0,
    newOrderTransfer: 0,
    newOrderCredit: 0,
    totalCollected: 0,
    recipientName: '',
    notes: '',
  });
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!order) {
    return (
      <div className="screen flex items-center justify-center">
        <p className="text-text-secondary">Không tìm thấy đơn hàng</p>
      </div>
    );
  }

  // Calculate amounts for partial delivery
  let newOrderAmount = order.total_amount || 0;
  if (mode === 'partial' && locationState?.deliveredItems) {
    newOrderAmount = locationState.deliveredItems.reduce(
      (sum, item) => sum + item.line_total, 0,
    );
  }

  const oldDebtAmount = order.old_debt_amount || 0;
  const totalRequired = newOrderAmount + oldDebtAmount;

  const handlePaymentChange = (split: {
    newOrderCash: number;
    newOrderTransfer: number;
    newOrderCredit: number;
    totalCollected: number;
    recipientName: string;
    notes: string;
  }) => {
    setPayment(split);
  };

  const handleConfirm = async () => {
    if (payment.totalCollected < 0) {
      setError('Số tiền thu không hợp lệ');
      return;
    }

    setSubmitting(true);
    setError(null);

    try {
      if (mode === 'full') {
        await submitFullDelivery(tripOrderIdNum, {
          newOrderCash: payment.newOrderCash,
          newOrderTransfer: payment.newOrderTransfer,
          newOrderCredit: payment.newOrderCredit,
          oldDebtCash: 0,
          oldDebtTransfer: 0,
          oldDebtCredit: 0,
          totalCollected: payment.totalCollected,
          recipientName: payment.recipientName,
          notes: payment.notes,
        });
      } else if (mode === 'partial') {
        const items: DeliveredItem[] = (locationState?.deliveredItems || []).map((d) => ({
          product_id: d.product_id,
          product_name: d.product_name,
          ordered_qty: d.ordered_qty,
          delivered_qty: d.delivered_qty,
          returned_qty: d.returned_qty,
          unit_price: d.unit_price,
          line_total: d.line_total,
        }));

        await submitPartialDelivery(
          tripOrderIdNum,
          {
            newOrderCash: payment.newOrderCash,
            newOrderTransfer: payment.newOrderTransfer,
            newOrderCredit: payment.newOrderCredit,
            oldDebtCash: 0,
            oldDebtTransfer: 0,
            oldDebtCredit: 0,
            totalCollected: payment.totalCollected,
            recipientName: payment.recipientName,
            notes: payment.notes,
          },
          items,
          locationState?.signatureData || '',
          locationState?.photoData || '',
        );
      }

      navigate('/deliveries');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Lỗi khi tạo phiếu giao hàng');
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
          <h1 className="header-title">Thanh Toán</h1>
          <p className="text-sm text-blue-200">{order.so_number}</p>
        </div>
      </header>

      {/* Order summary */}
      <div className="px-4 pt-3">
        <div className="card p-4">
          <div className="flex justify-between items-start">
            <div>
              <p className="font-semibold text-text-primary">{order.partner_name}</p>
              <p className="text-sm text-text-secondary">{order.address_line || '—'}</p>
            </div>
            <div className="text-right">
              {mode === 'partial' && (
                <span className="badge badge-partial text-xs mb-1">Giao một phần</span>
              )}
              <p className="font-bold text-primary">{formatCurrency(totalRequired)}</p>
            </div>
          </div>

          {/* Partial delivery items summary */}
          {mode === 'partial' && locationState?.deliveredItems && (
            <div className="mt-3 pt-3 border-t border-border">
              <p className="text-xs text-text-secondary mb-1">Hàng đã giao:</p>
              {(order.items || []).map((item: SOItem) => {
                const delivered = locationState.deliveredItems?.find(
                  (d) => d.product_id === item.product_id,
                );
                return (
                  <div key={item.so_item_id} className="flex justify-between text-sm py-0.5">
                    <span className="text-text-secondary">{item.product_name}</span>
                    <span className="font-medium">
                      {delivered ? `${delivered.delivered_qty}/${item.quantity}` : item.quantity}
                      {' → '}
                      <span className="text-success">{formatCurrency(delivered?.line_total ?? item.line_total)}</span>
                    </span>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>

      {/* Payment input */}
      <div className="flex-1 overflow-y-auto px-4 py-3">
        <PaymentInput
          newOrderAmount={newOrderAmount}
          oldDebtAmount={oldDebtAmount}
          onChange={handlePaymentChange}
        />

        {error && (
          <div className="mt-3 p-3 bg-danger-50 border border-danger-200 rounded-xl">
            <p className="text-sm text-danger">{error}</p>
          </div>
        )}
      </div>

      {/* Confirm button */}
      <div className="px-4 pb-6 safe-bottom">
        <button
          type="button"
          className="btn btn-success w-full text-lg py-4 rounded-2xl shadow-lg shadow-success/30"
          onClick={handleConfirm}
          disabled={submitting}
        >
          {submitting ? (
            <span className="flex items-center justify-center gap-2">
              <span className="w-5 h-5 border-2 border-white border-t-transparent rounded-full animate-spin" />
              Đang tạo phiếu giao hàng...
            </span>
          ) : (
            `XÁC NHẬN HOÀN THÀNH (${formatCurrency(payment.totalCollected)})`
          )}
        </button>
      </div>

      {submitting && <LoadingOverlay message="Đang tạo phiếu giao hàng..." />}
    </div>
  );
};

const BackIcon = () => (
  <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
  </svg>
);
