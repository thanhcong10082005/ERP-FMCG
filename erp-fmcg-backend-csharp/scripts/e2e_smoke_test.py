"""E2E smoke test cho Backend C# — chạy ngoài thư viện Pack.

Kịch bản:
  1. POST /api/auth/dev-login      -> lay JWT
  2. GET  /api/sales/trips?status= -> danh sach chuyen (can JWT)
  3. GET  /api/sales/trips/{id}    -> chi tiet chuyen + don hang
  4. POST /api/sales/pod            -> tao POD (idempotent)
  5. POST /api/sales/pod LAI       -> cung client_pod_id -> created=false
  6. GET  /api/driver/master/partners, /products
"""
import json
import os
import sys
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone

BASE = os.environ.get("API_BASE", "http://localhost:5090").rstrip("/")
PREFIX = "/api"

# ---------------------------------------------------------------------------
# MANIFEST — danh sach id do E2E tao ra, dung cho cleanup.
#
# VAN DE: smoke test chay tren DB THAT. Neu khai bao id da tao (pod_id,
# trip_id, trip_order_id, sales_order_id) thi cleanup xoa DUNG nhung dong
# do, khong dung "so hoc" de doan (de xoa nham don hang that).
# ---------------------------------------------------------------------------
MANIFEST = {
    "pod_ids": [],
    "client_pod_ids": [],
    "trip_ids": [],
    "trip_order_ids": [],
    "sales_order_ids": [],
}


def manifest_reset():
    for key in MANIFEST:
        MANIFEST[key] = []


def manifest_add(key, value):
    if value and value not in MANIFEST[key]:
        MANIFEST[key].append(value)


def manifest_path() -> str:
    """Duong dan file manifest (cung thu muc voi script)."""
    return os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        ".e2e_manifest.json")


def manifest_save():
    try:
        with open(manifest_path(), "w", encoding="utf-8") as fh:
            json.dump(MANIFEST, fh, indent=2)
    except OSError:
        pass


def manifest_load():
    try:
        with open(manifest_path(), "r", encoding="utf-8") as fh:
            data = json.load(fh)
        for key in MANIFEST:
            if isinstance(data.get(key), list):
                MANIFEST[key] = data[key]
        return True
    except (OSError, ValueError):
        return False


def req(method: str, path: str, body=None, token=None):
    url = f"{BASE}{path}"
    data = None
    headers = {"Content-Type": "application/json"}
    if body is not None:
        data = json.dumps(body).encode("utf-8")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    r = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(r, timeout=30) as resp:
            raw = resp.read().decode("utf-8")
            return resp.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8", "replace")
        try:
            return e.code, json.loads(raw)
        except Exception:
            return e.code, raw


def check(label, cond, detail=""):
    mark = "PASS" if cond else "FAIL"
    print(f" [{mark}] {label}" + (f" -> {detail}" if detail else ""))
    return bool(cond)


