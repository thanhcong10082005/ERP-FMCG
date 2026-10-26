// ============================================================
// Time Guard — 5h-22h delivery window
// ============================================================

export interface TimeGuardResult {
  allowed: boolean;
  currentHour: number;
  message?: string;
}

const DELIVERY_START_HOUR = 5;  // 05:00
const DELIVERY_END_HOUR = 22;   // 22:00

export function checkTimeGuard(): TimeGuardResult {
  const now = new Date();
  const hour = now.getHours();

  if (hour < DELIVERY_START_HOUR) {
    return {
      allowed: false,
      currentHour: hour,
      message: `Chưa đến giờ giao hàng. Giờ giao: ${DELIVERY_START_HOUR}h - ${DELIVERY_END_HOUR}h.`,
    };
  }

  if (hour >= DELIVERY_END_HOUR) {
    return {
      allowed: false,
      currentHour: hour,
      message: `Đã hết giờ giao hàng. Giờ giao: ${DELIVERY_START_HOUR}h - ${DELIVERY_END_HOUR}h.`,
    };
  }

  return { allowed: true, currentHour: hour };
}

export function isSameDay(date: string | Date): boolean {
  const d = typeof date === 'string' ? new Date(date) : date;
  const today = new Date();
  return (
    d.getFullYear() === today.getFullYear() &&
    d.getMonth() === today.getMonth() &&
    d.getDate() === today.getDate()
  );
}
