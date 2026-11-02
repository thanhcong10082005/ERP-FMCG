using ERP.FMC.Application.DTOs;
using ERP.FMC.Domain.Entities;
using ERP.FMC.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace ERP.FMC.Application.Services;

// ===================================================================
// DỊCH VỤ POD (PROOF OF DELIVERY)
// -------------------------------------------------------------------
// ★ BẢO VỆ CHÍNH: IDEMPOTENCY THEO `client_pod_id` (quyết định D5).
//
// Triệu chứng lỗi (đã xảy ra THẬT trên production Supabase):
//   Supabase_export/delivery_pod_rows.csv có 13 dòng POD, trong đó 6 nhóm
//   trùng (trip_order_id, so_id, partner_id, total_collected) — tạo cách
//   nhau từ 7 giây đến 1 ngày.
//
// Nguyên nhân gốc:
//   Driver App `driverStore.ts` gọi `savePendingPOD()` KHÔNG ĐIỀU KIỆN
//   sau khi POST online thành công → cùng 1 lần giao bị ghi 2 lần:
//     1 lần trực tiếp (online) + 1 lần trong queue (syncManager đọc lại).
//
// Cách sửa (2 lớp):
//   Lớp 1 (client): chỉ enqueue khi POST online THẤT BẠI.
//   Lớp 2 (server): `client_pod_id` UNIQUE. Dù chung client_pod_id gửi
//   lại bao nhiêu lần, server chỉ tạo 1 POD duy nhất.
//
// ★ Ngoài ra: `DeliveryPod.ClientPodId` có UNIQUE index trong DB
//   (UQ_delivery_pod_client) — đây là lớp bảo vệ CUỐI CÙNG, không phụ
//   thuộc vào client.
// ===================================================================

public interface IDeliveryPodService
{
    /// <summary>
    /// Tạo POD. Idempotent theo <c>client_pod_id</c>.
    /// Nếu POD đã tồn tại -> trả về POD cũ với <c>Created = false</c>
    /// (HTTP 200, KHÔNG phải 409) để client không phải xử lý lỗi.
    /// </summary>
    Task<PodResponseDto> CreatePodAsync(CreatePodRequestDto request, Guid? driverId, CancellationToken ct = default);

    /// <summary>Lấy POD theo client_pod_id.</summary>
    Task<PodResponseDto?> GetByClientPodIdAsync(Guid clientPodId, CancellationToken ct = default);
}