def setup_trip_for_today(driver_token: str, today: str):
    """Tạo chuyến xe của hôm nay + gắn 1 đơn + start chuyến.

    Cần token ADMIN (dispatcher) để tạo/gắn chuyến, nhưng token TÀI XẾ
    để start chuyến (endpoint /api/sales/trips/{id}/start kiểm tra
    CurrentDriverId — token admin sẽ bị 401).

    Trả về trip_id hoặc None nếu thất bại.
    """
    # 1. Token dispatcher (ADMIN)
    st, adm = req("POST", f"{PREFIX}/auth/dev-login",
                  {"username": os.environ.get("ADMIN_USERNAME", "admin")})
    if st != 200:
        print(f"    [ERR] dang nhap dispatcher that bai: {st} {str(adm)[:150]}")
        return None
    adm_token = adm.get("access_token") or (adm.get("data") or {}).get("access_token")

    # 2. Kho
    st, prods = req("GET", f"{PREFIX}/driver/master/products", token=driver_token)
    if st != 200:
        print(f"    [ERR] doc master products that bai: {st}")
        return None

    # 3. Tạo chuyến — cần warehouse_id. Lấy từ chuyến bất kỳ đang có.
    st, trips_all = req("GET", f"{PREFIX}/sales/trips", token=driver_token)
    tl = trips_all if isinstance(trips_all, list) else (trips_all or {}).get("items", [])
    warehouse_id = None
    for t in tl:
        if t.get("warehouse_id"):
            warehouse_id = t["warehouse_id"]
            break
    if not warehouse_id:
        print("    [ERR] khong lay duoc warehouse_id")
        return None

    driver_id = None
    st, me = req("GET", f"{PREFIX}/auth/me", token=driver_token)
    if st == 200:
        driver_id = me.get("user_id") or me.get("id")

    st, trip = req("POST", f"{PREFIX}/dispatcher/trips", {
        "warehouse_id": warehouse_id,
        "driver_id": driver_id,
        "trip_date": today,
        "vehicle_plate": "E2E-TEST",
        "note": "e2e smoke test - tao tu dong",
    }, token=adm_token)
    if st not in (200, 201):
        print(f"    [ERR] tao chuyen that bai: {st} {str(trip)[:200]}")
        return None
    trip_id = trip.get("trip_id")
    manifest_add("trip_ids", trip_id)
    print(f"    -> da tao chuyen {trip.get('trip_number')} ({trip_id})")

    # 4. Gắn 1 đơn hàng (endpoint sales-orders trả camelCase)
    st, orders = req("GET", f"{PREFIX}/sales-orders?pageSize=50", token=adm_token)
    ol = orders if isinstance(orders, list) else (orders or {}).get("items", [])
    so_id = None
    for o in ol:
        if o.get("customerId") and o.get("status") in ("CONFIRMED", "DRAFT", "PICKING"):
            so_id = o.get("id")
            break
    if not so_id:
        # fallback: don nao cung duoc
        for o in ol:
            if o.get("id"):
                so_id = o.get("id")
                break
    if not so_id:
        print("    [WARN] khong tim thay don hang de gan")
    else:
        st, r = req("POST", f"{PREFIX}/dispatcher/trips/{trip_id}/orders",
                    {"sales_order_ids": [so_id]}, token=adm_token)
        if st != 200:
            print(f"    [ERR] gan don that bai: {st} {str(r)[:200]}")
            return None
        manifest_add("sales_order_ids", so_id)
        print(f"    -> da gan don {so_id}")

    # 5. Start chuyến (token tài xế)
    st, r = req("PUT", f"{PREFIX}/sales/trips/{trip_id}/start", None, token=driver_token)
    if st != 200:
        print(f"    [ERR] start chuyen that bai: {st} {str(r)[:200]}")
        return None
    print(f"    -> chuyen da DELIVERING")
    return trip_id


def cleanup_test_data():
    """Don du lieu test vua tao (POD / ledger / trip / paid_amount).

    Script nay chay tren DB THAT, nen buoc nay la BAT BUOC de:
      - `08_data_regression.py --check` van PASS (hash khop baseline)
      - du lieu nghiep vu khong bi nhieu bang don hang/ton kho ao

    Thuc hien bang cach goi script dung chung:
        erp-fmcg-database/scripts/10_cleanup_e2e_data.py
    """
    import subprocess

    # Tim script cleanup: tu thu muc hien tai len 2 cap (erp-fmcg-backend-csharp
    # -> Pack -> erp-fmcg-database/scripts/...).
    here = os.path.dirname(os.path.abspath(__file__))
    candidates = [
        os.path.normpath(os.path.join(here, "..", "..", "erp-fmcg-database",
                                     "scripts", "10_cleanup_e2e_data.py")),
        os.path.normpath(os.path.join(here, "..", "erp-fmcg-database",
                                     "scripts", "10_cleanup_e2e_data.py")),
    ]
    script = next((c for c in candidates if os.path.isfile(c)), None)
    if not script:
        return None

    try:
        proc = subprocess.run(
            [sys.executable, script],
            capture_output=True, text=True, timeout=180,
        )
    except Exception as exc:
        print(f"    [ERR] cleanup that bai: {exc}")
        return None

    if proc.returncode != 0:
        print(f"    [ERR] cleanup exit={proc.returncode}: {proc.stderr.strip()[:200]}")
        return None

    # Tom tat cac dong da xoa.
    summary = []
    for line in proc.stdout.splitlines():
        s = line.strip()
        if s.startswith("[") and "]" in s and "->" in s:
            summary.append(s)
    return " | ".join(summary) if summary else "OK (khong con du lieu test)"


