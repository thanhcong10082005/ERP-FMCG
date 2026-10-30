namespace ERP.FMC.Application.Services;

// ===================================================================
// Lỗi nghiệp vụ chống gian lận
// -------------------------------------------------------------------
// Tách riêng khỏi `ArgumentException` để client nhận được mã lỗi
// CHI TIẾT (OUT_OF_DELIVERY_WINDOW / GPS_GATE_FAILED / BACKDATED_TRIP)
// thay vì chung chung "INVALID_ARGUMENT".
//
// Driver App dùng mã lỗi để hiển thị đúng thông báo cho tài xế và
// đưa ra hướng xử lý (bật GPS lại, chờ đến giờ giao, ...).
// ===================================================================

/// <summary>
/// Ném ra khi POD vi phạm chốt chặn chống gian lận server-side.
/// HTTP 400 với `error` = mã lỗi cụ thể.
/// </summary>
public class AntiFraudException : Exception
{
    /// <summary>Mã lỗi: OUT_OF_DELIVERY_WINDOW | GPS_GATE_FAILED | BACKDATED_TRIP | TIMESTAMP_OUT_OF_RANGE.</summary>
    public string ErrorCode { get; }

    /// <summary>Khoảng cách tới khách (mét) — chỉ có với lỗi GPS.</summary>
    public int? DistanceMeters { get; }

    public AntiFraudException(string errorCode, string message, int? distanceMeters = null)
        : base(message)
    {
        ErrorCode = errorCode;
        DistanceMeters = distanceMeters;
    }
}
