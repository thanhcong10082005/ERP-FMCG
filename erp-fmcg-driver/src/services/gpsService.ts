// ============================================================
// GPS Service — Geolocation + distance calculation
// ============================================================

export interface GeoPosition {
  latitude: number;
  longitude: number;
  accuracy?: number;
  timestamp: number;
}

export interface GPSCheckResult {
  isValid: boolean;
  distanceMeters?: number;
  currentPosition?: GeoPosition;
  error?: string;
}

const GPS_TIMEOUT_MS = 10_000;

function haversineDistance(
  lat1: number,
  lon1: number,
  lat2: number,
  lon2: number,
): number {
  const R = 6371000; // Earth radius in meters
  const toRad = (deg: number) => (deg * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const a =
    Math.sin(dLat / 2) * Math.sin(dLat / 2) +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) *
    Math.sin(dLon / 2) * Math.sin(dLon / 2);
  const c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
  return R * c;
}

export { haversineDistance };

export async function getCurrentPosition(): Promise<GeoPosition> {
  return new Promise((resolve, reject) => {
    if (!navigator.geolocation) {
      reject(new Error('Geolocation không được hỗ trợ trên thiết bị này'));
      return;
    }

    // Use an internal AbortController so we can kill the watchPosition too.
    let watched = false;
    const watchId = navigator.geolocation.watchPosition(
      (pos) => {
        watched = true;
        resolve({
          latitude: pos.coords.latitude,
          longitude: pos.coords.longitude,
          accuracy: pos.coords.accuracy,
          timestamp: pos.timestamp,
        });
      },
      (err) => {
        watched = true;
        reject(new Error(`GPS lỗi: ${err.message}`));
      },
      {
        enableHighAccuracy: true,
        timeout: GPS_TIMEOUT_MS,
        maximumAge: 0,
      },
    );

    // Fallback: if watchPosition never calls back (desktop/no-GPS browsers),
    // reject after the timeout so the promise doesn't hang forever.
    const fallbackTimer = setTimeout(() => {
      if (!watched) {
        navigator.geolocation.clearWatch(watchId);
        reject(new Error('Không lấy được vị trí — có thể GPS chưa bật hoặc trình duyệt không hỗ trợ'));
      }
    }, GPS_TIMEOUT_MS);

    // Clean up the fallback timer as soon as one of the callbacks fires.
    const originalResolve = resolve;
    const originalReject = reject;
    resolve = (...args: Parameters<typeof originalResolve>) => {
      clearTimeout(fallbackTimer);
      originalResolve(...args);
    };
    reject = (...args: Parameters<typeof originalReject>) => {
      clearTimeout(fallbackTimer);
      originalReject(...args);
    };
  });
}

export async function validateGPSGate(
  partnerLat?: number,
  partnerLon?: number,
  maxDistanceMeters = 100,
): Promise<GPSCheckResult> {
  if (partnerLat == null || partnerLon == null) {
    return {
      isValid: true,
      error: 'Không có tọa độ khách hàng — bỏ qua GPS Gate',
    };
  }

  try {
    const current = await getCurrentPosition();
    const distance = haversineDistance(
      current.latitude,
      current.longitude,
      partnerLat,
      partnerLon,
    );
    return {
      isValid: distance <= maxDistanceMeters,
      distanceMeters: Math.round(distance),
      currentPosition: current,
    };
  } catch (err: unknown) {
    return {
      isValid: false,
      error: err instanceof Error ? err.message : 'Lỗi GPS',
    };
  }
}
