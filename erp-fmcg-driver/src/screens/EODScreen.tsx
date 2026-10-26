// ============================================================
// EODScreen — Step 6: End of Day Report
// ============================================================

import React, { useEffect, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { formatCurrency } from '../utils/formatters';
import type { EODReport } from '../types';

export const EODScreen: React.FC = () => {
  const location = useLocation();
  const navigate = useNavigate();

  const report: EODReport | null = location.state?.report || null;
  const [loading, setLoading] = useState(!report);

  useEffect(() => {
    if (!report) {
      setLoading(false);
    }
  }, [report]);

  const handleEndShift = () => {
    navigate('/home', { replace: true });
  };

  if (loading || !report) {
    return (
      <div className="screen flex items-center justify-center">
        <div className="text-center">
          <div className="w-12 h-12 border-4 border-primary border-t-transparent rounded-full animate-spin mx-auto mb-4" />
          <p className="text-text-secondary">Đang tổng hợp...</p>
        </div>
      </div>
    );
  }

  const isPartial = report.status === 'PARTIAL_DELIVERED';

  return (
    <div className="screen flex flex-col">
      {/* Header */}
      <header className="header safe-top flex-col items-start gap-1 py-4">
        <p className="text-blue-200 text-sm">Báo cáo ca làm việc</p>
        <h1 className="text-xl font-extrabold text-white">{report.trip_number}</h1>
        <span className={`badge mt-1 ${
          isPartial ? 'badge-partial' : 'badge-delivered'
        }`}>
          {isPartial ? 'Giao một phần' : 'Hoàn thành'}
        </span>
      </header>

      {/* Summary cards */}
      <div className="flex-1 overflow-y-auto px-4 py-4 space-y-4">
        {/* Trip stats */}
        <div className="card p-4">
          <h3 className="text-sm font-semibold text-text-secondary uppercase mb-3">Tổng quan chuyến</h3>
          <div className="space-y-3">
            <div className="flex justify-between">
              <span className="text-text-secondary">Tổng điểm giao</span>
              <span className="font-semibold">{report.total_orders}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-text-secondary">Đã giao</span>
              <span className="font-semibold text-success">{report.delivered_count}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-text-secondary">Không giao được</span>
              <span className={`font-semibold ${report.failed_count > 0 ? 'text-danger' : 'text-text-secondary'}`}>
                {report.failed_count}
              </span>
            </div>
          </div>
        </div>

        {/* Cash summary */}
        <div className="card p-4">
          <h3 className="text-sm font-semibold text-text-secondary uppercase mb-3">
            💵 Tiền cần nộp quỹ
          </h3>
          <div className="space-y-3">
            <div className="flex justify-between items-center">
              <span className="text-text-secondary">Tiền mặt</span>
              <span className="text-xl font-bold text-success">
                {formatCurrency(report.total_cash)}
              </span>
            </div>
            <div className="flex justify-between items-center">
              <span className="text-text-secondary">Chuyển khoản</span>
              <span className="text-xl font-bold text-primary">
                {formatCurrency(report.total_transfer)}
              </span>
            </div>
            <div className="flex justify-between items-center">
              <span className="text-text-secondary">Công nợ cũ đã thu</span>
              <span className="font-semibold text-text-primary">
                {formatCurrency(report.total_old_debt)}
              </span>
            </div>
            <div className="border-t border-border pt-3 flex justify-between items-center">
              <span className="font-bold text-text-primary">Tổng thu</span>
              <span className="text-2xl font-extrabold text-success">
                {formatCurrency(report.total_collected)}
              </span>
            </div>
          </div>
        </div>

        {/* Returned items */}
        {report.total_returned_qty > 0 && (
          <div className="card p-4 border-l-4 border-l-warning">
            <h3 className="text-sm font-semibold text-warning mb-2">
              ⚠️ Hàng rớt cần trả kho
            </h3>
            <p className="text-sm text-text-secondary">
              Có <strong>{report.total_returned_qty}</strong> sản phẩm chưa giao được.
              Vui lòng trả lại kho khi về.
            </p>
          </div>
        )}

        {/* Success message */}
        {!isPartial && (
          <div className="card p-6 text-center border-l-4 border-l-success">
            <div className="w-16 h-16 bg-success-50 rounded-full flex items-center justify-center mx-auto mb-3">
              <SuccessIcon />
            </div>
            <h2 className="text-lg font-bold text-success mb-1">Chuyến giao hoàn tất!</h2>
            <p className="text-sm text-text-secondary">
              Tất cả điểm giao đã được hoàn thành.
            </p>
          </div>
        )}

        {/* Warning if partial */}
        {isPartial && (
          <div className="card p-4 border-l-4 border-l-warning">
            <div className="flex gap-3">
              <WarningIcon />
              <div>
                <h3 className="font-semibold text-warning mb-1">Có đơn chưa giao được</h3>
                <p className="text-sm text-text-secondary">
                  {report.failed_count} đơn không giao được. Dispatcher sẽ được thông báo để sắp xếp lại.
                </p>
              </div>
            </div>
          </div>
        )}
      </div>

      {/* End shift button */}
      <div className="px-4 pb-6 safe-bottom">
        <button
          type="button"
          className="btn btn-primary w-full text-lg py-4 rounded-2xl"
          onClick={handleEndShift}
        >
          Kết thúc ca làm việc
        </button>
      </div>
    </div>
  );
};

const SuccessIcon = () => (
  <svg className="w-8 h-8 text-success" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M5 13l4 4L19 7" />
  </svg>
);

const WarningIcon = () => (
  <svg className="w-6 h-6 text-warning flex-shrink-0 mt-1" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
  </svg>
);
