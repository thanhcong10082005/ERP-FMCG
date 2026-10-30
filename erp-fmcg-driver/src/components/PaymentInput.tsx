// ============================================================
// PaymentInput — Ma trận thanh toán 6 ô:
//   Đơn mới   : Tiền mặt / Chuyển khoản / Ghi nợ
//   Công nợ cũ : Tiền mặt / Chuyển khoản / Ghi nợ
// ------------------------------------------------------------
// SỬA LỖI MẤT oldDebtCash / oldDebtTransfer / oldDebtCredit.
//
// Triệu chứng
//   PaymentInput chỉ có 3 ô (cash/transfer/credit) cho ĐƠN MỚI, rồi
//   set cứng:
//       oldDebtCash: 0,
//       oldDebtTransfer: 0,
//       oldDebtCredit: remaining,
//   => Mọi khoản thu cho CÔNG NỢ CŨ bị ghi nhầm thành "ghi nợ công
//      nợ cũ", dù tài xế đã thu tiền mặt.
//   => Công nợ cũ của khách KHÔNG BAO GIỜ giảm => tài xế thu tiền
//      mà hệ thống vẫn cho là nợ.
//
// Cách sửa
//   Tách rõ 2 nhóm, mỗi nhóm 3 ô. Ô "ghi nợ" của từng nhóm được
//   TỰ ĐỘNG tính = phần còn thiếu của nhóm đó (nếu người dùng
//   không nhập tay), và vẫn sửa được bằng tay.
// ============================================================

import React, { useState, useEffect } from 'react';
import { formatCurrency } from '../utils/formatters';

