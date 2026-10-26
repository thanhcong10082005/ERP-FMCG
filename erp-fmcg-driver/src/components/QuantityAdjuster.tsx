// ============================================================
// QuantityAdjuster — Chỉ cho phép TRỪ số lượng
// ============================================================

import React from 'react';

interface Props {
  orderedQty: number;
  currentQty: number;
  productName: string;
  unitName?: string;
  onChange: (newQty: number) => void;
}

export const QuantityAdjuster: React.FC<Props> = ({
  orderedQty,
  currentQty,
  productName,
  unitName = 'đơn vị',
  onChange,
}) => {
  const handleDecrement = () => {
    if (currentQty > 0) {
      onChange(currentQty - 1);
    }
  };

  const handleIncrement = () => {
    if (currentQty < orderedQty) {
      onChange(currentQty + 1);
    }
  };

  const isDecrementDisabled = currentQty <= 0;
  const isIncrementDisabled = currentQty >= orderedQty;
  const returned = orderedQty - currentQty;

  return (
    <div className="flex items-center gap-3 py-3 border-b border-border last:border-0">
      {/* Product info */}
      <div className="flex-1 min-w-0">
        <p className="text-sm font-medium text-text-primary truncate">{productName}</p>
        <p className="text-xs text-text-secondary">
          Đặt: {orderedQty} {unitName}
          {returned > 0 && (
            <span className="text-danger ml-2">→ Rớt: {returned}</span>
          )}
        </p>
      </div>

      {/* Qty display */}
      <div className="flex items-center gap-2">
        {/* Minus button — only minus allowed */}
        <button
          type="button"
          onClick={handleDecrement}
          disabled={isDecrementDisabled}
          className={`w-10 h-10 rounded-full font-bold text-xl flex items-center justify-center
            transition-all active:scale-90 ${
              isDecrementDisabled
                ? 'bg-slate-100 text-slate-300 cursor-not-allowed'
                : 'bg-danger text-white shadow'
            }`}
        >
          −
        </button>

        <div className="w-14 text-center">
          <p className="text-xl font-bold text-text-primary">{currentQty}</p>
        </div>

        {/* Plus button — only add back up to ordered qty */}
        <button
          type="button"
          onClick={handleIncrement}
          disabled={isIncrementDisabled}
          className={`w-10 h-10 rounded-full font-bold text-xl flex items-center justify-center
            transition-all active:scale-90 ${
              isIncrementDisabled
                ? 'bg-slate-100 text-slate-300 cursor-not-allowed'
                : 'bg-slate-200 text-text-primary'
            }`}
        >
          +
        </button>
      </div>
    </div>
  );
};
