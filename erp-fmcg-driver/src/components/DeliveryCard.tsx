// ============================================================
// DeliveryCard — Một điểm giao trong danh sách
// ============================================================

import React from 'react';
import { Link } from 'react-router-dom';
import type { TripOrder } from '../types';
import { formatCurrency, statusLabel } from '../utils/formatters';

interface Props {
  order: TripOrder;
  index: number;
  isActive?: boolean;
}

const statusBgClass: Record<string, string> = {
  PENDING: 'border-l-4 border-l-yellow-400',
  DELIVERED: 'border-l-4 border-l-green-500',
  PARTIAL: 'border-l-4 border-l-orange-400',
  FAILED: 'border-l-4 border-l-red-500',
};

export const DeliveryCard: React.FC<Props> = ({ order, index, isActive }) => {
  const totalPayable =
    (order.total_amount || 0) + (order.old_debt_amount || 0);

  const phoneLink = order.phone
    ? `tel:${order.phone}`
    : undefined;

  const mapsLink = order.latitude && order.longitude
    ? `https://www.google.com/maps/dir/?api=1&destination=${order.latitude},${order.longitude}`
    : undefined;

  return (
    <div
      className={`card mb-3 overflow-hidden transition-all ${statusBgClass[order.status] || ''} ${
        isActive ? 'ring-2 ring-primary' : ''
      }`}
    >
      {/* Header: Stop # + Status */}
      <div className="flex items-center justify-between px-4 pt-3 pb-2">
        <div className="flex items-center gap-2">
          <span className="w-7 h-7 rounded-full bg-primary text-white text-sm font-bold flex items-center justify-center">
            {index + 1}
          </span>
          <span className="font-semibold text-text-primary">{order.so_number}</span>
        </div>
        <span className={`badge ${
          order.status === 'DELIVERED' ? 'badge-delivered' :
          order.status === 'PARTIAL' ? 'badge-partial' :
          order.status === 'FAILED' ? 'badge-failed' :
          'badge-pending'
        }`}>
          {statusLabel(order.status)}
        </span>
      </div>

      {/* Partner info */}
      <div className="px-4 pb-3 border-t border-border pt-2">
        <p className="font-medium text-text-primary">{order.partner_name}</p>
        <p className="text-sm text-text-secondary mt-0.5">
          {order.address_line || 'Không có địa chỉ'}
        </p>

        {/* Amounts */}
        <div className="flex gap-4 mt-2">
          <div>
            <p className="text-xs text-text-secondary">Tiền đơn</p>
            <p className="text-sm font-semibold text-text-primary">
              {formatCurrency(order.total_amount)}
            </p>
          </div>
          {(order.old_debt_amount ?? 0) > 0 && (
            <div>
              <p className="text-xs text-text-secondary">Công nợ cũ</p>
              <p className="text-sm font-semibold text-danger">
                {formatCurrency(order.old_debt_amount)}
              </p>
            </div>
          )}
          <div>
            <p className="text-xs text-text-secondary">Phải thu</p>
            <p className="text-sm font-bold text-primary">
              {formatCurrency(totalPayable)}
            </p>
          </div>
        </div>

        {/* Actions */}
        <div className="flex gap-2 mt-3">
          {phoneLink && (
            <a
              href={phoneLink}
              className="btn btn-outline flex-1 text-sm py-2 flex items-center justify-center gap-1"
            >
              <PhoneIcon />
              Gọi
            </a>
          )}
          {mapsLink && (
            <a
              href={mapsLink}
              target="_blank"
              rel="noopener noreferrer"
              className="btn btn-outline flex-1 text-sm py-2 flex items-center justify-center gap-1"
            >
              <MapIcon />
              Bản đồ
            </a>
          )}
          {order.status === 'PENDING' && (
            <Link
              to={`/deliver/${order.trip_id}/${order.trip_order_id}`}
              className="btn btn-primary flex-1 text-sm py-2"
            >
              Giao hàng
            </Link>
          )}
        </div>
      </div>
    </div>
  );
};

const PhoneIcon = () => (
  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M3 5a2 2 0 012-2h3.28a1 1 0 01.948.684l1.498 4.493a1 1 0 01-.502 1.21l-2.257 1.13a11.042 11.042 0 005.516 5.516l1.13-2.257a1 1 0 011.21-.502l4.493 1.498a1 1 0 01.684.949V19a2 2 0 01-2 2h-1C9.716 21 3 14.284 3 6V5z" />
  </svg>
);

const MapIcon = () => (
  <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M17.657 16.657L13.414 20.9a1.998 1.998 0 01-2.827 0l-4.244-4.243a8 8 0 1111.314 0z" />
    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2}
      d="M15 11a3 3 0 11-6 0 3 3 0 016 0z" />
  </svg>
);