export interface PaymentSplit {
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

/** Ô nhập tiền có nhãn + đơn vị VND. */
const MoneyField: React.FC<{
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  tone?: 'default' | 'warning' | 'danger' | 'success';
  hint?: string;
}> = ({ label, value, onChange, placeholder = '0', tone = 'default', hint }) => (
  <div className="input-group">
    <label className="input-label">{label}</label>
    <div className="relative">
      <input
        type="number"
        inputMode="numeric"
        className={`input pr-16 ${
          tone === 'warning' ? 'border-warning bg-warning-50' :
          tone === 'danger' ? 'border-danger bg-danger-50' :
          tone === 'success' ? 'border-success bg-success-50' : ''
        }`}
        placeholder={placeholder}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        min="0"
      />
      <span className="absolute right-4 top-1/2 -translate-y-1/2 text-text-secondary text-sm">VND</span>
    </div>
    {hint && <p className="text-xs text-text-secondary mt-1">{hint}</p>}
  </div>
);

export const PaymentInput: React.FC<Props> = ({
  newOrderAmount,
  oldDebtAmount,
  onChange,
  initialSplit,
}) => {
  const totalRequired = newOrderAmount + oldDebtAmount;

  // ── Nhóm đơn mới ──
  const [cash, setCash] = useState<string>(initialSplit?.newOrderCash?.toString() ?? '');
  const [transfer, setTransfer] = useState<string>(initialSplit?.newOrderTransfer?.toString() ?? '');
  const [credit, setCredit] = useState<string>(initialSplit?.newOrderCredit?.toString() ?? '');

  // ── Nhóm công nợ cũ ──
  const [oldCash, setOldCash] = useState<string>(initialSplit?.oldDebtCash?.toString() ?? '');
  const [oldTransfer, setOldTransfer] = useState<string>(initialSplit?.oldDebtTransfer?.toString() ?? '');
  const [oldCredit, setOldCredit] = useState<string>(initialSplit?.oldDebtCredit?.toString() ?? '');

  const [recipientName, setRecipientName] = useState(initialSplit?.recipientName ?? '');
  const [notes, setNotes] = useState(initialSplit?.notes ?? '');

  const toNum = (v: string) => Math.max(0, parseFloat(v) || 0);

  const cashNum = toNum(cash);
  const transferNum = toNum(transfer);
  const creditNum = toNum(credit);

  const oldCashNum = toNum(oldCash);
  const oldTransferNum = toNum(oldTransfer);
  const oldCreditNum = toNum(oldCredit);

  // Phần còn thiếu của TỪNG nhóm (tính riêng, không gộp)
  const newRemaining = newOrderAmount - (cashNum + transferNum + creditNum);
  const oldRemaining = oldDebtAmount - (oldCashNum + oldTransferNum + oldCreditNum);

  // Tổng đã thu = tiền mặt + chuyển khoản của CẢ 2 nhóm
  // (ghi nợ KHÔNG phải tiền thu)
  const totalPaid = cashNum + transferNum + oldCashNum + oldTransferNum;
  const remaining = totalRequired - totalPaid;

  useEffect(() => {
    onChange({
      newOrderCash: cashNum,
      newOrderTransfer: transferNum,
      newOrderCredit: creditNum,
      oldDebtCash: oldCashNum,
      oldDebtTransfer: oldTransferNum,
      oldDebtCredit: oldCreditNum,
      totalCollected: totalPaid,
      recipientName,
      notes,
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cash, transfer, credit, oldCash, oldTransfer, oldCredit, recipientName, notes]);

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

      {/* ══ NHÓM 1: ĐƠN MỚI ══ */}
      <div className="card p-4 space-y-3">
        <p className="text-sm font-semibold text-text-primary">
          Đơn mới <span className="font-normal text-text-secondary">({formatCurrency(newOrderAmount)})</span>
        </p>

        <MoneyField label="Tiền mặt (đã nhận)" value={cash} onChange={setCash} />
        <MoneyField label="Chuyển khoản" value={transfer} onChange={setTransfer} />
        <MoneyField
          label="Ghi nợ (tự động tính)"
          value={credit}
          onChange={setCredit}
          placeholder={newRemaining > 0 ? formatCurrency(newRemaining) : '0'}
          tone={
            newRemaining > 100 ? 'warning' :
            newRemaining < -100 ? 'danger' :
            newRemaining === 0 ? 'success' : 'default'
          }
          hint={
            newRemaining > 100
              ? `Còn thiếu ${formatCurrency(newRemaining)} — hệ thống sẽ ghi nợ`
              : newRemaining < -100
                ? 'Vượt quá giá trị đơn mới!'
                : undefined
          }
        />
      </div>

      {/* ══ NHÓM 2: CÔNG NỢ CŨ ══ */}
      {oldDebtAmount > 0 && (
        <div className="card p-4 space-y-3 border-danger-200">
          <p className="text-sm font-semibold text-danger">
            Công nợ cũ <span className="font-normal">({formatCurrency(oldDebtAmount)})</span>
          </p>

          <MoneyField
            label="Thu tiền mặt (nợ cũ)"
            value={oldCash}
            onChange={setOldCash}
            hint="Tiền mặt thu được cho khoản nợ cũ"
          />
          <MoneyField
            label="Thu chuyển khoản (nợ cũ)"
            value={oldTransfer}
            onChange={setOldTransfer}
            hint="Chuyển khoản thu được cho khoản nợ cũ"
          />
          <MoneyField
            label="Ghi nợ (nợ cũ, tự động tính)"
            value={oldCredit}
            onChange={setOldCredit}
            placeholder={oldRemaining > 0 ? formatCurrency(oldRemaining) : '0'}
            tone={
              oldRemaining > 100 ? 'warning' :
              oldRemaining < -100 ? 'danger' :
              oldRemaining === 0 ? 'success' : 'default'
            }
            hint={
              oldRemaining > 100
                ? `Còn thiếu ${formatCurrency(oldRemaining)} — hệ thống sẽ ghi nợ`
                : oldRemaining < -100
                  ? 'Vượt quá công nợ cũ của khách!'
                  : undefined
            }
          />
        </div>
      )}

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
        {isOver && (
          <div className="flex justify-between text-sm text-danger mt-1">
            <span>Thu vượt quá</span>
            <span>{formatCurrency(-remaining)}</span>
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
