// ===================================================================
// DebtSignConventions — QUY ƯỚC DẤU CÔNG NỢ (đơn vị nghiệp vụ)
// ===================================================================
// ★ TÀI LIỆU CHUẨN DUY NHẤT cho mọi nơi đọc/ghi `old_debt` & `current_debt`.
//
// Bối cảnh
// ---------
// `old_debt` / `current_debt` được NẠP TỪ FILE GỐC ERP CŨ
// Trong file đó, số ÂM được ghi bằng ngoặc đơn:
//     (660.000)  ⇒  -660.000
// Số âm ở đây KHÔNG phải "nợ âm" mà là SỐ DƯƠNG của ERP: khách đã
// TRẢ TRƯỚC, ERP đang giữ tiền của khách (cộng nợ bên khách).
//
// Quy ước (đã đối chiếu với file nguồn + temporal history)
// ------------------------------------------------------
//   old_debt > 0  ⇒ khách NỢ, phải thu.
//   old_debt < 0  ⇒ khách ĐÃ TRẢ TRƯỚC, ERP giữ tiền của khách.
//   old_debt = 0  ⇒ không có công nợ cũ.
//
// Hệ quả (bug đã xảy ra 2026-11-02)
// ---------------------------------
//   Code cũ: `partner.OldDebt = partner.OldDebt - thu; if (OldDebt < 0) OldDebt = 0;`
//   ⇒ MỘT POD thu 0 đồng cũng xoá sạch số dư dương của khách.
//   ⇒ Đã xác nhận mất 660.000đ ở KH CT00608580.
//   ⇒ Check D12 ("số KH old_debt <> 0") rớt 480 → 479.
//
// NGUYÊN TẮC (bắt buộc tuân thủ)
// --------------------------------
//   1. CHỈ được coi là "nợ" khi giá trị > 0.
//   2. KHÔNG BAO GIỜ clamp âm → 0 một cách vô điều kiện.
//   3. Muốn giảm nợ cũ ⇒ trừ trên phần DƯƠNG, giữ nguyên phần âm.
//   4. Muốn tính "nợ còn lại" ⇒ dùng hàm OutstandingDebt() bên dưới.
// ===================================================================

namespace ERP.FMC.Application.Services;

/// <summary>
/// Quy ước dấu công nợ — xem file doc đầu để hiểu đầy đủ.
/// </summary>
public static class DebtSignConventions
{
    /// <summary>
    /// Nợ cũ THỰC SỰ còn lại của khách (chỉ phần dương).
    /// Khách có số dư dương (old_debt &lt; 0) ⇒ trả về 0 (họ không nợ gì).
    /// </summary>
    public static decimal OutstandingDebt(decimal debt) => debt > 0m ? debt : 0m;

    /// <summary>
    /// Số dư dương khách đang giữ (khách trả trước). Trả về 0 nếu không có.
    /// </summary>
    public static decimal CreditBalance(decimal debt) => debt < 0m ? -debt : 0m;

    /// <summary>Khách này có đang nợ không?</summary>
    public static bool HasOutstandingDebt(decimal debt) => debt > 0m;

    /// <summary>Khách này có số dư dương (đã trả trước) không?</summary>
    public static bool HasCreditBalance(decimal debt) => debt < 0m;
}
