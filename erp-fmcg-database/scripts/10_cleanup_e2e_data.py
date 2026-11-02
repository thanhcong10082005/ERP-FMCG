"""Cleanup du lieu test E2E — khoi phuc DB ve trang thai truoc khi chay smoke test.

VSAO CAN SCRIPT NAY
------------------
`e2e_smoke_test.py` tao POD that tren DB that (khong dung DB test rieng).
Neu khong don, moi lan chay smoke test la:
  - them 1 dong `tenant.delivery_pod`
  - them 2 dong `tenant.stock_ledger` (OPENING + SALE)
  - them 1 chuyen xe + 1 `delivery_trip_orders`
  - sua `tenant.sales_orders.paid_amount` / `payment_status` cua don hang
  - sua bieu tong tien cua chuyen xe

=> `08_data_regression.py --check` FAIL (hash khac baseline) va du lieu
   nghiep vu bi nhieu. Script nay khoi phuc lai dung trang thai ban dau.

CACH DUNG
---------
    python scripts/10_cleanup_e2e_data.py            # don data E2E gan nhat
    python scripts/10_cleanup_e2e_data.py --all      # don TAT CA data test
    python scripts/10_cleanup_e2e_data.py --dry-run  # chi xem, khong xoa

Tieu chi cleanup
----------------
Chi xoa nhung ban ghi co dau hieu test:
  - POD co `client_pod_id` / `notes` / `recipient_name` dac thuoc E2E
  - `stock_ledger` tro toi POD do qua `reference_id`
  - chuyen xe co `trip_number` khu mau test E2E
KHONG xoa don hang that, khong xoa ledger giao hang that.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import datetime, timezone

try:
    import pyodbc
except ImportError:  # pragma: no cover
    print("LOI: can cai pyodbc (pip install pyodbc)", file=sys.stderr)
    raise SystemExit(2)

DEFAULT_SERVER = r".\SQLEXPRESS"
DEFAULT_DATABASE = "ERP_FMCG"

# Nhan dien ban ghi test E2E.
POD_NOTE_MARKERS = ("e2e", "E2E", "smoke test", "Smoke Test")
POD_RECIPIENT_MARKERS = ("E2E Test", "E2E")
# Chuyen xe E2E duoc tao voi `vehicle_plate = 'E2E-TEST'` (xem e2e_smoke_test.py).
# Khong dua vao trip_number vi dispatcher tu sinh (VD: TRIP-20261102-1308).
E2E_VEHICLE_PLATES = ("E2E-TEST", "E2E TEST")

# Cac cot cua sales_orders bi POD sua -> can khoi phuc.
ORDER_COLUMNS_RESTORE = ("paid_amount", "payment_status", "status", "updated_at")


def connect(server: str, database: str) -> pyodbc.Connection:
    cs = (
        f"DRIVER={{SQL Server}};SERVER={server};"
        f"DATABASE={database};Trusted_Connection=yes;"
        "Encrypt=no;TrustServerCertificate=yes;"
    )
    return pyodbc.connect(cs, timeout=30)


def find_e2e_pods(cur) -> list[tuple]:
    """Tra ve danh sach POD duoc tao boi E2E smoke test."""
    cur.execute(
        """
        SELECT id, client_pod_id, trip_order_id, sales_order_id, partner_id,
               new_order_cash, new_order_transfer, new_order_credit,
               old_debt_cash, old_debt_transfer, old_debt_credit,
               total_collected, notes, recipient_name
        FROM tenant.delivery_pod
        """
    )
    pods = []
    for r in cur.fetchall():
        notes = (r[12] or "").strip()
        recipient = (r[13] or "").strip()
        if notes in POD_NOTE_MARKERS or recipient in POD_RECIPIENT_MARKERS:
            pods.append(tuple(r))
    return pods


def find_e2e_trips(cur) -> list[tuple]:
    """Tra ve chuyen xe duoc tao boi E2E.

    Nhan dien bang `vehicle_plate` (E2E tao chuyen voi bien so 'E2E-TEST')
    HOAC bang `notes` chua 'e2e'. KHONG dua vao trip_number vi dispatcher
    tu sinh (VD: TRIP-20261102-1308) — khong phai dau hieu test.
    """
    cur.execute(
        "SELECT id, trip_number, vehicle_plate, notes FROM tenant.delivery_trips"
    )
    trips = []
    for r in cur.fetchall():
        plate = (r[2] or "").strip().upper()
        notes = (r[3] or "").strip().lower()
        if plate in E2E_VEHICLE_PLATES or "e2e" in notes:
            trips.append((str(r[0]), r[1]))
    return trips


def find_e2e_trip_orders(cur, trip_ids: list[str]) -> list[str]:
    """Tra ve id cac dong delivery_trip_orders thuoc chuyen xe test."""
    if not trip_ids:
        return []
    placeholders = ",".join("?" for _ in trip_ids)
    cur.execute(
        f"SELECT id FROM tenant.delivery_trip_orders WHERE trip_id IN ({placeholders})",
        *trip_ids,
    )
    return [str(r[0]) for r in cur.fetchall()]


def restore_orders(cur, pod_rows: list[tuple], dry_run: bool,
                   extra_so_ids: list[str] | None = None) -> int:
    """Khoi phuc paid_amount / payment_status cua don hang bi POD sua.

    Logic: don hang da giao trong E2E co `payment_status` = PARTIAL/PAID
    va `paid_amount` = tong da thu. Khoi phuc ve UNPAID/0 — dung trang
    thai cua don chua giao (baseline).

    `extra_so_ids` la don hang trong manifest E2E (dam bao don duoc chon
    nhung khong tao POD van duoc khoi phuc neu bi sua).
    """
    restored = 0
    so_ids = {row[3] for row in pod_rows}
    if extra_so_ids:
        so_ids.update(extra_so_ids)

    for sales_order_id in so_ids:
        cur.execute(
            "SELECT paid_amount, payment_status FROM tenant.sales_orders WHERE id = ?",
            sales_order_id,
        )
        r = cur.fetchone()
        if r is None:
            continue
        paid = r[0] or 0
        status = r[1]
        # Chi khoi phuc dong co thay doi that (paid > 0).
        if float(paid) == 0.0 and (status or "").upper() == "UNPAID":
            continue
        print(f"    - sales_order {sales_order_id}: paid {paid} / {status} -> 0.00 / UNPAID")
        if not dry_run:
            cur.execute(
                """
                UPDATE tenant.sales_orders
                SET paid_amount = 0, payment_status = 'UNPAID', updated_at = ?
                WHERE id = ?
                """,
                datetime.now(timezone.utc).replace(tzinfo=None),
                sales_order_id,
            )
        restored += 1
    return restored


def delete_pod_ledger(cur, pod_ids: list[str], dry_run: bool) -> int:
    """Xoa dong stock_ledger lien quan toi POD.

    Luu y ve cau truc that cua ledger:
      - Dong `SALE` giao hang: `reference_type = 'SALES_ORDER'`,
        `reference_id` = sales_order_id (KHONG phai pod_id), va POD id
        chi xuat hien trong `notes` ("POD <uuid> - SO...").
      - Dong `OPENING` tu dong nap ton: `reference_type = 'OPENING'`,
        `reference_id` = NULL, POD id cung chi nam trong `notes`.

    => Phai dua vao `notes` chu khong dua vao `reference_id`.
    """
    if not pod_ids:
        return 0

    # (a) Xoa theo notes chua POD id (chinh xac nhat).
    deleted = 0
    for pod_id in pod_ids:
        cur.execute(
            "SELECT COUNT(*) FROM tenant.stock_ledger WHERE notes LIKE ?",
            f"%{pod_id.lower()}%",
        )
        cnt = cur.fetchone()[0]
        if cnt:
            print(f"    - stock_ledger: {cnt} dong thuoc POD {pod_id}")
            if not dry_run:
                cur.execute(
                    "DELETE FROM tenant.stock_ledger WHERE notes LIKE ?",
                    f"%{pod_id.lower()}%",
                )
            deleted += cnt

    # (b) Xoa theo notes chua client_pod_id (phong khi notes doi format).
    return deleted


def delete_pod_rows(cur, pod_ids: list[str], dry_run: bool) -> int:
    """Xoa pod_items truoc (FK), sau do xoa delivery_pod."""
    if not pod_ids:
        return 0
    placeholders = ",".join("?" for _ in pod_ids)
    if not dry_run:
        cur.execute(
            f"DELETE FROM tenant.pod_items WHERE pod_id IN ({placeholders})",
            *pod_ids,
        )
        cur.execute(
            f"DELETE FROM tenant.pod_attachments WHERE pod_id IN ({placeholders})",
            *pod_ids,
        )
        cur.execute(
            f"DELETE FROM tenant.delivery_pod WHERE id IN ({placeholders})",
            *pod_ids,
        )
    return len(pod_ids)


def delete_trip_orders(cur, order_ids: list[str], dry_run: bool) -> int:
    if not order_ids:
        return 0
    placeholders = ",".join("?" for _ in order_ids)
    if not dry_run:
        cur.execute(
            f"DELETE FROM tenant.delivery_trip_orders WHERE id IN ({placeholders})",
            *order_ids,
        )
    return len(order_ids)


def delete_trips(cur, trip_ids: list[str], dry_run: bool) -> int:
    if not trip_ids:
        return 0
    placeholders = ",".join("?" for _ in trip_ids)
    if not dry_run:
        cur.execute(
            f"DELETE FROM tenant.delivery_trip_orders WHERE trip_id IN ({placeholders})",
            *trip_ids,
        )
        cur.execute(
            f"DELETE FROM tenant.delivery_trips WHERE id IN ({placeholders})",
            *trip_ids,
        )
    return len(trip_ids)


def recompute_inventory_balances(cur, dry_run: bool) -> int:
    """Tinh lai `tenant.inventory_balances` tu `tenant.stock_ledger`.

    BAT BUOC: trigger `trg_stockledger_after_insert` chi chay AFTER INSERT —
    khi xoa ledger, balance khong tu dong lui. Neu khong tinh lai, ton kho
    se bi lech (nhieu hon thuc te) va check "inventory_matches_ledger" fail.

    Dong bo cot `quantity_on_hand`:
      - `quantity_available` va `total_value` la COMPUTED COLUMN
        (SQL Server tu tinh) -> khong UPDATE truc tiep duoc (loi 271).
      - `average_cost` chi ghi khi co gia von > 0.
    """
    cur.execute(
        """
        SELECT b.id, b.product_id, b.uom_code, b.quantity_on_hand,
               b.average_cost,
               ISNULL(s.net, 0) AS net_from_ledger,
               ISNULL(s.avg_cost, 0) AS avg_cost_from_ledger
        FROM tenant.inventory_balances b
        LEFT JOIN (
            SELECT product_id, uom_code,
                   SUM(quantity_in - quantity_out) AS net,
                   CASE WHEN SUM(quantity_in - quantity_out) > 0
                        THEN SUM(quantity_in * ISNULL(unit_cost, 0))
                             / SUM(quantity_in - quantity_out)
                        ELSE 0 END AS avg_cost
            FROM tenant.stock_ledger
            GROUP BY product_id, uom_code
        ) s ON s.product_id = b.product_id AND s.uom_code = b.uom_code
        """
    )
    rows = cur.fetchall()
    fixed = 0
    for r in rows:
        current = float(r[3] or 0)
        expected = float(r[5] or 0)
        if abs(current - expected) < 1e-9:
            continue
        avg_cost = float(r[6] or 0)
        print(f"    - inventory_balance {r[1]}/{r[2]}: {current} -> {expected}")
        if not dry_run:
            cur.execute(
                """
                UPDATE tenant.inventory_balances
                SET quantity_on_hand = ?,
                    average_cost = CASE WHEN ? > 0 THEN ? ELSE average_cost END,
                    last_transaction_at = ?, updated_at = ?
                WHERE id = ?
                """,
                expected,
                avg_cost,
                avg_cost,
                datetime.now(timezone.utc).replace(tzinfo=None),
                datetime.now(timezone.utc).replace(tzinfo=None),
                r[0],
            )
        fixed += 1
    return fixed


def load_manifest() -> dict:
    """Doc manifest E2E (danh sach id da tao) de xoa CHINH XAC.

    Manifest duoc `e2e_smoke_test.py` ghi lai cung thu muc voi script
    backend: erp-fmcg-backend-csharp/scripts/.e2e_manifest.json
    """
    here = os.path.dirname(os.path.abspath(__file__))
    candidates = [
        os.path.normpath(os.path.join(here, "..", "..", "erp-fmcg-backend-csharp",
                                     "scripts", ".e2e_manifest.json")),
        os.path.normpath(os.path.join(here, "..", "erp-fmcg-backend-csharp",
                                     "scripts", ".e2e_manifest.json")),
    ]
    for path in candidates:
        try:
            with open(path, "r", encoding="utf-8") as fh:
                data = json.load(fh)
            if isinstance(data, dict):
                return data
        except (OSError, ValueError):
            continue
    return {}


def delete_orphan_ledger(cur, dry_run: bool) -> int:
    """Xoa dong `stock_ledger` mo tro toi POD da bi xoa (orphan).

    BAO HIEM CHI XOA DONG AN TOAN:
    Baseline (20 dong) gom cac dong `SALE` tro toi POD da xoa tu truoc
    (POD cu khong con trong DB) — DO DAY LA DU LIEU THAT, KHONG duoc xoa.

    => Chi xoa dong orphan co dau hieu la du lieu SINH RA BOI E2E:
      - `notes` chua marker `TON_AM_GIAI_QUYET` — chi ton ra khi
        `DeliveryPodService` tu dong nap ton am de giao hang (tinh nang
        moi them trong Phase 7, chi kich hoat trong kiem thu).
      - `notes` chua marker `Tu dong nap ton dau ky de giao hang` — dong
        `OPENING` cung do E2E sinh ra.
    """
    cur.execute(
        """
        SELECT id, notes FROM tenant.stock_ledger
        WHERE notes LIKE '%TON_AM_GIAI_QUYET%'
           OR notes LIKE '%Tu dong nap ton dau ky de giao hang%'
        """
    )
    rows = cur.fetchall()
    if not rows:
        return 0
    for r in rows:
        print(f"    - orphan stock_ledger {r[0]}: {(r[1] or '')[:70]}")
        if not dry_run:
            cur.execute("DELETE FROM tenant.stock_ledger WHERE id = ?", r[0])
    return len(rows)


def main() -> int:
    ap = argparse.ArgumentParser(description="Don du lieu test E2E khoi phuc DB ve baseline")
    ap.add_argument("--server", default=os.environ.get("DB_SERVER", DEFAULT_SERVER))
    ap.add_argument("--database", default=os.environ.get("DB_NAME", DEFAULT_DATABASE))
    ap.add_argument("--all", action="store_true",
                    help="Don ca du lieu test (ke ca chuyen xe co bien so E2E-TEST)")
    ap.add_argument("--dry-run", action="store_true", help="Chi xem, khong ghi vao DB")
    args = ap.parse_args()

    print("=" * 70)
    print(" CLEANUP DU LIEU TEST E2E")
    print(f" server={args.server} database={args.database}"
          f"{'  [DRY-RUN]' if args.dry_run else ''}")
    print("=" * 70)

    try:
        conn = connect(args.server, args.database)
    except Exception as exc:
        print(f"LOI ket noi: {exc}", file=sys.stderr)
        return 2

    cur = conn.cursor()
    try:
        manifest = load_manifest()
        m_pod_ids = [str(x) for x in manifest.get("pod_ids", [])]
        m_client_ids = [str(x) for x in manifest.get("client_pod_ids", [])]
        m_trip_ids = [str(x) for x in manifest.get("trip_ids", [])]
        m_trip_order_ids = [str(x) for x in manifest.get("trip_order_ids", [])]
        m_so_ids = [str(x) for x in manifest.get("sales_order_ids", [])]

        if m_pod_ids or m_trip_ids:
            print(f"  [0] Manifest E2E            : "
                  f"pods={len(m_pod_ids)} trips={len(m_trip_ids)} "
                  f"trip_orders={len(m_trip_order_ids)}")

        pods = find_e2e_pods(cur)
        trips = find_e2e_trips(cur)
        trip_ids = [t[0] for t in trips]
        trip_order_ids = find_e2e_trip_orders(cur, trip_ids)

        # Hop don id tu manifest vao danh sach tim thay.
        pod_ids = {str(p[0]) for p in pods} | set(m_pod_ids)
        trip_ids = set(trip_ids) | set(m_trip_ids)
        trip_order_ids = set(trip_order_ids) | set(m_trip_order_ids)

        print(f"  [1] POD test tim thay      : {len(pods)} (+manifest {len(m_pod_ids)})")
        print(f"  [2] Chuyen xe test tim thay: {len(trips)} (+manifest {len(m_trip_ids)})")
        print(f"  [3] Trip orders test       : {len(trip_order_ids)}")

        pod_ids = list(pod_ids)
        trip_ids = list(trip_ids)
        trip_order_ids = list(trip_order_ids)

        n_ledger = delete_pod_ledger(cur, pod_ids + m_client_ids, args.dry_run)
        print(f"  [4] Xoa stock_ledger (POD)  : {n_ledger}")

        n_orphan = delete_orphan_ledger(cur, args.dry_run)
        print(f"  [4b] Xoa stock_ledger orphan: {n_orphan}")

        n_pod = delete_pod_rows(cur, pod_ids, args.dry_run)
        print(f"  [5] Xoa delivery_pod       : {n_pod}")

        n_orders = restore_orders(cur, pods, args.dry_run, m_so_ids)
        print(f"  [6] Khoi phuc sales_orders  : {n_orders}")

        n_trip_orders = delete_trip_orders(cur, trip_order_ids, args.dry_run)
        print(f"  [7] Xoa delivery_trip_orders: {n_trip_orders}")

        n_trips = delete_trips(cur, trip_ids, args.dry_run)
        print(f"  [8] Xoa delivery_trips     : {n_trips}")

        n_bal = recompute_inventory_balances(cur, args.dry_run)
        print(f"  [9] Tinh lai ton kho       : {n_bal}")

        conn.commit()
        print("=" * 70)
        print(" KET QUA: PASS" if not args.dry_run else " KET QUA: DRY-RUN (khong ghi)")
        print("=" * 70)
        return 0
    except Exception as exc:
        conn.rollback()
        print(f"LOI: {exc}", file=sys.stderr)
        return 1
    finally:
        conn.close()


if __name__ == "__main__":
    sys.exit(main())
