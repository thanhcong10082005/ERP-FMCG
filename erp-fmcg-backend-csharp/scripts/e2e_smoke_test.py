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

BASE = os.environ.get("API_BASE", "http://localhost:5090").rstrip("/")
PREFIX = "/api"


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
        cid = "22222222-2222-2222-2222-222222222222"
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
        ok &= check("E11 pod_id giong nhau giua 2 lan", bool(pid1) and pid1 == pid2,
                   f"{pid1} vs {pid2}")
        ok &= check("E12 total_collected = tong 6 o",
                    (r1 or {}).get("total_collected") == 1000 + old_cash,
                    f"total_collected={(r1 or {}).get('total_collected')}")

        # E18: server phai TU CHOI thu cong no cu vuot nang cua khach
        bad = dict(payload)
        bad["client_pod_id"] = "33333333-3333-3333-3333-333333333333"
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

    print("\n" + ("KET QUA: PASS" if ok else "KET QUA: FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
