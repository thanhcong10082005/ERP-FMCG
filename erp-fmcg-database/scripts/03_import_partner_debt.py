#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
================================================================================
 ERP-FMCG - NẠP CÔNG NỢ ĐẦU KỲ (old_debt / current_debt)
================================================================================
 Nạp công nợ từ file gốc ERP cũ vào bảng `tenant.partners`.

 Nguồn
 -----
   CoreData/data/Danh sách khách hàng.xlsx - Danh sách khách hàng.csv
   Cột "Công nợ" (index 5). File có 3 dòng tiêu đề, dữ liệu từ dòng 4.

 Bẫy parse định dạng số kiểu Việt Nam (đã phát hiện & kiểm chứng)
 --------------------------------------------------------------------
   Trong file, số tiền dùng dấu CHẤM làm dấu phân cách nghìn:
       '21.234.000'  ->  21.234.000 đ    (KHÔNG phải 21.234)
       '(5.037.000)' -> -5.037.000 đ     (ngoặc đơn = số âm)
   Nếu parse bằng float()/Decimal() với culture 'en-US' thì
   '21.234.000' sẽ thành 21.234  -> SAI LỆCH 1.000 LẦN.
   => Bắt buộc dùng `parse_vn_decimal()` bên dưới.

 Kết quả phân tích đã kiểm chứng (29/10/2026)
 ---------------------------------------------
   File gốc        : 3.396 khách
   Có công nợ ≠ 0  : 559 khách (227 dương + 332 âm)
   Tổng công nợ    : 796.487.089
   Trùng DB        : 459 khách  -> NẠP
   Không có trong DB: 42 khách -> BỎ QUA (không tạo mới)

 Usage
 -----
   python 03_import_partner_debt.py --dry-run
   python 03_import_partner_debt.py
   python 03_import_partner_debt.py --server .\\SQLEXPRESS --database ERP_FMCG

