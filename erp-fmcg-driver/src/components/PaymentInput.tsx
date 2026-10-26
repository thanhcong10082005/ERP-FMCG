// ============================================================
// PaymentInput — 3 ô nhập tiền: Tiền mặt / Chuyển khoản / Ghi nợ
// ============================================================

import React, { useState, useEffect } from 'react';
import { formatCurrency } from '../utils/formatters';

interface PaymentSplit {
  newOrderCash: number;
  newOrderTransfer: number;
  newOrderCredit: number;
  oldDebtCash: number;
  oldDebtTransfer: number;
  oldDebtCredit: number;
  totalCollected: number;
  recipientName: string;
  notes: string;
}

interface Props {
  newOrderAmount: number;
  oldDebtAmount: number;
  onChange: (split: PaymentSplit) => void;
  initialSplit?: Partial<PaymentSplit>;
}

export const PaymentInput: React.FC<Props> = ({
  newOrderAmount,
  oldDebtAmount,
  onChange,
  initialSplit,
}) => {
  const totalRequired = newOrderAmount + oldDebtAmount;

  const [cash, setCash] = useState<string>(initialSplit?.newOrderCash?.toString() ?? '');
  const [transfer, setTransfer] = useState<string>(initialSplit?.newOrderTransfer?.toString() ?? '');
  const [credit, setCredit] = useState<string>(initialSplit?.newOrderCredit?.toString() ?? '');
  const [recipientName, setRecipientName] = useState(initialSplit?.recipientName ?? '');
  const [notes, setNotes] = useState(initialSplit?.notes ?? '');

  const toNum = (v: string) => Math.max(0, parseFloat(v) || 0);

  const cashNum = toNum(cash);
  const transferNum = toNum(transfer);
  const creditNum = toNum(credit);
  const totalPaid = cashNum + transferNum + creditNum;
  const remaining = totalRequired - totalPaid;

  useEffect(() => {
    onChange({
      newOrderCash: cashNum,
      newOrderTransfer: transferNum,
      newOrderCredit: creditNum,
      oldDebtCash: 0,
      oldDebtTransfer: 0,
      oldDebtCredit: remaining > 0 ? remaining : 0,
      totalCollected: totalPaid,
      recipientName,
      notes,
    });
  }, [cash, transfer, credit, recipientName, notes]);

  const isExact = Math.abs(remaining) < 100;
  const isOver = remaining < -100;

  return (
    <div className="space-y-4">
      {/* Summary */}
      <div className="card p-4 bg-primary-50 border-primary-200">
        <div className="flex justify-between text-sm text-primary-800 mb-1">
          <span>Tiền đơn mới</span>
          <span className="font-semibold">{formatCurrency(newOrderAmount)}</span>
        </div>
        {oldDebtAmount > 0 && (
          <div className="flex justify-between text-sm text-primary-800 mb-1">
            <span>Công nợ cũ</span>
            <span className="font-semibold text-danger">{formatCurrency(oldDebtAmount)}</span>
          </div>
        )}
        <div className="flex justify-between font-bold text-primary text-base pt-2 border-t border-primary-200">
          <span>Tổng phải thu</span>
          <span>{formatCurrency(totalRequired)}</span>
        </div>
      </div>

      {/* 3 input fields */}
      <div className="space-y-3">
        <div className="input-group">
          <label className="input-label">Tiền mặt (đã nhận)</label>
          <div className="relative">
            <input
              type="number"
              inputMode="numeric"
              className="input pr-16"
              placeholder="0"
              value={cash}
              onChange={(e) => setCash(e.target.value)}
              min="0"
            />
            <span className="absolute right-4 top-1/2 -translate-y-1/2 text-text-secondary text-sm">VND</span>
          </div>
        </div>

        <div className="input-group">
          <label className="input-label">Chuyển khoản</label>
          <div className="relative">
            <input
              type="number"
              inputMode="numeric"
              className="input pr-16"
              placeholder="0"
              value={transfer}
              onChange={(e) => setTransfer(e.target.value)}
              min="0"
            />
            <span className="absolute right-4 top-1/2 -translate-y-1/2 text-text-secondary text-sm">VND</span>
          </div>
        </div>

        <div className="input-group">
          <label className="input-label">Ghi nợ (tự động tính)</label>
          <div className="relative">
            <input
              type="number"
              inputMode="numeric"
              className={`input pr-16 ${
                remaining > 0 ? 'border-warning bg-warning-50' :
                isOver ? 'border-danger bg-danger-50' :
                'border-success bg-success-50'
              }`}
              placeholder={remaining > 0 ? formatCurrency(remaining) : '0'}
              value={credit}
              onChange={(e) => setCredit(e.target.value)}
              min="0"
            />
            <span className="absolute right-4 top-1/2 -translate-y-1/2 text-text-secondary text-sm">VND</span>
          </div>
          {remaining > 0 && (
            <p className="text-xs text-warning">
              Còn thiếu {formatCurrency(remaining)} — hệ thống sẽ ghi nợ
            </p>
          )}
          {isOver && (
            <p className="text-xs text-danger">
              Vượt quá tổng phải thu!
            </p>
          )}
        </div>
      </div>

      {/* Collected summary */}
      <div className={`card p-4 ${
        isExact ? 'border-success bg-success-50' :
        isOver ? 'border-danger bg-danger-50' :
        'border-warning bg-warning-50'
      }`}>
        <div className="flex justify-between font-bold text-lg">
          <span>Đã thu</span>
          <span className={isExact ? 'text-success' : isOver ? 'text-danger' : 'text-warning'}>
            {formatCurrency(totalPaid)}
          </span>
        </div>
        {remaining > 0 && (
          <div className="flex justify-between text-sm text-text-secondary mt-1">
            <span>Còn phải thu</span>
            <span>{formatCurrency(remaining)}</span>
          </div>
        )}
      </div>

      {/* Recipient name */}
      <div className="input-group">
        <label className="input-label">Người nhận hàng (tùy chọn)</label>
        <input
          type="text"
          className="input"
          placeholder="Nhập tên người nhận..."
          value={recipientName}
          onChange={(e) => setRecipientName(e.target.value)}
        />
      </div>

      {/* Notes */}
      <div className="input-group">
        <label className="input-label">Ghi chú (tùy chọn)</label>
        <textarea
          className="input resize-none"
          rows={2}
          placeholder="Ghi chú thêm..."
          value={notes}
          onChange={(e) => setNotes(e.target.value)}
        />
      </div>
    </div>
  );
};
