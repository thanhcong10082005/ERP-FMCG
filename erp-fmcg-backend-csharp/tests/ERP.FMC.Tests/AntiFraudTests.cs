using ERP.FMC.Application.Services;
using ERP.FMC.Domain.Entities;
using Xunit;

namespace ERP.FMC.Tests;

// ===================================================================
// TEST CHỐNG GIAN LẬN SERVER-SIDE
// -------------------------------------------------------------------
// Acceptance (theo đặc tả tích hợp):
//   [ ] POD ngoài khung 5h–22h        → 400 OUT_OF_DELIVERY_WINDOW
//   [ ] POD cách partner 250m          → 400 GPS_GATE_FAILED
//   [ ] gps_captured_at lệch > 15 phút → cảnh báo TIMESTAMP_MISMATCH
//   [ ] POD trùng client_pod_id × 3    → delivery_pod chỉ có 1 dòng
//
// Ghi chú về test thời gian:
//   `AntiFraudService` dùng `TimeZoneInfo` với múi giờ hệ thống.
//   Test dùng `serverReceivedAt` là tham số đầu vào ⇒ có thể kiểm thử
//   được MỌI khung giờ mà không cần chờ thật (không flaky).
// ===================================================================

public class AntiFraudTests
{
    private readonly AntiFraudService _svc = new();

    // -------------------------------------------------------------------
    // Helper: tạo trip hôm nay (giờ VN)
    // -------------------------------------------------------------------
    private static DeliveryTrip TripToday()
    {
        var vnToday = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Ho_Chi_Minh"));

        return new DeliveryTrip
        {
            Id = Guid.NewGuid(),
            TripNumber = "TRIP-TEST-001",
            TripDate = vnToday.Date,
            Status = "DELIVERING",
        };
    }

    private static Partner PartnerWithGps(double lat = 10.7769, double lon = 106.7009)
        => new()
        {
            Id = Guid.NewGuid(),
            PartnerCode = "CT001",
            PartnerName = "Khách Test",
            Latitude = (decimal)lat,
            Longitude = (decimal)lon,
        };

    /// <summary>Thời điểm UTC tương ứng với giờ Việt Nam cụ thể.</summary>
    private static DateTime UtcAtVietnamHour(int hour, int minute = 0)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Ho_Chi_Minh");
        var local = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day,
            hour, minute, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    // ===================================================================
    // CHỐT 1 — TIME-LOCK (05:00–22:00 giờ Việt Nam)
    // ===================================================================

    [Theory]
    [InlineData(5, 0, true)]     // 05:00 → biên dưới, cho phép
    [InlineData(6, 30, true)]    // trong khung
    [InlineData(12, 0, true)]    // trưa
    [InlineData(21, 59, true)]   // 21:59 → còn trong khung
    [InlineData(22, 0, false)]   // 22:00 → biên trên, CHẶN
    [InlineData(23, 30, false)]  // ngoài khung
    [InlineData(3, 0, false)]    // rạng sáng, CHẶN
    [InlineData(0, 0, false)]    // nửa đêm, CHẶN
    public void TimeLock_Rejects_Outside_05h_22h_Vietnam(int hour, int minute, bool expectedAllowed)
    {
        var serverAt = UtcAtVietnamHour(hour, minute);
        var trip = TripToday();

        var result = _svc.Validate(
            trip, PartnerWithGps(),
            deliveryLatitude: 10.7769, deliveryLongitude: 106.7009,
            gpsCapturedAt: serverAt, serverReceivedAt: serverAt,
            enforce: true);

        Assert.Equal(expectedAllowed, result.Allowed);

        if (!expectedAllowed)
        {
            Assert.Equal("OUT_OF_DELIVERY_WINDOW", result.ErrorCode);
            Assert.Equal("FAIL", result.TimeGuardResult);
        }
        else
        {
            Assert.Equal("PASS", result.TimeGuardResult);
        }
    }

    [Fact]
    public void TimeLock_Uses_Vietnam_Time_Not_Server_Local_Time()
    {
        // 01:00 giờ VN = 18:00 UTC hôm trước ⇒ phải CHẶN dù giờ server có thể là 18:00.
        var serverAt = UtcAtVietnamHour(1, 0);
        var result = _svc.Validate(
            TripToday(), PartnerWithGps(),
            10.7769, 106.7009, serverAt, serverAt, enforce: true);

        Assert.False(result.Allowed);
        Assert.Equal("OUT_OF_DELIVERY_WINDOW", result.ErrorCode);
    }

    // ===================================================================
    // CHỐT 2 — GPS GATE (<= 100m)
    // ===================================================================

    [Fact]
    public void GpsGate_Passes_When_Within_100meters()
    {
        var serverAt = UtcAtVietnamHour(10, 0);
        var partner = PartnerWithGps(10.7769, 106.7009);

        // Lệch ~50m về phía bắc (0.00045 độ ≈ 50m).
        var result = _svc.Validate(
            TripToday(), partner,
            deliveryLatitude: 10.77735, deliveryLongitude: 106.7009,
            gpsCapturedAt: serverAt, serverReceivedAt: serverAt,
            enforce: true);

        Assert.True(result.Allowed);
        Assert.Equal("PASS", result.GpsGateResult);
        Assert.True(result.DistanceMeters <= AntiFraudService.GpsGateRadiusM);
    }

