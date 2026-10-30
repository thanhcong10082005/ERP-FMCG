using ERP.FMC.Domain.Entities;

namespace ERP.FMC.Application.Services;

// ===================================================================
// CHỐNG GIAN LẬN SERVER-SIDE
// -------------------------------------------------------------------
// Đưa 3 chốt chặn từ client lên SERVER.
//
// Lý do BẮT BUỘC:
//   Client-side guard chỉ là "lớp UX". Tài xế có thể sửa JS, dùng
//   fake GPS app, đổi đồng hồ máy, hoặc gọi thẳng API bằng curl.
//   Chốt chặn chỉ có tác dụng khi nằm ở SERVER.
//
// 3 chốt chặn:
//   1. TIME-LOCK    — giờ Việt Nam (UTC+7) trong 05:00–22:00.
//                    Dùng `TimeZoneInfo` vì server có thể chạy UTC.
//   2. GPS GATE     — khoảng cách tới khách <= ngưỡng, HOẶC partner
//                    không có GPS (khách chưa được geocode).
//   3. NO BACKDATE  — trip phải thuộc hôm nay (giờ VN) và
//                    `gps_captured_at` không lệch quá ±15 phút so với
//                    thời điểm server nhận.
//
// ★ Lưu ý về `gps_captured_at`:
//   Do CLIENT gửi lên ⇒ có thể giả mạo. Vì vậy:
//     - Mốc CHÍNH thời gian là `serverReceivedAt` (DateTime.UtcNow).
//     - `gps_captured_at` chỉ dùng để ĐỐI CHIẾU (cross-check).
//     - Nếu lệch > 15 phút ⇒ ghi `TIMESTAMP_MISMATCH` vào audit log
//       nhưng KHÔNG chặn (vì client offline có thể gửi trễ hợp lệ).
// ===================================================================

/// <summary>Kết quả kiểm tra chống gian lận.</summary>
public class AntiFraudResult
{
    /// <summary>true = đạt (được phép ghi POD).</summary>
    public bool Allowed { get; set; }

    /// <summary>Mã lỗi: OUT_OF_DELIVERY_WINDOW | GPS_GATE_FAILED | BACKDATED_TRIP | TIMESTAMP_OUT_OF_RANGE.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Thông báo tiếng Việt cho UI.</summary>
    public string? Message { get; set; }

    /// <summary>Giá trị ghi vào `delivery_pod.gps_gate_result`.</summary>
    public string GpsGateResult { get; set; } = "SKIPPED";

    /// <summary>Giá trị ghi vào `delivery_pod.time_guard_result`.</summary>
    public string TimeGuardResult { get; set; } = "SKIPPED";

    /// <summary>Khoảng cách (mét) tới khách — null nếu không tính được.</summary>
    public int? DistanceMeters { get; set; }

    /// <summary>Giờ Việt Nam tại thời điểm kiểm tra (ISO-8601).</summary>
    public string VietnamTimeIso { get; set; } = string.Empty;

    /// <summary>Sai số (phút) giữa `gps_captured_at` và thời điểm server nhận.</summary>
    public int? TimestampDriftMinutes { get; set; }
}

public interface IAntiFraudService
{
    /// <summary>Kiểm tra đủ 3 chốt chặn. Trả về kết quả quyết định có ghi POD hay không.</summary>
    AntiFraudResult Validate(
        DeliveryTrip trip,
        Partner? partner,
        double? deliveryLatitude,
        double? deliveryLongitude,
        DateTime? gpsCapturedAt,
        DateTime serverReceivedAt,
        bool enforce);

    /// <summary>Giờ hiện tại theo múi giờ Việt Nam (UTC+7).</summary>
    DateTime GetVietnamNow();

    /// <summary>Thời điểm server nhận request (UTC).</summary>
    DateTime GetServerUtcNow();
}

public class AntiFraudService : IAntiFraudService
{
    /// <summary>Khung giờ giao hàng (giờ Việt Nam).</summary>
    public const int DeliveryStartHour = 5;   // 05:00
    public const int DeliveryEndHour = 22;    // 22:00

    /// <summary>Ngưỡng GPS gate (mét).</summary>
    public const int GpsGateRadiusM = 100;

    /// <summary>Sai số thời gian cho phép (phút) giữa GPS và server.</summary>
    public const int TimestampToleranceMinutes = 15;

