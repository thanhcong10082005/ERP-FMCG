// ============================================================
// Formatters
// ============================================================

export function formatCurrency(amount: number | string): string {
  const num = typeof amount === 'string' ? parseFloat(amount) : amount;
  if (isNaN(num)) return '0 đ';
  return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND', maximumFractionDigits: 0 }).format(num);
}

export function formatPhone(phone: string | undefined): string {
  if (!phone) return '—';
  return phone.replace(/(\d{3})(\d{3})(\d{4})/, '$1 $2 $3');
}

export function formatDate(date: string | Date): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  return d.toLocaleDateString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  });
}

export function formatTime(date: string | Date): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  return d.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' });
}

export function formatDateTime(date: string | Date): string {
  return `${formatDate(date)} ${formatTime(date)}`;
}

export function distanceToText(meters: number): string {
  if (meters < 1000) return `${Math.round(meters)}m`;
  return `${(meters / 1000).toFixed(1)}km`;
}

export function getStopLabel(index: number): string {
  return `${index + 1}`;
}

export function statusColor(status: string): string {
  switch (status) {
    case 'PENDING':    return 'text-yellow-600 bg-yellow-50 border-yellow-200';
    case 'DELIVERED':  return 'text-success bg-success-50 border-success-200';
    case 'PARTIAL':    return 'text-warning bg-warning-50 border-warning-200';
    case 'FAILED':     return 'text-danger bg-danger-50 border-danger-200';
    default:           return 'text-slate-500 bg-slate-50 border-slate-200';
  }
}

export function statusLabel(status: string): string {
  switch (status) {
    case 'PENDING':   return 'Chưa giao';
    case 'DELIVERED': return 'Đã giao';
    case 'PARTIAL':   return 'Giao một phần';
    case 'FAILED':    return 'Không giao được';
    default:          return status;
  }
}
