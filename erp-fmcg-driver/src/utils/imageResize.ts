// ============================================================
// Image Resize — nén ảnh & chữ ký trước khi upload
// ------------------------------------------------------------
//
// Triệu chứng
//   Ảnh chụp từ camera điện thoại có dung lượng 2–5 MB. Chuyển sang
//   base64 (DataURL) sẽ thành ~2,7–6,7 MB (base64 phình ~33%).
//   POST /api/sales/pod trên mạng 4G chậm, dễ timeout → tài xế bấm
//   "Giao hàng" lại → sinh POD trùng.
//
//   Ngoài ra `AuditLogMiddleware` buffer toàn bộ body request vào RAM
//   để ghi audit log => mỗi request 6 MB là 6 MB RAM trên server.
//
// Cách sửa
//   Resize về cạnh dài tối đa 1000px + JPEG quality 0.7 trước khi
//   gửi. Ảnh giảm còn ~80–150 KB mà vẫn đủ làm bằng chứng.
//
// Lưu ý
//   - Hàm bất đồng bộ, dùng `await` trước khi set state.
//   - Nếu trình duyệt không hỗ trợ Canvas/Image (rất hiếm), trả về
//     input gốc để không mất dữ liệu.
// ============================================================

const DEFAULT_MAX_DIM = 1000;
const DEFAULT_QUALITY = 0.7;

/**
 * Resize ảnh base64 về kích thước tối đa và nén JPEG.
 *
 * @param dataUrl  DataURL của ảnh gốc (image/png hoặc image/jpeg).
 * @param maxDim   Cạnh dài tối đa (px).
 * @param quality  Chất lượng JPEG (0..1).
 * @returns        DataURL JPEG đã nén; nếu lỗi → trả về `dataUrl` gốc.
 */
export async function resizeImage(
  dataUrl: string,
  maxDim: number = DEFAULT_MAX_DIM,
  quality: number = DEFAULT_QUALITY,
): Promise<string> {
  if (!dataUrl || !dataUrl.startsWith('data:')) return dataUrl;

  try {
    const img = await loadImage(dataUrl);

    const width = img.naturalWidth || img.width;
    const height = img.naturalHeight || img.height;
    if (!width || !height) return dataUrl;

    // Không cần resize nếu ảnh nhỏ — nhưng vẫn nén lại để giảm base64.
    const scale = Math.min(1, maxDim / Math.max(width, height));
    const targetW = Math.max(1, Math.round(width * scale));
    const targetH = Math.max(1, Math.round(height * scale));

    const canvas = document.createElement('canvas');
    canvas.width = targetW;
    canvas.height = targetH;

    const ctx = canvas.getContext('2d');
    if (!ctx) return dataUrl;

    // Nền trắng: ảnh PNG trong suốt -> JPEG sẽ nền đen nếu không fill.
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, targetW, targetH);
    ctx.drawImage(img, 0, 0, targetW, targetH);

    return canvas.toDataURL('image/jpeg', quality);
  } catch (err) {
    console.warn('[imageResize] resize thất bại — dùng ảnh gốc:', err);
    return dataUrl;
  }
}

function loadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error('Không đọc được ảnh'));
    img.src = src;
  });
}

/**
 * Ước lượng kích thước (byte) của một DataURL base64.
 * Dùng để hiển thị cảnh báo cho người dùng.
 */
export function dataUrlBytes(dataUrl: string): number {
  if (!dataUrl) return 0;
  const commaIdx = dataUrl.indexOf(',');
  if (commaIdx < 0) return 0;
  const b64 = dataUrl.slice(commaIdx + 1);
  const padding = b64.endsWith('==') ? 2 : b64.endsWith('=') ? 1 : 0;
  return Math.floor((b64.length * 3) / 4) - padding;
}

/** Format byte cho UI (KB / MB). */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
}