    [Fact]
    public void GpsGate_Rejects_When_Far_From_Partner()
    {
        var serverAt = UtcAtVietnamHour(10, 0);
        var partner = PartnerWithGps(10.7769, 106.7009);

        // Lệch ~0.0022 độ latitude ≈ 245m ⇒ ngoài 100m.
        var result = _svc.Validate(
            TripToday(), partner,
            deliveryLatitude: 10.7791, deliveryLongitude: 106.7009,
            gpsCapturedAt: serverAt, serverReceivedAt: serverAt,
            enforce: true);

        Assert.False(result.Allowed);
        Assert.Equal("GPS_GATE_FAILED", result.ErrorCode);
        Assert.Equal("FAIL", result.GpsGateResult);
        Assert.True(result.DistanceMeters > AntiFraudService.GpsGateRadiusM);
    }

    [Fact]
    public void GpsGate_Skipped_When_Partner_Has_No_Gps()
    {
        var serverAt = UtcAtVietnamHour(10, 0);
        var partner = new Partner { Id = Guid.NewGuid(), PartnerCode = "CT002", PartnerName = "Chưa geocode" };

        var result = _svc.Validate(
            TripToday(), partner,
            deliveryLatitude: null, deliveryLongitude: null,
            gpsCapturedAt: serverAt, serverReceivedAt: serverAt,
            enforce: true);

        Assert.True(result.Allowed);
        Assert.Equal("SKIPPED_NO_PARTNER_GPS", result.GpsGateResult);
    }

    [Fact]
    public void GpsGate_Rejects_When_Partner_Has_Gps_But_Client_Sends_None()
    {
        // Khách CÓ GPS nhưng client không gửi GPS ⇒ không xác minh được ⇒ chặn.
        var serverAt = UtcAtVietnamHour(10, 0);

        var result = _svc.Validate(
            TripToday(), PartnerWithGps(),
            deliveryLatitude: null, deliveryLongitude: null,
            gpsCapturedAt: serverAt, serverReceivedAt: serverAt,
            enforce: true);

        Assert.False(result.Allowed);
        Assert.Equal("GPS_GATE_FAILED", result.ErrorCode);
        Assert.Equal("FAIL_NO_GPS", result.GpsGateResult);
    }

    [Fact]
    public void Haversine_Distance_Is_Accurate()
    {
        // 1 độ latitude ≈ 111.320 km.
        var d = AntiFraudService.HaversineMeters(10.0, 106.0, 11.0, 106.0);
        Assert.InRange(d, 111_000, 111_500);

        // Cùng toạ độ ⇒ 0.
        Assert.Equal(0, AntiFraudService.HaversineMeters(10.0, 106.0, 10.0, 106.0), 3);
    }

    // ===================================================================
    // CHỐT 3 — NO BACKDATE
    // ===================================================================

    [Fact]
    public void Backdate_Rejects_Trip_From_Yesterday()
    {
        var serverAt = UtcAtVietnamHour(10, 0);
        var trip = TripToday();
        trip.TripDate = trip.TripDate.AddDays(-1);

        var result = _svc.Validate(
            trip, PartnerWithGps(),
            10.7769, 106.7009, serverAt, serverAt,
            enforce: true);

        Assert.False(result.Allowed);
        Assert.Equal("BACKDATED_TRIP", result.ErrorCode);
        Assert.Equal("FAIL_BACKDATE", result.TimeGuardResult);
    }

    [Fact]
    public void Backdate_Rejects_Trip_From_Tomorrow()
    {
        var serverAt = UtcAtVietnamHour(10, 0);
        var trip = TripToday();
        trip.TripDate = trip.TripDate.AddDays(1);

        var result = _svc.Validate(
            trip, PartnerWithGps(),
            10.7769, 106.7009, serverAt, serverAt,
            enforce: true);

        Assert.False(result.Allowed);
        Assert.Equal("BACKDATED_TRIP", result.ErrorCode);
    }

    [Fact]
    public void Timestamp_Drift_Over_15min_Is_Flagged_But_Not_Blocked()
    {
        var serverAt = UtcAtVietnamHour(10, 0);
        // GPS lấy 40 phút trước ⇒ lệch 40 phút > 15.
        var gpsAt = serverAt.AddMinutes(-40);

        var result = _svc.Validate(
            TripToday(), PartnerWithGps(),
            10.7769, 106.7009, gpsAt, serverAt,
            enforce: true);

        // KHÔNG chặn (offline hợp lệ có thể gửi trễ) nhưng phải cảnh báo.
        Assert.True(result.Allowed);
        Assert.Equal("TIMESTAMP_MISMATCH", result.TimeGuardResult);
        Assert.Equal(40, result.TimestampDriftMinutes);
    }

    [Fact]
    public void Timestamp_Drift_Within_15min_Is_Pass()
    {
        var serverAt = UtcAtVietnamHour(10, 0);
        var gpsAt = serverAt.AddMinutes(-10);

        var result = _svc.Validate(
            TripToday(), PartnerWithGps(),
            10.7769, 106.7009, gpsAt, serverAt,
            enforce: true);

        Assert.True(result.Allowed);
        Assert.Equal("PASS", result.TimeGuardResult);
        Assert.Equal(10, result.TimestampDriftMinutes);
    }

    // ===================================================================
    // Chế độ KHÔNG SIẾT (Development/UAT) — ghi cảnh báo nhưng không chặn
    // ===================================================================

    [Fact]
    public void NonEnforce_Mode_Records_But_Does_Not_Block()
    {
        var serverAt = UtcAtVietnamHour(23, 0);   // ngoài khung giờ
        var result = _svc.Validate(
            TripToday(), PartnerWithGps(),
            10.7769, 106.7009, serverAt, serverAt,
            enforce: false);

        Assert.True(result.Allowed);
        Assert.Null(result.ErrorCode);
        Assert.Contains("KHÔNG SIẾT", result.Message);
    }
}