    /// <summary>
    /// ID múi giờ Windows cho giờ Việt Nam.
    /// Trên Linux/macOS dùng "Asia/Ho_Chi_Minh".
    /// </summary>
    private static readonly Lazy<TimeZoneInfo> _tz = new(() =>
    {
        foreach (var id in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { /* thử id tiếp theo */ }
            catch (InvalidTimeZoneException) { /* thử id tiếp theo */ }
        }
        // Fallback: cố định UTC+7 (không có DST tại Việt Nam).
        return TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "UTC+07", "UTC+07");
    });

    public DateTime GetVietnamNow()
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz.Value);

    public DateTime GetServerUtcNow() => DateTime.UtcNow;

    public AntiFraudResult Validate(
        DeliveryTrip trip,
        Partner? partner,
        double? deliveryLatitude,
        double? deliveryLongitude,
        DateTime? gpsCapturedAt,
        DateTime serverReceivedAt,
        bool enforce)
    {
        var vnNow = TimeZoneInfo.ConvertTimeFromUtc(serverReceivedAt, _tz.Value);
        var vnToday = vnNow.Date;

        var result = new AntiFraudResult
        {
            Allowed = true,
            VietnamTimeIso = vnNow.ToString("yyyy-MM-ddTHH:mm:ss"),
        };

        // =============================================================
        // CHỐT 3b — TIMESTAMP: gps_captured_at lệch quá 15 phút
        //   → KHÔNG chặn (offline hợp lệ có thể gửi trễ), chỉ ghi cảnh báo.
        //   Luôn ghi lại `driftMinutes` để audit đối chiếu được.
        // =============================================================
        if (gpsCapturedAt.HasValue)
        {
            result.TimestampDriftMinutes = (int)Math.Round(
                Math.Abs((serverReceivedAt - gpsCapturedAt.Value).TotalMinutes));
        }

        // =============================================================
        // CHỐT 1 — TIME-LOCK (05:00–22:00 giờ Việt Nam)
        // =============================================================
        var hour = vnNow.Hour;
        if (hour < DeliveryStartHour || hour >= DeliveryEndHour)
        {
            result.TimeGuardResult = "FAIL";
            result.Allowed = false;
            result.ErrorCode = "OUT_OF_DELIVERY_WINDOW";
            result.Message =
                $"Ngoài giờ giao hàng (giờ Việt Nam {vnNow:HH:mm}). " +
                $"Giờ giao hợp lệ: {DeliveryStartHour:00}:00–{DeliveryEndHour:00}:00.";
            return Finalize(result, enforce);
        }

        // =============================================================
        // CHỐT 3a — NO BACKDATE: trip phải thuộc hôm nay (giờ VN)
        // =============================================================
        if (trip.TripDate.Date != vnToday)
        {
            result.TimeGuardResult = "FAIL_BACKDATE";
            result.Allowed = false;
            result.ErrorCode = "BACKDATED_TRIP";
            result.Message =
                $"Chuyến xe thuộc ngày {trip.TripDate:yyyy-MM-dd}, " +
                $"nhưng hôm nay là {vnToday:yyyy-MM-dd} (giờ Việt Nam). " +
                "Không được giao lại chuyến của ngày trước.";
            return Finalize(result, enforce);
        }

        result.TimeGuardResult = (result.TimestampDriftMinutes ?? 0) > TimestampToleranceMinutes
            ? "TIMESTAMP_MISMATCH"
            : "PASS";

        // =============================================================
        // CHỐT 2 — GPS GATE
        //   PASS nếu: partner không có GPS, HOẶC tài xế trong bán kính.
        //   FAIL nếu: partner có GPS mà tài xế nằm ngoài bán kính.
        // =============================================================
        var partnerHasGps =
            partner?.Latitude is not null && partner.Longitude is not null;

        if (!partnerHasGps)
        {
            // 20/2.723 khách chưa có GPS ⇒ không có mốc để so ⇒ cho qua.
            result.GpsGateResult = "SKIPPED_NO_PARTNER_GPS";
        }
        else if (deliveryLatitude is null || deliveryLongitude is null)
        {
            // Khách CÓ GPS nhưng client không gửi GPS ⇒ không xác minh được.
            // Đây là dấu hiệu gian lận nghiêm trọng ⇒ chặn.
            result.GpsGateResult = "FAIL_NO_GPS";
            result.Allowed = false;
            result.ErrorCode = "GPS_GATE_FAILED";
            result.Message =
                "Khách hàng có tọa độ nhưng phiếu giao không kèm GPS. " +
                "Vui lòng bật GPS và giao lại.";
            return Finalize(result, enforce);
        }
        else
        {
            var distance = HaversineMeters(
                deliveryLatitude.Value,
                deliveryLongitude.Value,
                (double)partner!.Latitude!.Value,
                (double)partner.Longitude!.Value);

            var meters = (int)Math.Round(distance);
            result.DistanceMeters = meters;

            if (meters <= GpsGateRadiusM)
            {
                result.GpsGateResult = "PASS";
            }
            else
            {
                result.GpsGateResult = "FAIL";
                result.Allowed = false;
                result.ErrorCode = "GPS_GATE_FAILED";
                result.Message =
                    $"Bạn đang cách vị trí khách hàng {meters}m " +
                    $"(yêu cầu <= {GpsGateRadiusM}m).";
            }
        }

        return Finalize(result, enforce);
    }

    /// <summary>
    /// Chuẩn hoá kết quả theo chế độ siết:
    ///   - <c>enforce = true</c>  → trả nguyên kết quả (chặn nếu vi phạm).
    ///   - <c>enforce = false</c> → KHÔNG chặn (môi trường demo/UAT),
    ///     nhưng giữ lại thông báo để audit theo dõi.
    /// </summary>
    private static AntiFraudResult Finalize(AntiFraudResult result, bool enforce)
    {
        if (!enforce && !result.Allowed)
        {
            result.Allowed = true;
            result.Message = $"[KHÔNG SIẾT] {result.Message}";
            result.ErrorCode = null;
        }
        return result;
    }

    /// <summary>Khoảng cách Haversine (mét).</summary>
    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusM = 6_371_000;
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusM * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double deg) => deg * Math.PI / 180.0;
}