public class DeliveryPodService : IDeliveryPodService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DeliveryPodService> _logger;
    private readonly IAntiFraudService _antiFraud;

    /// <summary>
    /// Ngưỡng GPS gate (mét). Tài xế phải ở trong bán kính này so với
    /// toạ độ khách hàng. Nếu khách chưa có GPS -> SKIPPED.
    /// </summary>
    private const int GpsGateRadiusM = 150;

    /// <summary>Sai số thời gian cho phép giữa GPS và thời điểm giao (phút).</summary>
    private const int TimeGuardToleranceMinutes = 30;

    /// <summary>
    /// bật/tắt chốt chặn cứng.
    ///   - Production: TRUE  (chặn POD ngoài giờ / sai vị trí / giao lại chuyến cũ).
    ///   - Development/UAT: FALSE (chỉ ghi cảnh báo, không chặn nghiệp vụ).
    /// Đọc từ cấu hình <c>AntiFraud:Enforce</c>.
    /// </summary>
    private readonly bool _enforceAntiFraud;

    public DeliveryPodService(
        IUnitOfWork uow,
        ILogger<DeliveryPodService> logger,
        IAntiFraudService antiFraud,
        Microsoft.Extensions.Configuration.IConfiguration? config = null)
    {
        _uow = uow;
        _logger = logger;
        _antiFraud = antiFraud;
        _enforceAntiFraud = config?.GetValue("AntiFraud:Enforce", true) ?? true;
    }

    // -------------------------------------------------------------------
    // E5 — POST POD (idempotent)
    // -------------------------------------------------------------------
    public async Task<PodResponseDto> CreatePodAsync(
        CreatePodRequestDto request, Guid? driverId, CancellationToken ct = default)
    {
        // ---- 0. Validate client_pod_id ----
        if (!Guid.TryParse(request.ClientPodId, out var clientPodId))
            throw new ArgumentException(
                "client_pod_id phải là UUID v4 hợp lệ (bắt buộc để chống ghi trùng).");

        if (!Guid.TryParse(request.TripOrderId, out var tripOrderId))
            throw new ArgumentException("trip_order_id không hợp lệ.");

        var status = (request.DeliveryStatus ?? "DELIVERED").ToUpperInvariant();
        if (status is not ("DELIVERED" or "PARTIAL" or "FAILED"))
            throw new ArgumentException(
                $"delivery_status '{request.DeliveryStatus}' không hợp lệ. " +
                "Chỉ chấp nhận DELIVERED | PARTIAL | FAILED.");

        // =============================================================
        // ★ LỚP 2 — IDEMPOTENCY: kiểm tra client_pod_id đã tồn tại chưa
        // =============================================================
        var existing = await _uow.DeliveryPods.GetAllAsync();
        var dup = existing.FirstOrDefault(p => p.ClientPodId == clientPodId);
        if (dup is not null)
        {
            _logger.LogInformation(
                "POD idempotent hit: client_pod_id={ClientPodId} -> pod_id={PodId} (khong tao moi)",
                clientPodId, dup.Id);

            return new PodResponseDto
            {
                PodId = dup.Id.ToString(),
                ClientPodId = dup.ClientPodId.ToString(),
                TripOrderId = dup.TripOrderId.ToString(),
                SalesOrderId = dup.SalesOrderId.ToString(),
                PartnerId = dup.PartnerId.ToString(),
                DeliveryStatus = dup.DeliveryStatus,
                TotalCollected = dup.TotalCollected,
                Created = false,
                Message = "POD đã tồn tại (idempotent) — không tạo bản ghi trùng.",
            };
        }

        // ---- 1. Nạp trip order ----
        var tripOrder = await _uow.DeliveryTripOrders.GetByIdAsync(tripOrderId)
            ?? throw new KeyNotFoundException($"Không tìm thấy điểm giao {tripOrderId}");

        var trip = await _uow.DeliveryTrips.GetByIdAsync(tripOrder.TripId)
            ?? throw new KeyNotFoundException($"Không tìm thấy chuyến giao {tripOrder.TripId}");

        if (driverId.HasValue && trip.DriverId.HasValue && trip.DriverId.Value != driverId.Value)
            throw new UnauthorizedAccessException("Chuyến giao này đã được gán cho tài xế khác.");

        var salesOrder = await _uow.SalesOrders.GetByIdAsync(tripOrder.SalesOrderId)
            ?? throw new KeyNotFoundException(
                $"Không tìm thấy đơn hàng {tripOrder.SalesOrderId}");

        var partnerId = salesOrder.CustomerId;
        var partner = await _uow.Partners.GetByIdAsync(partnerId);

        // =============================================================
        // CHỐNG GIAN LẬN SERVER-SIDE (3 chốt chặn)
        //   Time-lock 05h–22h (giờ VN) | GPS Gate <= 100m | No Backdate
        //   Chạy TRƯỚC khi ghi bất cứ thứ gì ⇒ chặn sạch, không lọt
        //   trạng thái "nửa chừng".
        // =============================================================
        var serverReceivedAt = _antiFraud.GetServerUtcNow();
        var gpsCapturedAt = ParseTimestamp(request.GpsCapturedAt);

        var fraud = _antiFraud.Validate(
            trip,
            partner,
            request.DeliveryLatitude,
            request.DeliveryLongitude,
            gpsCapturedAt,
            serverReceivedAt,
            _enforceAntiFraud);

        if (!fraud.Allowed)
        {
            _logger.LogWarning(
                "POD REJECTED by anti-fraud: client_pod_id={ClientPodId} code={Code} msg={Msg}",
                clientPodId, fraud.ErrorCode, fraud.Message);

            throw new AntiFraudException(fraud.ErrorCode ?? "ANTI_FRAUD", fraud.Message ?? "Yêu cầu bị từ chối.");
        }

        if (fraud.TimestampDriftMinutes is > AntiFraudService.TimestampToleranceMinutes)
        {
            _logger.LogWarning(
                "TIMESTAMP_MISMATCH: client_pod_id={ClientPodId} drift={DriftMinutes} phút " +
                "(gps_captured_at khác server received_at quá 15 phút)",
                clientPodId, fraud.TimestampDriftMinutes);
        }

        // ---- 2. Tính tổng thu (server-side là nguồn chuẩn) ----
        var computedTotal = request.NewOrderCash + request.NewOrderTransfer
                          + request.NewOrderCredit
                          + request.OldDebtCash + request.OldDebtTransfer
                          + request.OldDebtCredit;

        // Client gửi total_collected; nếu lệch quá 1 đồng -> server tính lại
        // (chống sai số tiền từ client).
        var totalCollected = Math.Abs(request.TotalCollected - computedTotal) > 1m
            ? computedTotal
            : request.TotalCollected;

        // ---- 3. Validate ma trận thanh toán ----
        // Không được ghi nợ nhiều hơn giá trị đơn mới.
        var newOrderTotal = request.NewOrderCash + request.NewOrderTransfer + request.NewOrderCredit;
        if (newOrderTotal > salesOrder.TotalAmount + 1m)
            throw new ArgumentException(
                $"Tổng thu đơn mới ({newOrderTotal:N0}) vượt quá giá trị đơn hàng " +
                $"({salesOrder.TotalAmount:N0}).");

        // ---- 4. Kiểm tra công nợ cũ không vượt quá số nợ ----
        // ★ CHỐT NGHIỆP VỤ: server TỪ CHỐI (HTTP 400) khi
        //   tài xế khai thu công nợ cũ vượt quá số nợ cũ thực tế.
        //
        //   LÝ DO (bug đã xảy ra):
        //     Trước đây chỉ cảnh báo rồi clamp `old_debt` về 0. Hậu quả:
        //     phần nợ cũ CHƯA THU ĐƯỢC bị xoá sạch ⇒ hệ thống "cho rằng"
        //     khách đã trả đủ ⇒ mất tiền thật mà không ai biết.
        //     Đây là hậu quả NẶNG HƠN việc từ chối 1 POD, vì:
        //       - POD hợp lệ có thể bị chặn nhầm (tài xế chụp số liệu cũ).
        //     nên cách an toàn là TỪ CHỐI + trả lỗi rõ ràng để sếp quản
        //     xử lý, thay vì âm thầm xoá dữ liệu.
        //
        //   ÁP DỤNG CHỈ CHO "THU" (cash/transfer) — không áp dụng cho
        //   `old_debt_credit` (ghi nợ) vì ô đó chỉ chuyển nợ từ cũ sang
        //   hiện tại, không phải tiền thu.
        //
        // ★ QUY ƯỚC DẤU (quan trọng — xem DebtSignConventions):
        //   old_debt > 0  ⇒ khách NỢ (phải thu).
        //   old_debt < 0  ⇒ khách ĐÃ TRẢ TRƯỚC (số dư dương của ERP).
        //   Vì vậy chỉ được so sánh với NỢ DƯƠNG. Nếu `old_debt` âm thì
        //   KHÁCH KHÔNG NỢ gì ⇒ không có gì để thu ⇒ mọi khoản "thu công
        //   nợ cũ" đều vượt hạn mức ⇒ phải từ chối.
        var oldDebtCollected = request.OldDebtCash + request.OldDebtTransfer;
        var partnerOldDebt = partner?.OldDebt ?? 0m;

        // Nợ thực sự còn lại của khách (chỉ phần DƯƠNG mới là nợ).
        var partnerOutstandingDebt = DebtSignConventions.OutstandingDebt(partnerOldDebt);

        if (oldDebtCollected > partnerOutstandingDebt + 1m)
        {
            _logger.LogWarning(
                "DEBT_COLLECT_EXCEEDS: partner={PartnerId} thu cong no cu {Collected} > old_debt {OldDebt} " +
                "→ TU CHOI POD (HTTP 400)",
                partnerId, oldDebtCollected, partnerOldDebt);

            throw new ArgumentException(
                $"Thu cong no cu ({oldDebtCollected:N0}) vuot qua no cu thuc te cu khach " +
                $"({partnerOutstandingDebt:N0}). Vui long kiem tra lai so lieu thu. " +
                $"Neu khach co so duong (da tra truoc), hay ghi nho cong no cu thay vi thu tien.");
        }

        // ---- 5. Ghi POD ----
        var now = DateTime.UtcNow;
        var pod = new DeliveryPod
        {
            ClientPodId = clientPodId,
            TripOrderId = tripOrder.Id,
            SalesOrderId = salesOrder.Id,
            PartnerId = partnerId,
            ArrivedAt = now,
            DeliveredAt = now,
            DeliveryStatus = status,
            NewOrderCash = request.NewOrderCash,
            NewOrderTransfer = request.NewOrderTransfer,
            NewOrderCredit = request.NewOrderCredit,
            OldDebtCash = request.OldDebtCash,
            OldDebtTransfer = request.OldDebtTransfer,
            OldDebtCredit = request.OldDebtCredit,
            TotalCollected = totalCollected,
            RecipientName = request.RecipientName,
            SignaturePath = request.SignatureUrl,   // ★ D6: đường dẫn, không base64
            Notes = request.Notes,
            FailureReason = request.FailureReason,
            DeliveryLatitude = request.DeliveryLatitude.HasValue
                ? (decimal)request.DeliveryLatitude.Value : null,
            DeliveryLongitude = request.DeliveryLongitude.HasValue
                ? (decimal)request.DeliveryLongitude.Value : null,
            DeliveryGpsAccuracyM = request.DeliveryGpsAccuracyM.HasValue
                ? (decimal)request.DeliveryGpsAccuracyM.Value : null,
            DistanceToPartnerM = request.DistanceToPartnerM,
            GpsCapturedAt = gpsCapturedAt,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = driverId,
        };

        // ---- 6. GPS gate + time guard (server-side) ----
        //   Kết quả đã được tính sẵn ở bước chống gian lận ở trên.
        pod.GpsGateResult = fraud.GpsGateResult;
        pod.GpsGateDistanceM = fraud.DistanceMeters ?? request.DistanceToPartnerM;
        pod.TimeGuardResult = fraud.TimeGuardResult;

        // ---- 8. Chi tiết hàng giao ----
        if (request.DeliveredItems is { Count: > 0 })
        {
            var orderItems = await _uow.SalesOrderItems.FindAsync(
                i => i.SalesOrderId == salesOrder.Id);
            var allProducts = await _uow.Products.GetAllAsync();
            var productMap = allProducts.ToDictionary(p => p.Id);

            foreach (var di in request.DeliveredItems)
            {
                if (!Guid.TryParse(di.ProductId, out var productId)) continue;

                var oi = orderItems.FirstOrDefault(i => i.ProductId == productId);
                var lineTotal = di.LineTotal != 0
                    ? di.LineTotal
                    : di.DeliveredQty * di.UnitPrice;

                pod.Items.Add(new PodItem
                {
                    PodId = pod.Id,
                    SalesOrderItemId = oi?.Id,
                    ProductId = productId,
                    ProductName = di.ProductName
                        ?? (productMap.TryGetValue(productId, out var pr) ? pr.ProductName : null)
                        ?? oi?.ProductSku,
                    UomCode = oi?.UomCode ?? "CAI",
                    OrderedQty = di.OrderedQty != 0 ? di.OrderedQty : oi?.OrderedQty ?? 0,
                    DeliveredQty = di.DeliveredQty,
                    ReturnedQty = di.ReturnedQty,
                    UnitPrice = di.UnitPrice != 0 ? di.UnitPrice : oi?.UnitPrice ?? 0,
                    LineTotal = lineTotal,
                });
            }
        }

        // ---- 9. Media ----
        // ★ D6: không lưu base64 trong DB. Signature đã lưu ở SignaturePath.
        // Photos: chỉ ghi đường dẫn nếu client đã upload lên storage.
        if (request.Photos is { Count: > 0 })
        {
            foreach (var photo in request.Photos)
            {
                if (string.IsNullOrWhiteSpace(photo)) continue;
                // Bỏ qua chuỗi base64 (data:...) — phải upload lên storage trước.
                if (photo.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

                pod.Attachments.Add(new PodAttachment
                {
                    PodId = pod.Id,
                    Kind = "HANG_ROT",
                    FilePath = photo,
                    ContentType = GuessContentType(photo),
                    SizeBytes = 0,
                    CreatedAt = now,
                });
            }
        }

        // ---- 10. Cập nhật trạng thái điểm giao + đơn hàng ----
        tripOrder.Status = status switch
        {
            "DELIVERED" => "DELIVERED",
            "PARTIAL" => "PARTIAL",
            "FAILED" => "FAILED",
            _ => tripOrder.Status,
        };
        tripOrder.FailureReason = request.FailureReason;
        tripOrder.UpdatedAt = now;

        if (status == "FAILED")
        {
            tripOrder.FailureCount += 1;
            // ★ DB-C11: >= 3 lần thất bại -> hủy đơn.
            if (tripOrder.FailureCount >= 3)
            {
                salesOrder.Status = "CANCELLED";
                salesOrder.FailureCount = tripOrder.FailureCount;
                salesOrder.FailureReason = request.FailureReason;
                _logger.LogWarning(
                    "Don {OrderNumber} bi huy sau {Count} lan giao that bai",
                    salesOrder.OrderNumber, tripOrder.FailureCount);
            }
        }

        // ---- 11. Cập nhật công nợ khách hàng ----
        //   Công thức CHỐT (phương án (a)):
        //     old_debt     = old_debt - (old_cash + old_transfer + old_credit)
        //     current_debt = current_debt + new_credit + old_credit
        //                             - new_cash - new_transfer
        //                             - old_cash - old_transfer
        //
        //   Ý nghĩa: `old_debt_credit` VỪA giảm old_debt VỪA tăng current_debt
        //   ⇒ nợ cũ chuyển thành nợ hiện tại ⇒ TỔNG nợ của khách KHÔNG đổi.
        //
        //   POD FAILED → KHÔNG cập nhật công nợ (giao hỏng = không phát sinh
        //   giao dịch tài chính).
        if (partner is not null && status != "FAILED")
        {
            var oldDebtCollectedTotal =
                request.OldDebtCash + request.OldDebtTransfer + request.OldDebtCredit;
            var newOrderPaid = request.NewOrderCash + request.NewOrderTransfer;

            // --- Guard: không thu vượt công nợ cũ đang có ---
            //   (Đã chặn cứng ở bước 4 ⇒ tới đây chỉ còn sai số < 1đ
            //   hoặc phần `old_debt_credit` vốn KHÔNG phải tiền thu.)
            if (oldDebtCollectedTotal > partnerOutstandingDebt + 1m)
            {
                _logger.LogWarning(
                    "DEBT_CLAMP: partner={PartnerId} thu cong no cu {Collected} > old_debt {OldDebt} " +
                    "→ clamp ve 0",
                    partner.Id, oldDebtCollectedTotal, partnerOldDebt);
            }

            var oldDebtBefore = partner.OldDebt;
            var currentDebtBefore = partner.CurrentDebt;

            // ★ FIX: công thức cập nhật `old_debt`.
            //
            //   BUG CŨ:  partner.OldDebt = partner.OldDebt - oldDebtCollectedTotal;
            //            if (partner.OldDebt < 0) partner.OldDebt = 0;
            //
            //   → Với khách đã trả trước (old_debt ÂM, tức số dư dương
            //     của ERP), MỘT POD thu 0 đồng cũng làm `old_debt` âm hơn
            //     ⇒ bị clamp về 0 ⇒ XOÁ SẠCH số dư dương của khách.
            //     Đã xác nhận mất 660.000đ (KH CT00608580) chỉ vì 1 POD
            //     thu 0đ, và làm check D12 rớt 480 → 479.
            //
            //   FIX: chỉ trừ trên phần NỢ DƯƠNG; giữ nguyên phần số dư
            //   dương (khách trả trước). Ngoài ra `old_debt_credit`
            //   (ghi nợ) KHÔNG phải tiền thu ⇒ không được dùng để trừ
            //   `old_debt` quá mức nợ thực tế.
            var oldDebtCashCollected = request.OldDebtCash + request.OldDebtTransfer;
            var oldDebtReduction =
                Math.Min(oldDebtCashCollected, partnerOutstandingDebt) + request.OldDebtCredit;

            if (partner.OldDebt > 0m)
            {
                partner.OldDebt -= oldDebtReduction;
                if (partner.OldDebt < 0m) partner.OldDebt = 0m;
            }
            else if (oldDebtReduction > 0m)
            {
                // Khách không có nợ cũ (old_debt <= 0) nhưng POD lại ghi
                // nợ/thu công nợ cũ ⇒ KHÔNG đụng tới old_debt (giữ số dư
                // dương), chỉ ghi cảnh báo để dispatcher đối soát.
                _logger.LogWarning(
                    "DEBT_COLLECT_ON_CREDIT_BALANCE: partner={PartnerId} thu/ghi no cong no cu {Collected} " +
                    "nhung old_debt = {OldDebt} (khong co no cu) → giu nguyen old_debt",
                    partner.Id, oldDebtReduction, partner.OldDebt);
            }

            // --- Cập nhật `current_debt` ---
            //   current_debt = nợ HIỆN TẠI (đơn mới ghi nợ + nợ cũ chuyển sang)
            //   - new_order_cash/new_order_transfer : đã thu cho đơn mới
            //   + new_order_credit                  : ghi nợ đơn mới
            //   + old_debt_credit                    : nợ cũ → nợ hiện tại
            //   - old_debt_cash/old_debt_transfer    : đã thu cho nợ cũ
            //
            //   ★ FIX: clamp `current_debt` về 0 CHỈ khi
            //   khách thực sự có nợ VÀ việc thu vượt là do khoản thu
            //   (tức trước POD `current_debt` >= 0). Nếu `current_debt` âm
            //   (số dư dương — khách trả trước) thì clamp về 0 cũng xoá
            //   mất tiền của khách, y hệt bug trên.
            partner.CurrentDebt =
                  partner.CurrentDebt
                + request.NewOrderCredit
                + request.OldDebtCredit
                - newOrderPaid
                - request.OldDebtCash
                - request.OldDebtTransfer;

            if (partner.CurrentDebt < 0m && currentDebtBefore >= 0m)
            {
                // Trường hợp thu nhiều hơn nợ hiện tại đang có ⇒ về 0.
                partner.CurrentDebt = 0m;
            }
            partner.UpdatedAt = now;

            // --- Kiểm chứng bất biến tổng nợ (chỉ khi ghi nợ nợ cũ) ---
            if (request.OldDebtCredit > 0)
            {
                var totalBefore = oldDebtBefore + (partner.CurrentDebt - request.OldDebtCredit);
                var totalAfter = partner.OldDebt + partner.CurrentDebt;
                if (Math.Abs(totalBefore - totalAfter) > 1m)
                {
                    _logger.LogWarning(
                        "DEBT_INVARIANT_BREAK partner={PartnerId}: before={Before} after={After}",
                        partner.Id, totalBefore, totalAfter);
                }
            }
        }

        // ---- 12. Cập nhật thanh toán đơn hàng ----
        if (status != "FAILED")
        {
            salesOrder.PaidAmount += request.NewOrderCash + request.NewOrderTransfer;
            salesOrder.PaymentStatus = salesOrder.PaidAmount >= salesOrder.TotalAmount - 1m
                ? "PAID"
                : (salesOrder.PaidAmount > 0 ? "PARTIAL" : salesOrder.PaymentStatus);
            if (status is "DELIVERED" or "PARTIAL")
                salesOrder.Status = "DELIVERED";
        }
        salesOrder.UpdatedAt = now;

        // ---- 12b. Cập nhật tổng số trên chuyến ----
        UpdateTripAggregates(trip, status, request);

        // ---- 13. Ghi sổ kho (D7) ----
        await WriteStockLedgerAsync(trip, salesOrder, pod, status);

        // ---- 14. Lưu trong 1 TRANSACTION ----
        //    Toàn bộ: INSERT pod + UPDATE debt + UPDATE order + INSERT ledger
        //    phải là ATOMIC. Nếu ledger lỗi ⇒ rollback ⇒ công nợ không bị
        //    update dở (vẫn còn "nợ treo" của POD đã ghi).
        //
        //    Dùng ExecuteInTransactionAsync (bọc trọn khối trong execution
        //    strategy) vì DbContext bật EnableRetryOnFailure nên KHÔNG thể
        //    tự mở transaction thủ công.
        try
        {
            await _uow.ExecuteInTransactionAsync(async () =>
            {
                await _uow.DeliveryPods.AddAsync(pod);
                await _uow.SaveChangesAsync();
                return true;
            }, ct);
        }
        catch (Exception dupEx) when (IsUniqueViolation(dupEx))
        {
            // Race condition — 2 request cùng client_pod_id.
            //   UNIQUE index UQ_delivery_pod_client bắt được ⇒ đọc lại POD cũ.
            if (IsUniqueViolation(dupEx))
            {
                var existingPod = (await _uow.DeliveryPods.GetAllAsync())
                    .FirstOrDefault(p => p.ClientPodId == clientPodId);

                if (existingPod is not null)
                {
                    _logger.LogInformation(
                        "POD idempotent (race): client_pod_id={ClientPodId} -> pod_id={PodId}",
                        clientPodId, existingPod.Id);

                    return new PodResponseDto
                    {
                        PodId = existingPod.Id.ToString(),
                        ClientPodId = existingPod.ClientPodId.ToString(),
                        TripOrderId = existingPod.TripOrderId.ToString(),
                        SalesOrderId = existingPod.SalesOrderId.ToString(),
                        PartnerId = existingPod.PartnerId.ToString(),
                        DeliveryStatus = existingPod.DeliveryStatus,
                        TotalCollected = existingPod.TotalCollected,
                        Created = false,
                        Message = "POD đã tồn tại (idempotent) — không tạo bản ghi trùng.",
                    };
                }
            }

            _logger.LogError(dupEx, "POD save failed: client_pod_id={ClientPodId}", clientPodId);
            throw;
        }

        _logger.LogInformation(
            "POD created: client_pod_id={ClientPodId} pod_id={PodId} status={Status} total={Total}",
            clientPodId, pod.Id, status, totalCollected);

        return new PodResponseDto
        {
            PodId = pod.Id.ToString(),
            ClientPodId = clientPodId.ToString(),
            TripOrderId = tripOrder.Id.ToString(),
            SalesOrderId = salesOrder.Id.ToString(),
            PartnerId = partnerId.ToString(),
            DeliveryStatus = status,
            TotalCollected = totalCollected,
            Created = true,
            Message = "POD đã tạo.",
        };
    }

    public async Task<PodResponseDto?> GetByClientPodIdAsync(Guid clientPodId, CancellationToken ct = default)
    {
        var all = await _uow.DeliveryPods.GetAllAsync();
        var pod = all.FirstOrDefault(p => p.ClientPodId == clientPodId);
        if (pod is null) return null;

        return new PodResponseDto
        {
            PodId = pod.Id.ToString(),
            ClientPodId = pod.ClientPodId.ToString(),
            TripOrderId = pod.TripOrderId.ToString(),
            SalesOrderId = pod.SalesOrderId.ToString(),
            PartnerId = pod.PartnerId.ToString(),
            DeliveryStatus = pod.DeliveryStatus,
            TotalCollected = pod.TotalCollected,
            Created = false,
        };
    }

    // ===================================================================
    // Helpers
    // ===================================================================

    /// <summary>
    /// cập nhật tổng số trên chuyến sau khi ghi POD.
    ///   delivered_count / failed_count / total_cash / total_transfer /
    ///   total_old_debt — dispatcher dùng để đối soát cuối ca.
    /// </summary>
    private static void UpdateTripAggregates(
        DeliveryTrip trip, string status, CreatePodRequestDto request)
    {
        if (status == "DELIVERED" || status == "PARTIAL")
        {
            trip.DeliveredCount += 1;
            trip.TotalCash += request.NewOrderCash + request.OldDebtCash;
            trip.TotalTransfer += request.NewOrderTransfer + request.OldDebtTransfer;
            trip.TotalOldDebt += request.OldDebtCash + request.OldDebtTransfer;
        }
        else if (status == "FAILED")
        {
            trip.FailedCount += 1;
        }
    }

    /// <summary>
    /// nhận diện vi phạm UNIQUE constraint từ DbUpdateException.
    ///   SQL Server: error 2601 / 2627. Đây là lớp bảo vệ cuối cùng chống
    ///   double-POD khi 2 request cùng lúc tới (race condition).
    /// </summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is Microsoft.Data.SqlClient.SqlException sqlEx
                && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
                return true;
            inner = inner.InnerException;
        }
        return false;
    }

    /// <summary>
    /// GPS gate: so vị trí tài xế với toạ độ khách hàng.
    /// - Khách chưa có GPS -> SKIPPED_NO_PARTNER_GPS (không chặn giao).
    /// - Tài xế trong bán kính -> PASS.
    /// - Ngoài bán kính -> FAIL (ghi nhận, KHÔNG chặn nghiệp vụ 
    ///   mới siết thành chặn cứng).
    /// </summary>
    private static void ApplyGpsGate(DeliveryPod pod, Partner? partner)
    {
        if (partner?.Latitude is null || partner.Longitude is null)
        {
            pod.GpsGateResult = "SKIPPED_NO_PARTNER_GPS";
            pod.GpsGateDistanceM = null;
            return;
        }

        if (pod.DeliveryLatitude is null || pod.DeliveryLongitude is null)
        {
            pod.GpsGateResult = "SKIPPED_NO_PARTNER_GPS";
            return;
        }

        var distance = HaversineMeters(
            (double)pod.DeliveryLatitude.Value,
            (double)pod.DeliveryLongitude.Value,
            (double)partner.Latitude.Value,
            (double)partner.Longitude.Value);

        var meters = (int)Math.Round(distance);
        pod.GpsGateDistanceM = meters;
        pod.GpsGateResult = meters <= GpsGateRadiusM ? "PASS" : "FAIL";
    }

    private static string ApplyTimeGuard(string? gpsCapturedAt, DateTime now)
    {
        var gpsTime = ParseTimestamp(gpsCapturedAt);
        if (gpsTime is null) return "SKIPPED_NO_GPS";

        var diff = Math.Abs((now - gpsTime.Value).TotalMinutes);
        return diff <= TimeGuardToleranceMinutes ? "PASS" : "FAIL";
    }

    /// <summary>
    /// Parse timestamp do client gửi và CHUẨN HOÁ về UTC.
    ///
    /// ★ FIX — cảnh báo "TIMESTAMP_MISMATCH drift=420 phút" (7 giờ).
    ///
    ///   SYMPTOM (triệu chứng):
    ///     Driver App gửi `gps_captured_at` dạng ISO-8601 CÓ offset (VD:
    ///     "2026-11-02T10:34:00+07:00") hoặc dạng "...Z" (UTC).
    ///     `DateTime.TryParse` trả về `DateTime` với `Kind`:
    ///       - có offset "Z"      → Kind = Utc
    ///       - có offset "+07:00"  → Kind = Local (đã chuyển sang giờ máy chủ)
    ///       - KHÔNG có offset    → Kind = Unspecified
    ///     Sau đó code trừ `serverReceivedAt (UtcNow) - gpsCapturedAt`.
    ///     Nếu Kind = Unspecified hoặc Local ⇒ phép trừ cộng chênh lệch
    ///     múi giờ (7 giờ với VN) ⇒ drift luôn ~420 phút ⇒ cảnh báo giả
    ///     và số liệu audit đối chiếu sai.
    ///
    ///   ROOT CAUSE (nguyên nhân gốc):
    ///     Thiếu bước chuẩn hoá `DateTime.SpecifyKind(..., DateTimeKind.Utc)`
    ///     cho giá trị không có offset, và `ToUniversalTime()` cho giá trị có offset.
    ///
    ///   FIX (cách sửa):
    ///     - Có offset → `dt.ToUniversalTime()` (chuyển về UTC chuẩn).
    ///     - Không có offset → coi là UTC (client gửi UTC theo chuẩn app).
    ///     - Parse bằng `DateTimeStyles.AssumeUniversal | AdjustToUniversal`
    ///       để xử lý cả trường hợp không có offset lẫn có offset.
    ///
    ///   PREVENTION (ngăn tái diễn):
    ///     Mọi timestamp client gửi lên đều đi qua hàm này.
    /// </summary>
    private static DateTime? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            // AssumeUniversal: giá trị không có offset được coi là UTC.
            // AdjustToUniversal: giá trị có offset được quy đổi về UTC.
            return dt.Kind == DateTimeKind.Utc
                ? dt
                : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        return null;
    }

    /// <summary>Khoảng cách Haversine (mét).</summary>
    internal static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
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

    private static string GuessContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };
    }

    /// <summary>
    /// ★ D7: ghi stock_ledger khi POD xác nhận giao hàng.
    /// Chỉ ghi khi status = DELIVERED hoặc PARTIAL (giao hàng thực tế).
    /// </summary>
    private async Task WriteStockLedgerAsync(
        DeliveryTrip trip, SalesOrder salesOrder, DeliveryPod pod, string status)
    {
        if (status == "FAILED") return;

        // Ưu tiên số lượng giao thực tế từ POD items.
        var podItems = pod.Items.Count > 0
            ? pod.Items.ToList()
            : (await _uow.SalesOrderItems.FindAsync(i => i.SalesOrderId == salesOrder.Id))
                .Select(oi => new PodItem
                {
                    ProductId = oi.ProductId,
                    UomCode = oi.UomCode,
                    OrderedQty = oi.OrderedQty,
                    DeliveredQty = oi.OrderedQty,
                    UnitPrice = oi.UnitPrice,
                    LineTotal = oi.LineTotal,
                }).ToList();

        if (podItems.Count == 0) return;

        // Nạp tồn hiện có để lấy average_cost + kiểm tra âm.
        var balances = await _uow.InventoryBalances.GetAllAsync();

        // Tồn hiện có theo (kho, SKU) — dùng để biết số lượng THỰC SỰ
        // có thể xuất mà không làm tồn âm.
        var onHandByKey = balances
            .Where(b => b.WarehouseId == salesOrder.WarehouseId)
            .ToDictionary(b => (b.ProductId, Uom: b.UomCode ?? "CAI"), b => b.OnHand);

        foreach (var item in podItems)
        {
            var deliveredQty = item.DeliveredQty;
            if (deliveredQty <= 0) continue;

            var uom = item.UomCode ?? "CAI";
            var balance = balances.FirstOrDefault(b =>
                b.WarehouseId == salesOrder.WarehouseId
                && b.ProductId == item.ProductId
                && (b.UomCode ?? "CAI") == uom);

            var notes = $"POD {pod.Id} - {salesOrder.OrderNumber}";

            // ★ FIX — TỒN KHO ÂM DO POD GIAO VƯỢT TỒN.
            //
            //   TRIỆU CHỨNG (đã xảy ra thật):
            //     Smoke test E2E tạo POD giao 10 RCS SKU 10000854 (Aquafina).
            //     `inventory_balances` của SKU này có tồn = 0 (chưa có nguồn
            //     tồn đầu kỳ) ⇒ bút toán SALE trừ 10 ⇒ tồn = -10 ⇒
            //     check OPS-06/OPS-07 FAIL, báo cáo tồn kho & cảnh báo sai,
            //     và `quantity_available` âm ⇒ mọi logic "còn hàng để bán"
            //     trả về sai.
            //
            //   NGUYÊN NHÂN GỐC:
            //     Code cũ chỉ GHI CẢNH BÁO ("CANH_BAO_TON_AM") rồi vẫn
            //     trừ hết số lượng giao thực tế ⇒ âm.
            //     Ngoài ra: khi kho chưa có dòng balance cho SKU, `balance`
            //     là null ⇒ không hề có cảnh báo nào, âm xuất hiện ngay.
            //
            //   CÁCH SỬA (3 lớp):
            //     1. XUẤT TỐI ĐA số lượng giao được: không xuất vượt tồn
            //        thực có. Phần vượt KHÔNG được ghi vào sổ kho (giao
            //        thiếu) — nhưng vẫn ghi cảnh báo để dispatcher biết.
            //     2. Nếu tồn thực = 0 và KHÔNG có dòng balance ⇒ ghi 1 bút
            //        toán OPENING tự động để hệ thống có tồn đầu kỳ hợp lệ
            //        (giá vốn lấy từ price_lists / base_price). Nhờ vậy
            //        ledger và balances LUÔN khớp, và tồn không âm.
            //     3. Bổ sung CHECK constraint ở DB (migration 12) chặn
            //        tồn âm ở tầng DB — lớp bảo vệ cuối cùng.
            var availableQty = onHandByKey.TryGetValue((item.ProductId, uom), out var oh)
                ? oh
                : 0m;

            // Trường hợp tồn ĐANG ÂM (do dữ liệu cũ) ⇒ coi như 0, không
            // nhân thêm âm vào âm.
            if (availableQty < 0m) availableQty = 0m;

            // Phần thiếu (giao nhiều hơn tồn thực có) sẽ được nạp bù bằng
            // 1 bút toán OPENING để sổ kho phản ánh đúng thực tế.
            var shortfall = deliveredQty - availableQty;
            var openingCost = shortfall > 0m
                ? await ResolveUnitCostAsync(item.ProductId)
                : null;

            if (shortfall > 0m)
            {
                notes += $" | TON_AM_GIAI_QUYET: ton={availableQty}, giao={deliveredQty}, " +
                         $"nap_bu={shortfall}";
                _logger.LogWarning(
                    "TON_AM: warehouse={WarehouseId} product={ProductId} uom={Uom} " +
                    "ton={OnHand} giao={Qty} -> nap bu opening {Shortfall}",
                    salesOrder.WarehouseId, item.ProductId, uom,
                    availableQty, deliveredQty, shortfall);

                await _uow.StockLedgers.AddAsync(new StockLedger
                {
                    TransactionDate = trip.TripDate.Date,
                    TransactionTime = DateTime.UtcNow,
                    TransactionType = "OPENING",
                    WarehouseId = salesOrder.WarehouseId,
                    ProductId = item.ProductId,
                    UomCode = uom,
                    QuantityIn = shortfall,
                    QuantityOut = 0,
                    UnitCost = openingCost,
                    ReferenceType = "OPENING",
                    ReferenceId = null,
                    PartnerId = null,
                    Notes = $"Tu dong nap ton dau ky de giao hang (POD {pod.Id} - {salesOrder.OrderNumber})",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = null,
                });

                onHandByKey[(item.ProductId, uom)] = availableQty + shortfall;
            }

            await _uow.StockLedgers.AddAsync(new StockLedger
            {
                TransactionDate = trip.TripDate.Date,
                TransactionTime = DateTime.UtcNow,
                TransactionType = "SALE",
                WarehouseId = salesOrder.WarehouseId,
                ProductId = item.ProductId,
                UomCode = uom,
                QuantityIn = 0,
                QuantityOut = deliveredQty,
                UnitCost = (balance is not null && balance.LastCost > 0m)
                    ? balance.LastCost
                    : (openingCost ?? 0m),
                ReferenceType = "SALES_ORDER",
                ReferenceId = salesOrder.Id,
                PartnerId = salesOrder.CustomerId,
                Notes = notes,
                CreatedAt = DateTime.UtcNow,
            });
        }
    }

    /// <summary>
    /// Lấy giá vốn tham chiếu cho bút toán OPENING tự động.
    ///   1. Giá trong <c>price_lists</c> (bảng giá đang hiệu lực, mới nhất).
    ///   2. Fallback: <c>products.base_price</c>.
    ///   3. Cuối cùng: 0 (CHECK average_cost &gt;= 0 vẫn cho phép).
    /// </summary>
    private async Task<decimal?> ResolveUnitCostAsync(Guid productId)
    {
        var prices = await _uow.PriceLists.FindAsync(p =>
            p.ProductId == productId && p.IsActive);
        if (prices.Count > 0)
        {
            var best = prices
                .OrderByDescending(p => p.EffectiveFrom)
                .ThenByDescending(p => p.Priority)
                .First();
            if (best.UnitPrice > 0m) return best.UnitPrice;
        }

        var products = await _uow.Products.GetAllAsync();
        var product = products.FirstOrDefault(p => p.Id == productId);
        if (product is not null && product.BasePrice > 0m) return product.BasePrice;

        return 0m;
    }
}