================================================================================
"""

from __future__ import annotations

import argparse
import csv
import io
import logging
import os
import sys
from collections import Counter
from dataclasses import dataclass, field
from decimal import Decimal
from pathlib import Path
from typing import Any

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

try:
    import pyodbc
except ImportError:  # pragma: no cover
    print("Thieu pyodbc. Chay: pip install pyodbc", file=sys.stderr)
    raise

# =============================================================================
# CẤU HÌNH
# =============================================================================

SCRIPT_DIR = Path(__file__).resolve().parent
PACK_ROOT = SCRIPT_DIR.parent

DEFAULT_SERVER = os.environ.get("ERP_FMCG_SERVER", r".\SQLEXPRESS")
DEFAULT_DATABASE = os.environ.get("ERP_FMCG_DATABASE", "ERP_FMCG")

# Nguồn CSV gốc. Ưu tiên: biến môi trường -> trong Pack (migration_data)
# -> thư mục CoreData cạnh Pack -> thư mục cha.
#
# file nguồn đã được copy vào Pack/erp-fmcg-database/migration_data/
# -> repo tự chứa đầy đủ, không phụ thuộc thư mục ngoài.
_DEBT_FILE_NAME = "Danh sách khách hàng.xlsx - Danh sách khách hàng.csv"
_PACK_DEBT_FILE = "migration_data/cong_no_dau_ky/Danh_sach_khach_hang_cong_no.csv"


def _find_source_file() -> Path | None:
    env = os.environ.get("ERP_FMCG_DEBT_FILE")
    if env:
        p = Path(env).expanduser().resolve()
        return p if p.is_file() else None

    candidates: list[Path] = []
    # 1. Bản đóng gói trong Pack
    candidates.append(PACK_ROOT / _PACK_DEBT_FILE)
    # 2. CoreData cạnh Pack (môi trường dev)
    for base in (PACK_ROOT.parent, PACK_ROOT.parent.parent, PACK_ROOT):
        candidates.append(base / "CoreData" / "data" / _DEBT_FILE_NAME)
        candidates.append(base / "data" / _DEBT_FILE_NAME)
    for c in candidates:
        if c.is_file():
            return c
    return None


log = logging.getLogger("debt_import")

# =============================================================================
# PARSER SỐ KIỂU VIỆT NAM  ★ CORE CỦA SCRIPT NÀY
# =============================================================================

def parse_vn_decimal(raw: str | None) -> Decimal:
    """Parse số tiền theo định dạng Việt Nam.

    Quy tắc:
      - Dấu CHẤM '.' = dấu phân cách nghìn -> bỏ hết
      - Dấu PHẨY ',' = dấu thập phân -> đổi thành '.'
      - Ngoặc đơn bao ngoài = số ÂM
      - Rỗng / '0' -> Decimal(0)

    Ví dụ:
      '21.234.000'  -> Decimal('21234000')
      '(5.037.000)' -> Decimal('-5037000')
      '1.234,56'    -> Decimal('1234.56')
    """
    if raw is None:
        return Decimal(0)

    s = str(raw).strip()
    if not s or s == "0":
        return Decimal(0)

    negative = False
    if s.startswith("(") and s.endswith(")"):
        negative = True
        s = s[1:-1].strip()

    # Bỏ mọi ký tự không phải số/dấu thập phân/dấu phẩy
    s = s.replace(" ", "").replace("\u00a0", "")
    s = s.replace(".", "").replace(",", ".")

    # Loại ký tự lạ còn sót (vd ký hiệu tiền tệ)
    allowed = set("0123456789.-")
    s = "".join(ch for ch in s if ch in allowed)

    if not s or s in ("-", ".", "-."):
        return Decimal(0)

    try:
        value = Decimal(s)
    except Exception:
        log.warning("Khong parse duoc gia tri: %r -> 0", raw)
        return Decimal(0)

    return -value if negative else value


# =============================================================================
# DOC FILE GOC
# =============================================================================

@dataclass
class DebtRow:
    partner_code: str
    partner_name: str
    debt: Decimal
    raw: str


@dataclass
class LoadReport:
    total_rows: int = 0
    nonzero_file: int = 0
    matched_db: int = 0
    matched_nonzero: int = 0
    updated: int = 0
    skipped_not_in_db: int = 0
    skipped_not_in_db_nonzero: int = 0
    skipped_zero: int = 0
    skipped_existing_debt: int = 0
    total_debt_file: Decimal = Decimal(0)
    total_debt_loaded: Decimal = Decimal(0)
    total_debt_skipped: Decimal = Decimal(0)
    not_in_db: list[tuple[str, str, Decimal]] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)


def read_source_csv(path: Path) -> list[DebtRow]:
    """Đọc file gốc, trả về các dòng có công nợ khác 0."""
    rows: list[DebtRow] = []
    # File có 3 dòng tiêu đề -> dữ liệu bắt đầu từ dòng thứ 4 (index 3)
    with io.open(path, "r", encoding="utf-8-sig", errors="replace", newline="") as fh:
        reader = csv.reader(fh)
        for idx, row in enumerate(reader):
            if idx < 3:
                continue  # bỏ 3 dòng tiêu đề
            if not row:
                continue
            if len(row) <= 5:
                continue
            code = (row[1] or "").strip()
            if not code:
                continue
            raw = row[5] or ""
            debt = parse_vn_decimal(raw)
            if debt == 0:
                continue
            name = (row[2] or "").strip()
            rows.append(DebtRow(code, name, debt, raw.strip()))
    return rows


# =============================================================================
# DOC / GHI DB
# =============================================================================

def fetch_db_partners(conn: Any) -> dict[str, tuple[str, Decimal, Decimal]]:
    """Lấy partner_code -> (id, current_debt, old_debt) từ DB.

    Lưu ý: bảng `tenant.partners` (TEMPORAL) trong MS SQL KHÔNG có cột
    `deleted_at` — soft-delete chưa được implement ở schema hiện tại.
    """
    cur = conn.cursor()
    cur.execute(
        """
        SELECT id, partner_code, current_debt, old_debt
        FROM tenant.partners
        """
    )
    out: dict[str, tuple[str, Decimal, Decimal]] = {}
    while True:
        rows = cur.fetchmany(2000)
        if not rows:
            break
        for r in rows:
            out[str(r[1]).strip()] = (
                str(r[0]),
                Decimal(r[2] or 0),
                Decimal(r[3] or 0),
            )
    cur.close()
    return out


def load(conn: Any, rows: list[DebtRow], dry_run: bool) -> LoadReport:
    rep = LoadReport()
    rep.total_rows = len(rows)
    rep.nonzero_file = len(rows)
    rep.total_debt_file = sum((r.debt for r in rows), Decimal(0))

    db_partners = fetch_db_partners(conn)
    log.info("DB co %d khach hang", len(db_partners))

    # Gom theo batch 500 để UPDATE hiệu quả
    to_update: list[DebtRow] = []

    for r in rows:
        hit = db_partners.get(r.partner_code)
        if hit is None:
            # ★ Chốt của anh: BỎ QUA, không tạo mới khách hàng
            rep.skipped_not_in_db += 1
            rep.skipped_not_in_db_nonzero += 1
            rep.total_debt_skipped += r.debt
            rep.not_in_db.append((r.partner_code, r.partner_name, r.debt))
            continue

        rep.matched_db += 1
        rep.matched_nonzero += 1

        partner_id, current_debt, old_debt = hit
        # An toàn dữ liệu: không ghi đè dòng đã có công nợ (không thể
        # xác định được nguồn -> giữ nguyên, báo cáo).
        if current_debt != 0 or old_debt != 0:
            rep.skipped_existing_debt += 1
            log.warning(
                "BO QUA (da co cong no): %s current_debt=%s old_debt=%s",
                r.partner_code, current_debt, old_debt,
            )
            continue

        to_update.append(r)
        rep.total_debt_loaded += r.debt

    if dry_run:
        log.info("DRY-RUN: se cap nhat %d khach hang", len(to_update))
        return rep

    cur = conn.cursor()
    sql = """
        UPDATE tenant.partners
        SET old_debt = ?, current_debt = ?, old_debt_date = NULL, updated_at = GETDATE()
        WHERE id = ?
    """
    batch: list[tuple] = []
    for r in to_update:
        partner_id = db_partners[r.partner_code][0]
        # current_debt = old_debt: cong no cu chua thu = cong no hien tai
        batch.append((r.debt, r.debt, partner_id))
        if len(batch) >= 500:
            cur.executemany(sql, batch)
            batch.clear()
    if batch:
        cur.executemany(sql, batch)

    conn.commit()
    rep.updated = len(to_update)
    cur.close()
    return rep


# =============================================================================
# BAO CAO
# =============================================================================

def print_report(rep: LoadReport, source: Path | None, dry_run: bool) -> None:
    bar = "=" * 74
    print("\n" + bar)
    print(" BÁO CÁO NẠP CÔNG NỢ ĐẦU KỲ")
    print(bar)
    if source:
        print(f" Nguồn            : {source}")
    print(f" Chế độ           : {'DRY-RUN (không ghi)' if dry_run else 'GHI VÀO DB'}")
    print("-" * 74)
    print(f" Dòng đọc từ file          : {rep.total_rows:>12,}")
    print(f" Có công nợ khác 0          : {rep.nonzero_file:>12,}")
    print(f" Tổng công nợ trong file   : {rep.total_debt_file:>12,}")
    print("-" * 74)
    print(f" Trùng DB (sẽ nạp)         : {rep.matched_nonzero:>12,}")
    print(f"   └ tổng tiền sẽ nạp     : {rep.total_debt_loaded:>12,}")
    print(f" BỎ QUA - không có trong DB : {rep.skipped_not_in_db_nonzero:>12,}")
    print(f"   └ tổng tiền bỏ qua     : {rep.total_debt_skipped:>12,}")
    print(f" BỎ QUA - đã có công nợ    : {rep.skipped_existing_debt:>12,}")
    print("-" * 74)
    print(f" Số dòng ĐÃ CẬP NHẬT       : {rep.updated:>12,}")
    print("=" * 74)

    if rep.not_in_db:
        print("\n BẢNG CHI TIẾT 42 KHÁCH BỎ QUA (không tạo mới):")
        print("-" * 74)
        print(f" {'Mã KH':<16}{'Tên':<38}{'Công nợ':>18}")
        print("-" * 74)
        for code, name, debt in sorted(rep.not_in_db, key=lambda x: -abs(x[2])):
            print(f" {code:<16}{name[:36]:<38}{debt:>18,}")
        print("-" * 74)

    if rep.errors:
        print("\n LỖI:")
        for e in rep.errors:
            print(f"  - {e}")


# =============================================================================
# MAIN
# =============================================================================

def main() -> int:
    parser = argparse.ArgumentParser(
        description="Nạp công nợ đầu kỳ (old_debt) cho 459 khách trùng DB"
    )
    parser.add_argument("--server", default=DEFAULT_SERVER)
    parser.add_argument("--database", default=DEFAULT_DATABASE)
    parser.add_argument("--source", default=None, help="Đường dẫn file CSV gốc")
    parser.add_argument("--dry-run", action="store_true",
                        help="Chỉ đọc + báo cáo, không ghi DB")
    parser.add_argument("--log-file", default=None)
    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)-7s %(message)s",
        handlers=[
            logging.StreamHandler(),
            *([logging.FileHandler(args.log_file, encoding="utf-8")]
              if args.log_file else []),
        ],
    )

    source = Path(args.source).expanduser().resolve() if args.source else _find_source_file()
    if source is None or not source.is_file():
        print("[ERROR] Khong tim thay file nguon cong no.", file=sys.stderr)
        print("        Dat ERP_FMCG_DEBT_FILE hoac truyen --source <duong-dan>.",
              file=sys.stderr)
        return 2
    log.info("Nguon: %s", source)

    log.info("Doc file...")
    rows = read_source_csv(source)
    log.info("Doc duoc %d dong co cong no khac 0", len(rows))
    if not rows:
        print("[WARN] File khong co dong cong nao khac 0.")
        return 0

    cs = (
        f"DRIVER={{SQL Server}};SERVER={args.server};"
        f"DATABASE={args.database};Trusted_Connection=yes;"
    )
    try:
        conn = pyodbc.connect(cs, timeout=30, autocommit=False)
    except Exception as exc:
        print(f"[ERROR] Khong ket noi duoc DB: {exc}", file=sys.stderr)
        return 3

    try:
        report = load(conn, rows, args.dry_run)
    except Exception as exc:
        try:
            conn.rollback()
        except Exception:
            pass
        print(f"[ERROR] Loi khi nap: {exc}", file=sys.stderr)
        return 4
    finally:
        conn.close()

    print_report(report, source, args.dry_run)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