def main():
    ok = True

    # 1. Login — dùng SĐT của tài xế đang có chuyến (0909000039 = SP00005764)
    st, data = req("POST", f"{PREFIX}/auth/dev-login", {"phone": os.environ.get("DRIVER_PHONE", "0909000039")})
    if not check("E1 dev-login HTTP 200", st == 200, f"status={st}"):
        print(f"    body: {str(data)[:300]}")
        return 1
    token = data.get("access_token") or (data.get("data") or {}).get("access_token")
    ok &= check("E2 access_token trả về", bool(token))
    user = data.get("user") or (data.get("data") or {}).get("user") or {}
    print(f"    user_id={user.get('user_id')} role={user.get('role')} tenant={user.get('tenant_id')}")

    # 2. Trips list
    st, trips = req("GET", f"{PREFIX}/sales/trips?status=DELIVERING", token=token)
    ok &= check("E3 GET /api/sales/trips HTTP 200", st == 200, f"status={st}")
    trip_list = trips if isinstance(trips, list) else (trips or {}).get("items", [])
    print(f"    so chuyen DELIVERING = {len(trip_list)}")
    if not trip_list:
        # thu PREPARING
        st, trips = req("GET", f"{PREFIX}/sales/trips?status=PREPARING", token=token)
        trip_list = trips if isinstance(trips, list) else (trips or {}).get("items", [])
        print(f"    so chuyen PREPARING = {len(trip_list)}")
    ok &= check("E4 co it nhat 1 chuyen", len(trip_list) > 0)
    if not trip_list:
        return 0 if ok else 1

    # ★ Chỉ chọn chuyến CỦA HÔM NAY.
    #   Anti-fraud chặn `BACKDATED_TRIP`: không được giao lại chuyến của
    #   ngày trước. Chọn chuyến cũ ⇒ POD bị từ chối 400 ⇒ smoke test
    #   fail vì lý do sai (không phải lỗi hệ thống).
    today = datetime.now().date().isoformat()
    today_trips = [t for t in trip_list if (t.get("trip_date") or "")[:10] == today]

    if not today_trips:
        # ── Tự tạo chuyến của hôm nay để test POD ──
        print(f"    -> Khong co chuyen hom nay ({today}); tao chuyen moi de test POD")
        trip_id = setup_trip_for_today(token, today)
        if trip_id is None:
            ok &= check("E4b tao duoc chuyen hom nay de tao POD", False)
            print("\n" + ("KET QUA: FAIL" if ok else "KET QUA: FAIL"))
            return 0 if ok else 1
        ok &= check("E4b tao duoc chuyen hom nay de tao POD", True, f"trip_id={trip_id}")
        st, trips = req("GET", f"{PREFIX}/sales/trips?status=DELIVERING", token=token)
        trip_list = trips if isinstance(trips, list) else (trips or {}).get("items", [])
        today_trips = [t for t in trip_list if (t.get("trip_date") or "")[:10] == today]
        if not today_trips:
            print("    [WARN] Van khong thay chuyen hom nay sau khi tao")
            print("\n" + ("KET QUA: FAIL" if ok else "KET QUA: FAIL"))
            return 0 if ok else 1

    trip_list = today_trips
    print(f"    -> chon chuyen hom nay ({today}): {len(trip_list)} chuyen")

    trip = trip_list[0]
    trip_id = trip.get("trip_id")
    print(f"    trip_id={trip_id} number={trip.get('trip_number')} orders={trip.get('total_orders')}")

    # 3. Trip detail
    st, detail = req("GET", f"{PREFIX}/sales/trips/{trip_id}", token=token)
    ok &= check("E5 GET trip detail HTTP 200", st == 200, f"status={st}")
    orders = (detail or {}).get("orders", [])
    ok &= check("E6 trip co don hang", len(orders) > 0, f"orders={len(orders)}")
    if orders:
        o = orders[0]
        print(f"    don dau: trip_order_id={o.get('trip_order_id')} so={o.get('so_number')} "
              f"partner={o.get('partner_name')} total={o.get('total_amount')} "
              f"old_debt={o.get('old_debt_amount')}")

    # 4/5. POD idempotency
    if orders:
        o = orders[0]
        # ★ UUID NGẪU NHIÊN mỗi lần chạy: nếu dùng client_pod_id cố định
        #   thì lần chạy sau sẽ luôn trả về POD cũ (idempotency) ⇒ test
        #   "created=true" luôn FAIL dù hệ thống hoàn toàn đúng.
        #   Đồng thời POD cũ sẽ để lại dữ liệu test trong DB.
        cid = str(uuid.uuid4())
        manifest_add("client_pod_ids", cid)
        manifest_add("trip_order_ids", o.get("trip_order_id"))
        manifest_add("sales_order_ids", o.get("so_id"))
        old_debt = float(o.get("old_debt_amount") or 0)
        # Thu công nợ cũ không được vượt quá nợ thực tế của khách
        old_cash = min(500.0, old_debt)
        payload = {
            "client_pod_id": cid,
            "trip_order_id": o.get("trip_order_id"),
            "so_id": o.get("so_id"),
            "partner_id": o.get("partner_id"),
            "delivery_status": "DELIVERED",
            "new_order_cash": 1000,
            "new_order_transfer": 0,
            "new_order_credit": 0,
            "old_debt_cash": old_cash,
            "old_debt_transfer": 0,
            "old_debt_credit": 0,
            "total_collected": 1000 + old_cash,
            "recipient_name": "E2E Test",
            "notes": "e2e",
        }

        # ★ Anti-fraud GPS GATE: nếu khách CÓ toạ độ, POD phải kèm GPS
        #   nằm trong bán kính 100m. Lấy toạ độ của khách từ master
        #   data và gửi kèm (giả lập tài xế đứng tại điểm giao).
        st, mparts = req("GET", f"{PREFIX}/driver/master/partners", token=token)
        plist = mparts if isinstance(mparts, list) else (mparts or {}).get("items", [])
        pid = o.get("partner_id")
        gps_partner = next((p for p in plist if p.get("partner_id") == pid), None)
        if gps_partner and gps_partner.get("latitude") is not None:
            payload["delivery_latitude"] = gps_partner["latitude"]
            payload["delivery_longitude"] = gps_partner["longitude"]
            payload["delivery_gps_accuracy_m"] = 10
            payload["gps_captured_at"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.000Z")
            print(f"    GPS gan theo toa do khach: "
                  f"{gps_partner['latitude']}, {gps_partner['longitude']}")
        else:
            print("    [WARN] Khach khong co GPS -> bo qua GPS gate")

        st, r1 = req("POST", f"{PREFIX}/sales/pod", payload, token=token)
        ok &= check("E7 POST /api/sales/pod HTTP 200", st == 200, f"status={st} body={str(r1)[:200]}")
        created1 = (r1 or {}).get("created")
        ok &= check("E8 lan 1 created=true", created1 is True, f"created={created1}")

        st, r2 = req("POST", f"{PREFIX}/sales/pod", payload, token=token)
        ok &= check("E9 lan 2 HTTP 200 (idempotent)", st == 200, f"status={st}")
        created2 = (r2 or {}).get("created")
        ok &= check("E10 lan 2 created=false (KHONG tao POD trung)",
                   created2 is False, f"created={created2}")
        pid1 = (r1 or {}).get("pod_id")
        pid2 = (r2 or {}).get("pod_id")
        manifest_add("pod_ids", pid1)
        manifest_save()
        ok &= check("E11 pod_id giong nhau giua 2 lan", bool(pid1) and pid1 == pid2,
                   f"{pid1} vs {pid2}")
        ok &= check("E12 total_collected = tong 6 o",
                    (r1 or {}).get("total_collected") == 1000 + old_cash,
                    f"total_collected={(r1 or {}).get('total_collected')}")

        # E18: server phai TU CHOI thu cong no cu vuot nang cua khach
        bad = dict(payload)
        bad["client_pod_id"] = str(uuid.uuid4())
        bad["old_debt_cash"] = old_debt + 10_000_000
        bad["total_collected"] = 1000 + bad["old_debt_cash"]
        st, r3 = req("POST", f"{PREFIX}/sales/pod", bad, token=token)
        ok &= check("E18 server TU CHOI thu cong no vuot nang (HTTP 400)",
                   st == 400, f"status={st} body={str(r3)[:150]}")

    # 6. Master data
    st, parts = req("GET", f"{PREFIX}/driver/master/partners", token=token)
    ok &= check("E13 GET master/partners HTTP 200", st == 200, f"status={st}")
    plist = parts if isinstance(parts, list) else (parts or {}).get("items", [])
    ok &= check("E14 master partners co du lieu", len(plist) > 0, f"n={len(plist)}")
    if plist:
        print(f"    mau partner: {plist[0]}")

    st, prods = req("GET", f"{PREFIX}/driver/master/products", token=token)
    ok &= check("E15 GET master/products HTTP 200", st == 200, f"status={st}")
    prlist = prods if isinstance(prods, list) else (prods or {}).get("items", [])
    ok &= check("E16 master products co du lieu", len(prlist) > 0, f"n={len(prlist)}")

    # 7. Auth guard
    st, _ = req("GET", f"{PREFIX}/sales/trips")
    ok &= check("E17 endpoint cam JWT tra 401", st == 401, f"status={st}")

    # 8. Don du lieu test (POD + ledger + trip + paid_amount don hang).
    #    Bat buoc: khong don thi moi lan chay se lam `08_data_regression.py
    #    --check` FAIL (hash khac baseline) va du lieu nghiep vu bi nhieu.
    cleanup_result = cleanup_test_data()
    if cleanup_result is None:
        print("    [WARN] Khong cleanup duoc (thieu pyodbc / loi ket noi) — "
              "hay chay: python scripts/10_cleanup_e2e_data.py")
    else:
        print(f"    -> Da cleanup: {cleanup_result}")

    print("\n" + ("KET QUA: PASS" if ok else "KET QUA: FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
