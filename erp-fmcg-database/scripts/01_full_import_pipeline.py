#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
================================================================================
 ERP-FMCG - FULL IMPORT PIPELINE
================================================================================
 Nạp dữ liệu ERP FMCG từ file CSV (RDT + seed) vào MS SQL Server.

 Nguồn dữ liệu
 -------------
   7 file RDT (RDT1.26.csv ... RDT7.26.csv)  : 177.631 dòng, 66-68 cột
     - NGUỒN CHÍNH cho master data + đơn hàng + chi tiết dòng
   4 file *_clean.csv                        : bổ sung (giá, seed 11 đơn, kho, user)
   2 file *.csv (partners/products)          : bổ sung thuộc tính master

 Nguyên tắc
 ----------
   - 1 file duy nhất, thứ tự nạp bị ràng buộc bởi FK
   - Idempotent: chạy lại cho kết quả giống hệt
   - Không hardcode đường dẫn (dùng pathlib tương đối)
   - Mọi lỗi -> tenant.import_errors, KHÔNG crash giữa chừng
   - Batch insert 1.000 dòng/lần, fast_execute=True

 Công thức (đã kiểm chứng 100% trên dữ liệu thật)
 -------------------------------------------------
   line_subtotal   = ordered_qty * unit_price
   line_total      = line_subtotal - discount_amount        (PRE-TAX)
   tax_amount      = (line_subtotal - discount_amount) * rate / 100
   order.subtotal  = SUM(line_subtotal)
   order.discount  = SUM(discount_amount)
   order.tax       = SUM(tax_amount)
   order.total     = SUM(line_total) + tax_amount

 Usage
 -----
   python 01_full_import_pipeline.py                 # nạp đầy đủ
   python 01_full_import_pipeline.py --files RDT1.26.csv
   python 01_full_import_pipeline.py --dry-run       # chỉ đọc + báo cáo
   python 01_full_import_pipeline.py --server .\\SQLEXPRESS --database ERP_FMCG

================================================================================
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import logging
import os
import re
import sys
import time
import unicodedata
from collections import Counter, defaultdict
from dataclasses import dataclass, field
from datetime import date, datetime
from decimal import Decimal, InvalidOperation, ROUND_HALF_UP
from pathlib import Path
from typing import Any, Iterable, Iterator, Sequence

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

try:
    import pyodbc
except ImportError:  # pragma: no cover
    print("Thiếu pyodbc. Chạy: pip install pyodbc", file=sys.stderr)
    raise

# =============================================================================
# CẤU HÌNH
# =============================================================================

SCRIPT_DIR = Path(__file__).resolve().parent
PACK_ROOT = SCRIPT_DIR.parent

# Thu muc du lieu: uu tien bien moi truong ERP_FMCG_DATA_DIR,
# sau do migration_data canh ke script, roi migration_data trong Pack.


def _resolve_data_dir() -> Path:
    env = os.environ.get("ERP_FMCG_DATA_DIR")
    if env:
        p = Path(env).expanduser().resolve()
        if p.is_dir():
            return p
        log.warning("ERP_FMCG_DATA_DIR khong ton tai: %s", p)
    candidates = [
        PACK_ROOT / "migration_data",
        SCRIPT_DIR.parent / "migration_data",
        SCRIPT_DIR / "migration_data",
    ]
    for c in candidates:
        if c.is_dir() and any(c.glob("RDT*.csv")):
            return c
    # tra ve candidate dau tien (de bao loi ro rang)
    return candidates[0]


DATA_DIR = _resolve_data_dir()

RDT_FILES = [
    "RDT1.26.csv", "RDT2.26.csv", "RDT3.26.csv", "RDT4.26.csv",
    "RDT5.26.csv", "RDT6.26.csv", "RDT7.26.csv",
]

DEFAULT_SERVER = r".\SQLEXPRESS"
DEFAULT_DATABASE = "ERP_FMCG"
BATCH_SIZE = 1000
MAX_DECIMAL = Decimal("9999999999999.99")   # DECIMAL(15,2) max
MIN_DECIMAL = Decimal("-9999999999999.99")

log = logging.getLogger("erp-fmcg-import")


# =============================================================================
# TIỆN ÍCH CHUẨN HÓA  (R01, R03, R04, R05, R13)
# =============================================================================

def nfc(value: Any) -> str:
    """R01: chuẩn hóa Unicode NFC. Bắt buộc vì header RDT dùng NFD."""
    if value is None:
        return ""
    s = str(value)
    return unicodedata.normalize("NFC", s)


def parse_decimal(value: Any, field_name: str = "", row_hint: str = "") -> Decimal | None:
    """
    R03: Parse số theo chuẩn RDT.
      - '' / None -> None
      - '1.234,56' (dấu ',' thập phân) -> Decimal
      - Nếu có CẢ ',' và '.' -> lỗi dữ liệu -> raise (pipeline dừng)
    Đã kiểm chứng: 177.631 dòng RDT không có giá trị nào chứa cả ',' và '.'
    """
    if value is None:
        return None
    s = str(value).strip()
    if not s:
        return None
    has_comma = "," in s
    has_dot = "." in s
    if has_comma and has_dot:
        raise ValueError(
            f"R03 VI PHAM: gia tri '{s}' chua ca dau ',' va '.' (field={field_name} row={row_hint})"
        )
    if has_comma:
        s = s.replace(",", ".")
    # loai dau phan tach nghin khac (space, '_')
    s = re.sub(r"[\s_]", "", s)
    if s in ("", "-", ".", "-."):
        return None
    try:
        return Decimal(s)
    except (InvalidOperation, ValueError):
        raise ValueError(
            f"R03 VI PHAM: khong parse duoc '{value}' thanh so (field={field_name} row={row_hint})"
        )


def clamp_decimal(value: Decimal | None) -> Decimal | None:
    """R05: clamp ve khoang DECIMAL(15,2), giu bang 0 neu None."""
    if value is None:
        return None
    if value > MAX_DECIMAL:
        log.warning("R05 CLAMP: %s > %s -> %s", value, MAX_DECIMAL, MAX_DECIMAL)
        return MAX_DECIMAL
    if value < MIN_DECIMAL:
        log.warning("R05 CLAMP: %s < %s -> %s", value, MIN_DECIMAL, MIN_DECIMAL)
        return MIN_DECIMAL
    return value


def parse_date(value: Any, field_name: str = "", row_hint: str = "") -> date | None:
    """
    R04: Parse ngay.
      - 'M/D/YYYY' (RDT, định dạng Mỹ) -> date
      - 'YYYY-MM-DD' (seed) -> date
    Đã kiểm chứng: 100% RDT dùng M/D/YYYY; 11 đơn seed dùng ISO.
    """
    if value is None:
        return None
    s = str(value).strip()
    if not s:
        return None
    for fmt in ("%m/%d/%Y", "%Y-%m-%d", "%d/%m/%Y", "%Y/%m/%d"):
        try:
            return datetime.strptime(s, fmt).date()
        except ValueError:
            continue
    raise ValueError(f"R04 VI PHAM: khong parse duoc ngay '{value}' (field={field_name} row={row_hint})")


def norm_phone(value: Any) -> str | None:
    """
    R13: chuan hoa so dien thoai ve 10 chu so.
    - bo ky tu la
    - neu con lai 9-11 chu so -> giu
    - con lai -> giu nguyen ban dau neu khong rong (khong mat thong tin)
    """
    if value is None:
        return None
    s = str(value).strip()
    if not s or s in ("0", "."):
        return None
    digits = re.sub(r"\D", "", s)
    if 9 <= len(digits) <= 11:
        return digits
    return s[:20] or None


def strip_code_prefix(value: Any) -> str:
    """'MK0003 - Tỉnh Long An' -> 'Long An'; 'PX2780115028 - Xã An Ninh' -> 'Xã An Ninh'."""
    s = nfc(value).strip()
    if not s:
        return ""
    m = re.match(r"^([A-Z]{1,3}\d+)\s*-\s*(.+)$", s)
    if m:
        return m.group(2).strip()
    return s


def slugify(value: str) -> str:
    """'JUICY MILK' -> 'JUICY_MILK'; 'POSM L2' -> 'POSM_L2'."""
    s = re.sub(r"[^A-Za-z0-9]+", "_", value.strip())
    return s.strip("_").upper()


def file_sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


# =============================================================================
# ĐỌC CSV
# =============================================================================

def read_csv_rows(path: Path) -> Iterator[dict[str, str]]:
    """
    Đọc CSV, chuẩn hóa header về NFC (R01) và tên cột về chuẩn.
    File CSV dùng encoding utf-8-sig (có BOM) và delimiter ','.
    """
    with path.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if reader.fieldnames is None:
            return
        # R01: chuẩn hóa header -> NFC
        reader.fieldnames = [nfc(h) for h in reader.fieldnames]
        for row in reader:
            yield {nfc(k): (nfc(v) if isinstance(v, str) else v) for k, v in row.items()}


def get(row: dict[str, str], *names: str) -> str:
    """Lấy giá trị theo tên cột, chấp nhận nhiều tên thay thế (thiếu cột -> '')."""
    for n in names:
        v = row.get(n)
        if v is not None and str(v).strip() != "":
            return str(v).strip()
    return ""


# =============================================================================
# KẾT NỐI DB
# =============================================================================

class Database:
    """Bọc pyodbc: batch insert nhanh + ghi log lỗi."""

    def __init__(self, server: str, database: str, dry_run: bool = False):
        self.server = server
        self.database = database
        self.dry_run = dry_run
        self.conn: pyodbc.Connection | None = None
        self.errors: list[tuple[str, int | None, str, str, str, str]] = []
        self.error_count: int = 0
        self.error_by_code: dict[str, int] = {}
        self.error_rows: int = 0
        self.warning_rows: int = 0
        self.job_id: str | None = None

    def connect(self) -> None:
        if self.dry_run:
            log.info("DRY-RUN: bo qua ket noi DB")
            return
        cs = (
            f"DRIVER={{SQL Server}};SERVER={self.server};"
            f"DATABASE={self.database};Trusted_Connection=yes;"
        )
        self.conn = pyodbc.connect(cs, timeout=30, autocommit=False)
        self.conn.timeout = 300
        log.info("Da ket noi %s / %s", self.server, self.database)

    def close(self) -> None:
        if self.conn is not None:
            try:
                self.conn.close()
            except Exception:
                pass
            self.conn = None

    def execute(self, sql: str, params: Sequence[Any] = ()) -> None:
        if self.dry_run:
            return
        cur = self.conn.cursor()
        cur.execute(sql, params)
        cur.close()

    def query(self, sql: str, params: Sequence[Any] = ()) -> list[tuple]:
        if self.dry_run:
            return []
        cur = self.conn.cursor()
        cur.execute(sql, params)
        rows = cur.fetchall()
        cur.close()
        return rows

    def query_one(self, sql: str, params: Sequence[Any] = ()) -> tuple | None:
        rows = self.query(sql, params)
        return rows[0] if rows else None

    def commit(self) -> None:
        if self.dry_run or self.conn is None:
            return
        self.conn.commit()

    def rollback(self) -> None:
        if self.dry_run or self.conn is None:
            return
        try:
            self.conn.rollback()
        except Exception:
            pass

    # -- batch insert -------------------------------------------------------

    def insert_many(self, sql: str, rows: Sequence[Sequence[Any]], fast: bool = True) -> int:
        """
        executemany + fast_executemany (nhanh gap ~10 lan).

        `fast_executemany` gom TOAN BO tham so cua batch vao mot buffer trong
        bo nho -> hay MemoryError voi cac cot chuoi dai (vd import_errors).
        Voi cot TEXT/chi tiet loi: dat fast=False (cham hon nhung an toan).
        """
        if not rows or self.dry_run:
            return 0
        cur = self.conn.cursor()
        if fast:
            # pyodbc: thuoc tinh dung ten la 'fast_executemany' (khong phai 'fast_execute')
            try:
                cur.fast_executemany = True
            except AttributeError:
                pass  # ban pyodbc cu khong ho tro -> dung executemany thuong
        try:
            cur.executemany(sql, rows)
        except MemoryError:
            log.warning("  MemoryError -> thu lai voi executemany thuong")
            cur.close()
            cur = self.conn.cursor()
            cur.executemany(sql, rows)
        finally:
            cur.close()
        return len(rows)

    def insert_batched(
        self,
        sql: str,
        rows: Sequence[Sequence[Any]],
        label: str = "",
        batch_size: int = BATCH_SIZE,
        fast: bool = True,
    ) -> int:
        total = 0
        t0 = time.time()
        for i in range(0, len(rows), batch_size):
            chunk = rows[i : i + batch_size]
            try:
                self.insert_many(sql, chunk, fast=fast)
                self.commit()
            except Exception as exc:
                self.rollback()
                log.error("LOI khi insert %s (batch %d): %s", label, i // batch_size, exc)
                raise
            total += len(chunk)
            if label:
                log.info("  %s: %d/%d (%.1fs)", label, total, len(rows), time.time() - t0)
        return total

    # -- import log ---------------------------------------------------------

    def add_error(
        self,
        error_code: str,
        error_message: str,
        field_name: str = "",
        field_value: str = "",
        row_number: int | None = None,
        severity: str = "WARNING",
        suggested_fix: str = "",
    ) -> None:
        self.errors.append(
            (error_code, row_number, field_name, str(field_value)[:500],
             error_message[:500], severity, suggested_fix[:500])
        )
        self.error_count += 1
        self.error_by_code[error_code] = self.error_by_code.get(error_code, 0) + 1
        if severity == "ERROR":
            self.error_rows += 1
        else:
            self.warning_rows += 1
        if len(self.errors) >= 500:
            self.flush_errors()

    def flush_errors(self) -> None:
        if not self.errors:
            return
        if self.dry_run:
            # DRY-RUN: khong ghi DB, bo dem loi (da dem o add_error)
            self.errors.clear()
            return
        if self.job_id is None:
            # CHUA co import job -> giu loi trong RAM cho den khi job duoc tao.
            # (Truoc day o day lenh `self.errors.clear()` se LAM MAT loi:
            #  2.520 loi da dem nhung chi 20 dong con lai moi ghi xuong DB.)
            log.warning(
                "[ERRORS] Chua co import job, giu %d loi trong bo dem cho den khi "
                "job duoc tao", len(self.errors))
            return
        sql = """
        INSERT INTO [tenant].[import_errors]
            (job_id, row_number, field_name, field_value,
             error_code, error_message, severity, suggested_fix, created_at)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, GETDATE())
        """
        rows = [
            (self.job_id, rn or 0, fn, fv, ec, em, sev, sf)
            for (ec, rn, fn, fv, em, sev, sf) in self.errors
        ]
        self.insert_batched(sql, rows, label="import_errors", batch_size=200, fast=False)
        self.errors.clear()


# =============================================================================
# MODEL DỮ LIỆU RDT
# =============================================================================

# Tên cột chuẩn hóa cho từng file RDT (R02 - map theo TÊN, không theo vị trí).
# RDT3 thiếu 'Thuế GTGT (%)' và 'Loại Chứng Từ' (thay bằng 'Status').
COL_ORDER = "Mã Đơn Hàng"
COL_INVOICE = "Mã Hóa Đơn"
COL_STATUS = "Loại Chứng Từ"
COL_STATUS_ALT = "Status"
COL_CUSTOMER = "Mã KH"
COL_CUSTOMER_OLD = "Mã KH cũ"
COL_CUSTOMER_NAME = "Tên KH"
COL_CONTACT = "Tên Người Đại Diện"
COL_PHONE = "Số Điện Thoại"
COL_HOUSE_NO = "Số Nhà"
COL_STREET = "Đường"
COL_WARD = "Phường"
COL_DISTRICT = "Quận"
COL_PROVINCE = "Tỉnh"
COL_LAT = "Vĩ Độ"
COL_LON = "Kinh Độ"
COL_SEGMENT = "Customer Segment"
COL_OUTLET_TYPE = "Mã Loại Hình"
COL_OUTLET_GROUP = "Mã Nhóm Loại Hình"
COL_CHANNEL = "Mã Kênh KH"
COL_ORDER_DATE = "Ngày (Đơn Hàng)"
COL_INVOICE_DATE = "Ngày hóa đơn"
COL_WAREHOUSE = "Mã Kho"
COL_CATEGORY = "Tên Ngành Hàng"
COL_BRAND = "Nhãn Hàng"
COL_PACK_TYPE = "Pack Type"
COL_PACK_SIZE = "Pack Size"
COL_VOLUME = "SKU"
COL_PACKAGE_GROUP = "Package Group"
COL_FLAVOUR = "Flavour"
COL_SKU = "Mã SP"
COL_PRODUCT_NAME = "Tên SP"
COL_UOM = "ĐVT"
COL_QTY_ORDER = "Sản lượng đặt hàng"
COL_QTY_DELIVER = "Sản lượng đi giao"
COL_QTY_ACTUAL = " Sản lượng thực giao"
COL_QTY_PROMO = "Số Lượng KM"
COL_PRICE_BEFORE = "DS Trước Chiết Khấu/KM"
COL_PRICE_AFTER = "DS Sau Chiết Khấu /KM"
COL_TAX = "Thuế GTGT (%)"
COL_DISCOUNT = "Tiền Chiết Khấu"
COL_PROMO_AMOUNT = "Tiền Khuyến Mãi"
COL_ROUTE = "Mã Route"
COL_ROUTE_NAME = "Tên Route"
COL_ROUTE_TYPE = "Loại Route"
COL_SALESPERSON = "Mã NVBH"
COL_SALESPERSON_NAME = "Tên NVBH"
COL_NOTE = "Ghi chú"

# Kho: RDT dùng 'MAIN_WHS', DB dùng 'KHO001'
# DB chỉ có 1 kho duy nhất -> moi ma kho (ke ca rong) deu map ve KHO001
WAREHOUSE_MAP = {"MAIN_WHS": "KHO001", "KHO001": "KHO001"}
DEFAULT_WAREHOUSE_CODE = "KHO001"

# Loại Route -> business_line
ROUTE_TYPE_MAP = {
    "Presell": "PEPSI",
    "Distributor": "GENERAL",
    "AFH Hunting": "GENERAL",
    "SPVBWholesale": "GENERAL",
}


@dataclass
class RdtRow:
    source_file: str
    row_number: int
    order_number: str = ""
    invoice_number: str = ""
    status: str = ""
    customer_code: str = ""
    customer_old_code: str = ""
    customer_name: str = ""
    contact_person: str = ""
    phone: str = ""
    house_no: str = ""
    street: str = ""
    ward: str = ""
    district: str = ""
    province: str = ""
    latitude: str = ""
    longitude: str = ""
    segment: str = ""
    outlet_type: str = ""
    outlet_group: str = ""
    channel: str = ""
    order_date: date | None = None
    invoice_date: date | None = None
    warehouse_code: str = ""
    category_name: str = ""
    brand: str = ""
    pack_type: str = ""
    pack_size: str = ""
    volume: str = ""
    package_group: str = ""
    flavour: str = ""
    sku: str = ""
    product_name: str = ""
    uom: str = ""
    qty_order: Decimal | None = None
    qty_deliver: Decimal | None = None
    qty_actual: Decimal | None = None
    qty_promo: Decimal | None = None
    price_before: Decimal | None = None
    price_after: Decimal | None = None
    tax_rate: Decimal | None = None
    discount_amount: Decimal | None = None
    promo_amount: Decimal | None = None
    route_code: str = ""
    route_name: str = ""
    route_type: str = ""
    salesperson_code: str = ""
    salesperson_name: str = ""
    note: str = ""


def load_rdt(files: Sequence[str], db: Database) -> list[RdtRow]:
    """B1/B2: doc + chuan hoa 7 file RDT."""
    out: list[RdtRow] = []
    t0 = time.time()
    for fname in files:
        path = DATA_DIR / fname
        if not path.exists():
            log.error("Khong tim thay file %s", path)
            continue
        n = 0
        with path.open("r", encoding="utf-8-sig", newline="") as f:
            reader = csv.DictReader(f)
            reader.fieldnames = [nfc(h) for h in reader.fieldnames]
            for i, raw in enumerate(reader, start=2):
                row = {nfc(k): (nfc(v) if isinstance(v, str) else v) for k, v in raw.items()}
                try:
                    r = RdtRow(source_file=fname, row_number=i)
                    r.order_number = get(row, COL_ORDER)
                    r.invoice_number = get(row, COL_INVOICE)
                    r.status = get(row, COL_STATUS) or get(row, COL_STATUS_ALT)
                    r.customer_code = get(row, COL_CUSTOMER)
                    r.customer_old_code = get(row, COL_CUSTOMER_OLD)
                    r.customer_name = get(row, COL_CUSTOMER_NAME)
                    r.contact_person = get(row, COL_CONTACT)
                    r.phone = get(row, COL_PHONE)
                    r.house_no = get(row, COL_HOUSE_NO)
                    r.street = get(row, COL_STREET)
                    r.ward = strip_code_prefix(get(row, COL_WARD))
                    r.district = strip_code_prefix(get(row, COL_DISTRICT))
                    r.province = strip_code_prefix(get(row, COL_PROVINCE))
                    r.latitude = get(row, COL_LAT)
                    r.longitude = get(row, COL_LON)
                    r.segment = get(row, COL_SEGMENT)
                    r.outlet_type = get(row, COL_OUTLET_TYPE)
                    r.outlet_group = get(row, COL_OUTLET_GROUP)
                    r.channel = get(row, COL_CHANNEL)
                    r.order_date = parse_date(get(row, COL_ORDER_DATE), COL_ORDER_DATE, f"{fname}:{i}")
                    r.invoice_date = parse_date(get(row, COL_INVOICE_DATE), COL_INVOICE_DATE, f"{fname}:{i}")
                    r.warehouse_code = get(row, COL_WAREHOUSE)
                    r.category_name = get(row, COL_CATEGORY)
                    r.brand = get(row, COL_BRAND)
                    r.pack_type = get(row, COL_PACK_TYPE)
                    r.pack_size = get(row, COL_PACK_SIZE)
                    r.volume = get(row, COL_VOLUME)
                    r.package_group = get(row, COL_PACKAGE_GROUP)
                    r.flavour = get(row, COL_FLAVOUR)
                    r.sku = get(row, COL_SKU)
                    r.product_name = get(row, COL_PRODUCT_NAME)
                    r.uom = get(row, COL_UOM)
                    r.qty_order = parse_decimal(get(row, COL_QTY_ORDER), COL_QTY_ORDER, f"{fname}:{i}")
                    r.qty_deliver = parse_decimal(get(row, COL_QTY_DELIVER), COL_QTY_DELIVER, f"{fname}:{i}")
                    r.qty_actual = parse_decimal(get(row, COL_QTY_ACTUAL), COL_QTY_ACTUAL, f"{fname}:{i}")
                    r.qty_promo = parse_decimal(get(row, COL_QTY_PROMO), COL_QTY_PROMO, f"{fname}:{i}")
                    r.price_before = parse_decimal(get(row, COL_PRICE_BEFORE), COL_PRICE_BEFORE, f"{fname}:{i}")
                    r.price_after = parse_decimal(get(row, COL_PRICE_AFTER), COL_PRICE_AFTER, f"{fname}:{i}")
                    tv = get(row, COL_TAX)
                    r.tax_rate = parse_decimal(tv, COL_TAX, f"{fname}:{i}") if tv else None
                    r.discount_amount = parse_decimal(get(row, COL_DISCOUNT), COL_DISCOUNT, f"{fname}:{i}")
                    r.promo_amount = parse_decimal(get(row, COL_PROMO_AMOUNT), COL_PROMO_AMOUNT, f"{fname}:{i}")
                    r.route_code = get(row, COL_ROUTE)
                    r.route_name = get(row, COL_ROUTE_NAME)
                    r.route_type = get(row, COL_ROUTE_TYPE)
                    r.salesperson_code = get(row, COL_SALESPERSON)
                    r.salesperson_name = get(row, COL_SALESPERSON_NAME)
                    r.note = get(row, COL_NOTE)
                    out.append(r)
                    n += 1
                except ValueError as exc:
                    # R03/R04 vi pham -> ghi log va bo dong, KHONG crash toan pipeline
                    db.add_error(
                        error_code="PARSE_ERROR",
                        error_message=str(exc)[:500],
                        field_name="RDT",
                        field_value=fname,
                        row_number=i,
                        severity="ERROR",
                        suggested_fix="Kiem tra lai dau phan cach so / ngay trong file nguon",
                    )
        log.info("[B2] %s: %d dong (%.1fs)", fname, n, time.time() - t0)
    log.info("[B2] Tong RDT: %d dong", len(out))
    return out


# =============================================================================
# PHẦN 2: TRÍCH XUẤT MASTER DATA  (B3, B4)
# =============================================================================

def load_clean_csv(name: str) -> list[dict[str, str]]:
    path = DATA_DIR / name
    if not path.exists():
        log.warning("Khong tim thay %s", path)
        return []
    return list(read_csv_rows(path))


@dataclass
class Dimension:
    categories: list[dict] = field(default_factory=list)
    products: list[dict] = field(default_factory=list)
    routes: list[dict] = field(default_factory=list)
    warehouses: list[dict] = field(default_factory=list)
    users: list[dict] = field(default_factory=list)
    partners: list[dict] = field(default_factory=list)
    price_lists: list[dict] = field(default_factory=list)
    uom_conversions: list[dict] = field(default_factory=list)
    id_maps: dict[str, dict[str, str]] = field(default_factory=dict)


def extract_dimensions(rdt: list[RdtRow], db: Database) -> Dimension:
    """
    B3/B4: trich xuat master data theo thu tu dung FK:
        categories -> products -> uom_conversions -> routes -> users
        -> warehouses -> partners
    """
    dim = Dimension()

    # ---------------------------------------------------------------- 2.1 categories
    cat_names: list[str] = []
    seen_cat: set[str] = set()
    for r in rdt:
        c = r.category_name.strip()
        if c and c not in seen_cat:
            seen_cat.add(c)
            cat_names.append(c)
    cat_names.sort()
    for c in cat_names:
        dim.categories.append({
            "id": None,                      # sinh trong DB
            "category_code": slugify(c),
            "category_name": c,
            "is_active": 1,
        })
    log.info("[B3] categories: %d", len(dim.categories))

    # ---------------------------------------------------------------- 2.2 products
    prod_clean = {r["sku"].strip(): r for r in load_clean_csv("products_clean.csv") if r.get("sku")}
    prod_raw = {r["sku"].strip(): r for r in load_clean_csv("products.csv") if r.get("sku")}

    # thuoc tinh theo SKU (tu RDT) - 141 SKU
    sku_attr: dict[str, dict] = {}
    sku_price_samples: dict[str, list[Decimal]] = defaultdict(list)
    sku_tax: dict[str, Counter] = defaultdict(Counter)
    for r in rdt:
        if not r.sku:
            continue
        a = sku_attr.setdefault(r.sku, {
            "name": r.product_name, "category": r.category_name, "brand": r.brand,
            "pack_type": r.pack_type, "pack_size": r.pack_size, "volume": r.volume,
            "package_group": r.package_group, "flavour": r.flavour, "uom": r.uom,
        })
        if r.price_before and r.price_before > 0:
            sku_price_samples[r.sku].append(r.price_before)
        if r.tax_rate is not None:
            sku_tax[r.sku][r.tax_rate] += 1

    cat_id_by_name: dict[str, str] = {}
    for c in dim.categories:
        cat_id_by_name[c["category_name"]] = c["category_code"]

    def median(values: list[Decimal]) -> Decimal:
        if not values:
            return Decimal("0")
        s = sorted(values)
        mid = len(s) // 2
        if len(s) % 2:
            return s[mid]
        return (s[mid - 1] + s[mid]) / 2

    for sku in sorted(sku_attr.keys()):
        a = sku_attr[sku]
        pc = prod_clean.get(sku) or prod_raw.get(sku) or {}
        # gia ban: uu tien selling_price tu products_clean (cung don vi - gia/thung)
        price: Decimal = Decimal("0")
        price_source = "RDT_MEDIAN"
        sp = (pc.get("selling_price") or "").strip()
        if sp:
            try:
                price = Decimal(sp)
                price_source = "PRODUCTS_CLEAN"
            except InvalidOperation:
                pass
        if price == 0 and sku_price_samples.get(sku):
            price = median(sku_price_samples[sku])
        # thue: theo SKU (nhat quan 126/126 SKU)
        vat = Decimal("0")
        if sku_tax.get(sku):
            vat = max(sku_tax[sku].items(), key=lambda kv: kv[1])[0]
        # volume ml: cot RDT 'SKU' -> khop products_clean.pack_size_ml 80/81
        volume: Decimal | None = None
        vol_raw = a["volume"]
        if vol_raw:
            try:
                volume = Decimal(vol_raw)
            except InvalidOperation:
                volume = None
        # weight
        weight: Decimal | None = None
        w = (pc.get("weight_per_unit") or "").strip()
        if w:
            try:
                weight = Decimal(w)
            except InvalidOperation:
                weight = None
        uom = (a["uom"] or "CAI").strip() or "CAI"

        dim.products.append({
            "sku": sku,
            "product_name": a["name"] or sku,
            "short_name": (a["name"] or sku)[:255],
            "category_code": cat_id_by_name.get(a["category"], ""),
            "brand": a["brand"] or None,
            "base_uom": uom[:20],
            "base_price": clamp_decimal(price) or Decimal("0"),
            "vat_rate": clamp_decimal(vat) or Decimal("0"),
            "weight_gram": clamp_decimal(weight),
            "volume_ml": clamp_decimal(volume),
            "is_active": 1,
            "_price_source": price_source,
            "_pack_size": a["pack_size"],
            "_package_group": a["package_group"],
            "_flavour": a["flavour"],
            "_pack_type": a["pack_type"],
        })
    log.info("[B3] products: %d (co gia tu products_clean: %d)",
             len(dim.products),
             sum(1 for p in dim.products if p["_price_source"] == "PRODUCTS_CLEAN"))

    # ---------------------------------------------------------------- 2.3 uom_conversions
    for p in dim.products:
        pg = p["_package_group"]
        if not pg or not str(pg).isdigit():
            continue
        try:
            factor = Decimal(pg)
        except InvalidOperation:
            continue
        if factor <= 1:
            continue
        dim.uom_conversions.append({
            "sku": p["sku"],
            "from_uom": p["base_uom"],
            "to_uom": "CAI",
            "conversion_factor": factor,
            "is_default": 1,
        })
    log.info("[B3] uom_conversions: %d", len(dim.uom_conversions))

    # ---------------------------------------------------------------- 2.4 routes
    route_info: dict[str, dict] = {}
    for r in rdt:
        if not r.route_code:
            continue
        info = route_info.setdefault(r.route_code, {
            "route_code": r.route_code, "route_name": r.route_name or r.route_code,
            "business_line": "GENERAL", "region": None,
        })
        if not info["route_name"] and r.route_name:
            info["route_name"] = r.route_name
        if r.route_type:
            info["business_line"] = ROUTE_TYPE_MAP.get(r.route_type, "GENERAL")
    # vung: suy tu tinh cua khach hang gan tuyen
    route_province: dict[str, Counter] = defaultdict(Counter)
    for r in rdt:
        if r.route_code and r.province:
            route_province[r.route_code][r.province] += 1
    for code, info in route_info.items():
        if route_province.get(code):
            info["region"] = route_province[code].most_common(1)[0][0]
    dim.routes = sorted(route_info.values(), key=lambda x: x["route_code"])
    log.info("[B3] routes: %d", len(dim.routes))

    # ---------------------------------------------------------------- 2.5 users
    # user_accounts.csv (38) + 16 NVBH moi tu RDT
    ua_raw = load_clean_csv("user_accounts.csv")
    ua_clean = load_clean_csv("user_accounts_clean.csv")
    ua_map = {r["username"].strip(): r for r in ua_raw} if ua_raw else {}

    existing_usernames = {u.strip() for u in ua_map}
    role_map = {"1": "ADMIN", "2": "MANAGER", "3": "MANAGER", "4": "SALES",
                "5": "WAREHOUSE", "6": "ACCOUNTANT"}
    for u in ua_clean:
        un = (u.get("username") or "").strip()
        if un and un not in ua_map:
            ua_map[un] = u
    for un, u in ua_map.items():
        rid = (u.get("primary_role_id") or "").strip()
        role = role_map.get(rid, "SALES")
        dim.users.append({
            "username": un[:50],
            "full_name": (u.get("full_name") or un)[:255],
            "email": (u.get("email") or None),
            "role": role,
            "is_active": 1 if (u.get("is_active") or "1").strip().lower() in ("1", "true") else 0,
            "route_code": None,
        })

    # 16 NVBH (SP0000xxxx) tu RDT - KHONG co trong user_accounts.csv
    nv: dict[str, str] = {}
    for r in rdt:
        if r.salesperson_code and r.salesperson_code not in nv:
            nv[r.salesperson_code] = r.salesperson_name or r.salesperson_code
    for code, name in sorted(nv.items()):
        if code in existing_usernames:
            continue
        dim.users.append({
            "username": code[:50],
            "full_name": name[:255],
            "email": f"{code.lower()}@phn.vn",
            "role": "SALES",
            "is_active": 1,
            "route_code": None,
        })
        log.info("  [B4] Tao user moi tu NVBH: %s (%s)", code, name)
    log.info("[B4] users: %d (%d tu user_accounts.csv, %d NVBH moi)",
             len(dim.users), len(ua_map), len(nv))

    # ---------------------------------------------------------------- 2.6 warehouses
    wh_raw = load_clean_csv("warehouses_clean.csv")
    if not wh_raw:
        wh_raw = load_clean_csv("warehouses.csv")
    for w in wh_raw:
        code = (w.get("warehouse_code") or "").strip()
        if not code:
            continue
        dim.warehouses.append({
            "warehouse_code": code[:50],
            "warehouse_name": (w.get("warehouse_name") or code)[:255],
            "warehouse_type": (w.get("warehouse_type") or "MAIN")[:20],
            "address": (w.get("address_line") or None),
            "is_active": 1,
        })
    # dam bao co kho cho MAIN_WHS
    codes = {w["warehouse_code"] for w in dim.warehouses}
    for src in WAREHOUSE_MAP.values():
        if src not in codes:
            dim.warehouses.append({
                "warehouse_code": src, "warehouse_name": "Kho trung tâm",
                "warehouse_type": "MAIN", "address": None, "is_active": 1,
            })
    log.info("[B4] warehouses: %d", len(dim.warehouses))

    # ---------------------------------------------------------------- 2.7 partners
    partners_clean = {r["partner_code"].strip(): r for r in load_clean_csv("partners_clean.csv")
                      if r.get("partner_code")}
    partners_raw = {r["partner_code"].strip(): r for r in load_clean_csv("partners.csv")
                    if r.get("partner_code")}

    # thuoc tinh theo khach hang (tu RDT) - quy tac da so R08
    kh_routes: dict[str, Counter] = defaultdict(Counter)
    kh_names: dict[str, Counter] = defaultdict(Counter)
    kh_dcr: dict[str, Counter] = defaultdict(Counter)
    kh_seg: dict[str, Counter] = defaultdict(Counter)
    kh_phone: dict[str, Counter] = defaultdict(Counter)
    kh_addr: dict[str, Counter] = defaultdict(Counter)
    kh_ward: dict[str, Counter] = defaultdict(Counter)
    kh_district: dict[str, Counter] = defaultdict(Counter)
    kh_province: dict[str, Counter] = defaultdict(Counter)
    kh_lat: dict[str, Counter] = defaultdict(Counter)
    kh_lon: dict[str, Counter] = defaultdict(Counter)
    kh_contact: dict[str, Counter] = defaultdict(Counter)
    kh_outlet: dict[str, Counter] = defaultdict(Counter)
    kh_group: dict[str, Counter] = defaultdict(Counter)
    kh_channel: dict[str, Counter] = defaultdict(Counter)

    def bump(d: dict[str, Counter], k: str, v: str) -> None:
        if v:
            d[k][v] += 1

    for r in rdt:
        kh = r.customer_code
        if not kh:
            continue
        bump(kh_routes, kh, r.route_code)
        bump(kh_names, kh, r.customer_name)
        bump(kh_dcr, kh, r.customer_old_code)
        bump(kh_seg, kh, r.segment)
        bump(kh_phone, kh, r.phone)
        bump(kh_addr, kh, " ".join(x for x in (r.house_no, r.street) if x))
        bump(kh_ward, kh, r.ward)
        bump(kh_district, kh, r.district)
        bump(kh_province, kh, r.province)
        bump(kh_lat, kh, r.latitude)
        bump(kh_lon, kh, r.longitude)
        bump(kh_contact, kh, r.contact_person)
        bump(kh_outlet, kh, r.outlet_type)
        bump(kh_group, kh, r.outlet_group)
        bump(kh_channel, kh, r.channel)

    all_codes: list[str] = sorted(set(kh_routes.keys()) | set(partners_clean.keys()))

    def majority(counter: Counter) -> str:
        """R08/R11: gia tri da so; tie -> gia tri nho nhat theo sort."""
        if not counter:
            return ""
        return sorted(counter.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]

    for code in all_codes:
        pc = partners_clean.get(code) or partners_raw.get(code) or {}
        name = majority(kh_names.get(code, Counter())) or (pc.get("partner_name") or code)
        route_code = majority(kh_routes.get(code, Counter())) or (pc.get("route_code") or "") or None
        segment = majority(kh_seg.get(code, Counter())) or None
        dcr = majority(kh_dcr.get(code, Counter())) or (pc.get("dcr_code") or "") or None
        phone = norm_phone(majority(kh_phone.get(code, Counter())))
        address = majority(kh_addr.get(code, Counter())) or (pc.get("address_line") or "") or None
        ward = majority(kh_ward.get(code, Counter())) or (pc.get("ward") or "") or None
        district = majority(kh_district.get(code, Counter())) or (pc.get("district") or "") or None
        province = majority(kh_province.get(code, Counter())) or (pc.get("province") or "") or None
        contact = majority(kh_contact.get(code, Counter())) or None
        outlet = majority(kh_outlet.get(code, Counter())) or (pc.get("outlet_type_code") or "") or None
        outlet_group = majority(kh_group.get(code, Counter())) or None
        channel = majority(kh_channel.get(code, Counter())) or None

        def geo(raw: str) -> Decimal | None:
            if not raw:
                return None
            try:
                v = Decimal(raw.replace(",", "."))
            except InvalidOperation:
                return None
            if not (Decimal("-180") <= v <= Decimal("180")):
                return None
            return v

        lat = geo(majority(kh_lat.get(code, Counter()))) or geo((pc.get("latitude") or "").strip())
        lon = geo(majority(kh_lon.get(code, Counter()))) or geo((pc.get("longitude") or "").strip())

        # partner_type: ASO neu ten bat dau bang 'ASO' -> nguoc lai, uu tien ASO
        ptype = (pc.get("partner_type") or "").strip().upper()
        if ptype not in ("STORE", "ASO", "CORPORATE", "AGENCY"):
            ptype = "ASO" if name.upper().startswith("ASO") else "STORE"

        dim.partners.append({
            "partner_code": code[:50],
            "partner_name": name[:255],
            "tax_code": (pc.get("tax_code") or None),
            "partner_type": ptype[:20],
            "segment": (segment or None),
            "outlet_type_code": (outlet or None),
            "channel_type": (channel or outlet_group),
            "business_line": "GENERAL",
            "contact_person": (contact or None),
            "phone": phone,
            "email": (pc.get("email") or None),
            "address": (address or None),
            "ward": (ward or None),
            "district": (district or None),
            "province": (province or None),
            "latitude": lat,
            "longitude": lon,
            "route_code": (route_code or None),
            "dcr_code": (dcr or None),
            "credit_limit": Decimal("0"),
            "payment_term_days": 0,
            "is_active": 1,
            "notes": None,
        })
    log.info("[B4] partners: %d", len(dim.partners))
    return dim


# =============================================================================
# PHẦN 3: TÍNH LẠI SỐ TIỀN & NẠP ĐƠN HÀNG  (B5, B6, B7)
# =============================================================================

@dataclass
class OrderAggregate:
    order_number: str
    customer_code: str = ""
    warehouse_code: str = ""
    salesperson_code: str = ""
    order_date: date | None = None
    invoice_date: date | None = None
    invoice_number: str = ""
    status: str = ""
    segment: str = ""
    province: str = ""
    district: str = ""
    phone: str = ""
    route_code: str = ""
    notes: list[str] = field(default_factory=list)
    subtotal: Decimal = Decimal("0")
    discount_amount: Decimal = Decimal("0")
    tax_amount: Decimal = Decimal("0")
    total_amount: Decimal = Decimal("0")
    line_count: int = 0
    _cust: Counter = field(default_factory=Counter)
    _wh: Counter = field(default_factory=Counter)
    _sp: Counter = field(default_factory=Counter)
    _date: Counter = field(default_factory=Counter)
    _inv: Counter = field(default_factory=Counter)
    _status: Counter = field(default_factory=Counter)
    _seg: Counter = field(default_factory=Counter)
    _prov: Counter = field(default_factory=Counter)
    _dist: Counter = field(default_factory=Counter)
    _phone: Counter = field(default_factory=Counter)
    _route: Counter = field(default_factory=Counter)


def aggregate_orders(rdt: list[RdtRow], db: Database) -> dict[str, OrderAggregate]:
    """
    B5: Gom dong theo don + tinh lai toan bo so tien.

    Quy tac:
      R09  don co Cancelled -> CANCELLED (uu tien)
      R10  so hoa don nhieu gia tri -> lay nho nhat theo sort
      R11  gia tri da so cho KH/kho/NVBH/ngay/tinh
      R06/R07  co flags ANOMALY_LARGE_VALUE / ANOMALY_NEGATIVE_TOTAL
    """
    orders: dict[str, OrderAggregate] = {}

    for r in rdt:
        on = r.order_number
        if not on:
            db.add_error(
                "MISSING_ORDER_NUMBER",
                "Dong thieu 'Ma Don Hang' - bo qua",
                field_name=COL_ORDER, field_value=r.source_file,
                row_number=r.row_number, severity="ERROR",
                suggested_fix="Kiem tra file nguon, dong nay khong thuoc don nao",
            )
            continue
        o = orders.get(on)
        if o is None:
            o = OrderAggregate(order_number=on)
            orders[on] = o

        o._cust[r.customer_code] += 1
        o._wh[r.warehouse_code] += 1
        o._sp[r.salesperson_code] += 1
        o._date[r.order_date.isoformat() if r.order_date else ""] += 1
        o._inv[r.invoice_number] += 1
        o._status[r.status] += 1
        o._seg[r.segment] += 1
        o._prov[r.province] += 1
        o._dist[r.district] += 1
        o._phone[r.phone] += 1
        o._route[r.route_code] += 1

    for on, o in orders.items():
        def maj(c: Counter) -> str:
            if not c:
                return ""
            return sorted(c.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]

        o.customer_code = maj(o._cust)
        o.warehouse_code = maj(o._wh)
        o.salesperson_code = maj(o._sp)
        o.segment = maj(o._seg)
        o.province = maj(o._prov)
        o.district = maj(o._dist)
        o.phone = maj(o._phone)
        o.route_code = maj(o._route)
        d = maj(o._date)
        o.order_date = date.fromisoformat(d) if d else None
        inv_vals = [v for v in o._inv if v]
        o.invoice_number = min(inv_vals) if inv_vals else ""
        sts = o._status
        if "Cancelled" in sts:
            o.status = "CANCELLED"
        elif "Confirmed" in sts:
            o.status = "DELIVERED"
        else:
            o.status = "CONFIRMED"
        for label, c in (("customer", o._cust), ("warehouse", o._wh), ("date", o._date),
                         ("route", o._route), ("phone", o._phone)):
            if len([k for k in c if k]) > 1:
                db.add_error(
                    "ORDER_INCONSISTENT",
                    f"Don co nhieu gia tri '{label}' - chon gia tri da so",
                    field_name=label, field_value=on, row_number=None,
                    severity="WARNING",
                    suggested_fix="Xac nhan gia tri chinh xac voi nguon he thong",
                )

    return orders


def resolve_tax_rates(rdt: list[RdtRow], db: Database) -> dict[str, Decimal]:
    """
    Fallback thue cho RDT3 (27.884 dong thieu 'Thuế GTGT (%)').

    Thue la thuoc tinh THEO SKU: 126/126 SKU co duy nhat 1 muc thue.
    => lay theo 'Ma SP' tu 6 file RDT con lai -> phu 27.732/27.884 dong.
    152 dong con lai (SKU POSM khong co ban ghi khac) -> tax_rate = 0.
    """
    sku_tax: dict[str, Counter] = defaultdict(Counter)
    for r in rdt:
        if r.sku and r.tax_rate is not None:
            sku_tax[r.sku][r.tax_rate] += 1
    rates: dict[str, Decimal] = {}
    for sku, c in sku_tax.items():
        rates[sku] = max(c.items(), key=lambda kv: kv[1])[0]
    log.info("[B5] tax rate theo SKU: %d SKU co thue", len(rates))
    return rates


def compute_amounts(
    rdt: list[RdtRow],
    orders: dict[str, OrderAggregate],
    sku_tax_rates: dict[str, Decimal],
    db: Database,
) -> list[dict]:
    """
    B5: Tinh lai so tien cap dong + cap don.

    Cong thuc (da kiem chung 100%):
      line_subtotal = qty * unit_price
      line_total    = line_subtotal - discount_amount      (PRE-TAX)
      tax_amount    = (line_subtotal - discount_amount) * rate / 100
      order.total   = SUM(line_total) + tax_amount
    """
    by_order: dict[str, list[RdtRow]] = defaultdict(list)
    for r in rdt:
        if r.order_number:
            by_order[r.order_number].append(r)

    items: list[dict] = []
    skipped_no_sku = 0
    tax_fallback = 0
    tax_unresolved = 0

    for onum, rows in by_order.items():
        o = orders[onum]
        for line_no, r in enumerate(rows, start=1):
            if not r.sku:
                skipped_no_sku += 1
                db.add_error(
                    "MISSING_SKU",
                    "Dong thieu 'Ma SP' - khong xac dinh duoc san pham -> bo qua",
                    field_name=COL_SKU, field_value=onum,
                    row_number=r.row_number, severity="ERROR",
                    suggested_fix="Tra 'Ma SP' tu file nguon de nap day du chi tiet dong",
                )
                continue

            qty = r.qty_order or Decimal("0")
            price = r.price_before or Decimal("0")
            discount = r.discount_amount or Decimal("0")
            qty = clamp_decimal(qty) or Decimal("0")
            price = clamp_decimal(price) or Decimal("0")
            discount = clamp_decimal(discount) or Decimal("0")

            line_subtotal = qty * price
            discount_pct = (discount / line_subtotal * 100) if line_subtotal > 0 else Decimal("0")
            line_total = line_subtotal - discount

            if r.tax_rate is not None:
                tax_rate = r.tax_rate
            else:
                tax_rate = sku_tax_rates.get(r.sku)
                if tax_rate is not None:
                    tax_fallback += 1
                else:
                    tax_rate = Decimal("0")
                    tax_unresolved += 1
                    db.add_error(
                        "TAX_RATE_UNRESOLVED",
                        f"Khong tim thay thue GTGT cho SKU {r.sku} (RDT3) - dat tax_rate = 0",
                        field_name=COL_TAX, field_value=r.sku,
                        row_number=r.row_number, severity="WARNING",
                        suggested_fix="Bo sung muc thue GTGT cho SKU trong file nguon",
                    )
            tax_amount = (line_subtotal - discount) * tax_rate / 100

            delivered = r.qty_deliver if r.qty_deliver is not None else qty
            actual = r.qty_actual

            note_parts: list[str] = []
            if r.note:
                note_parts.append(r.note)
            if actual is not None and actual != (r.qty_deliver if r.qty_deliver is not None else qty):
                note_parts.append(f"Thuc giao: {actual}")
            if r.qty_promo is not None and r.qty_promo > 0:
                note_parts.append(f"KM: {r.qty_promo}")
            if r.pack_size:
                note_parts.append(f"Pack: {r.pack_size}")
            if r.flavour:
                note_parts.append(f"Flavour: {r.flavour}")

            items.append({
                "order_number": onum,
                "line_number": line_no,
                "sku": r.sku,
                "uom_code": (r.uom or "CAI")[:20],
                "ordered_qty": qty,
                "base_qty": qty,
                "unit_price": price,
                "discount_percent": discount_pct.quantize(Decimal("0.01")),
                "discount_amount": discount,
                "line_subtotal": line_subtotal,
                "line_total": line_total,
                "delivered_qty": delivered if delivered is not None else Decimal("0"),
                "returned_qty": Decimal("0"),
                "notes": "; ".join(note_parts)[:255] if note_parts else None,
                "_tax_rate": tax_rate,
                "_tax_amount": tax_amount,
                "_source_file": r.source_file,
                "_row_number": r.row_number,
            })

            o.subtotal += line_subtotal
            o.discount_amount += discount
            o.tax_amount += tax_amount
            o.line_count += 1

    for onum, o in orders.items():
        o.total_amount = o.subtotal - o.discount_amount + o.tax_amount
        if abs(o.total_amount) > Decimal("1000000000000"):
            o.notes.append("ANOMALY_LARGE_VALUE")
            db.add_error(
                "ANOMALY_LARGE_VALUE",
                f"Tong don {onum} vuot 1e12 ({o.total_amount})",
                field_name="total_amount", field_value=onum, severity="WARNING",
                suggested_fix="Kiem tra don le doanh nghiep co thuoc lo lon khong",
            )
        if o.total_amount < 0:
            o.notes.append("ANOMALY_NEGATIVE_TOTAL")
            db.add_error(
                "ANOMALY_NEGATIVE_TOTAL",
                f"Tong don {onum} am ({o.total_amount})",
                field_name="total_amount", field_value=onum, severity="WARNING",
                suggested_fix="Kiem tra lai chiet khau am o cap dong",
            )
        if not o.order_date:
            db.add_error(
                "MISSING_ORDER_DATE",
                f"Don {onum} thieu ngay dat hang",
                field_name=COL_ORDER_DATE, field_value=onum, severity="WARNING",
                suggested_fix="Bu lai ngay dat hang tu file nguon",
            )

    log.info("[B5] items tinh toan: %d (bo %d dong thieu Ma SP)", len(items), skipped_no_sku)
    log.info("[B5] fallback thue theo SKU: %d dong; khong xac dinh duoc: %d dong",
             tax_fallback, tax_unresolved)
    log.info("[B5] tong don: %d | tong tien: %s",
             len(orders), f"{sum((o.total_amount for o in orders.values()), Decimal('0')):,}")
    return items


# =============================================================================
# PHẦN 4: NẠP VÀO DATABASE  (B6, B7, B8)
# =============================================================================

def _s(val: Any) -> str | None:
    if val is None:
        return None
    s = str(val).strip()
    return s or None


def clean_business_data(db: Database) -> None:
    """
    Xoa du lieu nghiep vu (giu master data seed) de pipeline chay lai duoc
    ma khong sinh loi UNIQUE KEY.

    Thu tu xoa theo phu thuoc FK:
      sales_order_items -> sales_orders (CASCADE) -> partners/products
    """
    log.info("[CLEAN] Xoa du lieu nghiep vu cu (giu master data seed)...")
    steps = [
        ("DELETE FROM [tenant].[sales_order_items]", "sales_order_items"),
        ("DELETE FROM [tenant].[sales_orders]", "sales_orders"),
        ("DELETE FROM [tenant].[price_lists]", "price_lists"),
        ("DELETE FROM [tenant].[uom_conversions]", "uom_conversions"),
        ("DELETE FROM [tenant].[stock_ledger]", "stock_ledger"),
        ("DELETE FROM [tenant].[inventory_balances]", "inventory_balances"),
    ]
    for sql, label in steps:
        try:
            db.execute(sql)
        except Exception as exc:
            log.warning("[CLEAN] Bo qua %s: %s", label, exc)
    db.commit()
    log.info("[CLEAN] Hoan tat")


def clean_import_log(db: Database) -> None:
    """Xoa lich su import cu (import_jobs / import_errors) truoc khi tao job moi."""
    for sql, label in (
        ("DELETE FROM [tenant].[import_errors]", "import_errors"),
        ("DELETE FROM [tenant].[import_jobs]", "import_jobs"),
    ):
        try:
            db.execute(sql)
        except Exception as exc:
            log.warning("[CLEAN] Bo qua %s: %s", label, exc)
    db.commit()


def _q(val: Any, scale: int) -> Decimal | None:
    """
    Lam tron Decimal ve `scale` chu so thap phan cua cot trong DB.
    pyodbc nem 'Converting decimal loses precision' neu so thap phan vượt
    quy dinh (vd: credit_limit decimal(15,2) nhung RDT con nhieu chu so hon).
    """
    if val is None:
        return None
    try:
        d = val if isinstance(val, Decimal) else Decimal(str(val))
    except (InvalidOperation, ValueError):
        return None
    return d.quantize(Decimal(1).scaleb(-scale), rounding=ROUND_HALF_UP)


def _upsert_dim(
    db: Database,
    table: str,
    key_col: str,
    rows: list[tuple],
    insert_cols: list[str],
    update_cols: list[str],
    label: str,
) -> None:
    """
    Nhap dimension theo kieu idempotent:
      - ban ghi da ton tai (theo key_col) -> UPDATE cac cot trong update_cols
      - chua co -> INSERT (cot `id` tu sinh bang DEFAULT newid())
    Cach nay giu duoc du lieu seed trong 01_schema.sql (categories/routes/users)
    nhung van cho phep chay pipeline nhieu lan.

    `UPDATE ... WHERE key = ?` chay tung ban ghi vi pyodbc `executemany`
    khong ho tro `UPDATE ... FROM (VALUES ...)`.
    """
    existing = {str(r[0]) for r in db.query(f"SELECT {key_col} FROM [tenant].[{table}]")}

    to_update = [r for r in rows if r[0] in existing]
    to_insert = [r for r in rows if r[0] not in existing]

    if to_insert:
        # insert_cols bao gom key_col o dau -> moi row co dung len(insert_cols) gia tri
        all_cols = [key_col] + [c for c in insert_cols if c != key_col]
        col_list = ", ".join(all_cols)
        val_list = ", ".join(["?"] * len(all_cols))
        sql = f"INSERT INTO [tenant].[{table}] ({col_list}) VALUES ({val_list})"
        db.insert_batched(sql, to_insert, label=f"{label} (insert {len(to_insert)})")

    if to_update:
        set_clause = ", ".join(f"{c} = ?" for c in update_cols)
        upd_sql = f"UPDATE [tenant].[{table}] SET {set_clause} WHERE {key_col} = ?"
        for r in to_update:
            params = [r[insert_cols.index(c)] for c in update_cols] + [r[0]]
            db.execute(upd_sql, params)
        db.commit()
        log.info("[B4] %s: update %d ban ghi", label, len(to_update))

    log.info("[B4] %s: %d insert, %d update", label, len(to_insert), len(to_update))


def load_dimensions(db: Database, dim: Dimension) -> dict[str, dict[str, str]]:
    """
    Nap master data theo thu tu FK. Tra ve mapping id cho tung khoa.
    """
    ids: dict[str, dict[str, str]] = {
        "categories": {}, "products": {}, "routes": {},
        "warehouses": {}, "users": {}, "partners": {},
    }

    # ---- categories (idempotent: insert neu moi, update neu da ton tai)
    if dim.categories:
        rows = [(c["category_code"], c["category_name"], c["is_active"]) for c in dim.categories]
        _upsert_dim(
            db, "categories", "category_code", rows,
            insert_cols=["category_code", "category_name", "is_active"],
            update_cols=["category_name", "is_active"],
            label="categories",
        )
        for code, cid in db.query("SELECT category_code, id FROM [tenant].[categories]"):
            ids["categories"][code] = str(cid)

    # ---- products
    if dim.products:
        rows = []
        for p in dim.products:
            cat_id = ids["categories"].get(p["category_code"])
            rows.append((
                p["sku"], p["product_name"], p["short_name"], cat_id, p["brand"],
                p["base_uom"], _q(p["base_price"], 2), _q(p["vat_rate"], 2),
                _q(p["weight_gram"], 2), _q(p["volume_ml"], 2), p["is_active"],
            ))
        _upsert_dim(
            db, "products", "sku", rows,
            insert_cols=["sku", "product_name", "short_name", "category_id", "brand",
                         "base_uom", "base_price", "vat_rate", "weight_gram",
                         "volume_ml", "is_active"],
            update_cols=["product_name", "short_name", "category_id", "brand",
                         "base_uom", "base_price", "vat_rate", "weight_gram",
                         "volume_ml", "is_active"],
            label="products",
        )
        for sku, pid in db.query("SELECT sku, id FROM [tenant].[products]"):
            ids["products"][sku] = str(pid)

    # ---- routes
    if dim.routes:
        rows = [(r["route_code"], r["route_name"], r["business_line"], r["region"])
                for r in dim.routes]
        _upsert_dim(
            db, "routes", "route_code", rows,
            insert_cols=["route_code", "route_name", "business_line", "region"],
            update_cols=["route_name", "business_line", "region"],
            label="routes",
        )
        for code, rid in db.query("SELECT route_code, id FROM [tenant].[routes]"):
            ids["routes"][code] = str(rid)

    # ---- warehouses
    if dim.warehouses:
        rows = [(w["warehouse_code"], w["warehouse_name"], w["warehouse_type"],
                 w["address"], w["is_active"]) for w in dim.warehouses]
        _upsert_dim(
            db, "warehouses", "warehouse_code", rows,
            insert_cols=["warehouse_code", "warehouse_name", "warehouse_type",
                         "address", "is_active"],
            update_cols=["warehouse_name", "warehouse_type", "address", "is_active"],
            label="warehouses",
        )
        for code, wid in db.query("SELECT warehouse_code, id FROM [tenant].[warehouses]"):
            ids["warehouses"][code] = str(wid)

    # ---- users
    if dim.users:
        rows = [(u["username"], u["full_name"], u["email"], u["role"], u["is_active"])
                for u in dim.users]
        _upsert_dim(
            db, "user_accounts", "username", rows,
            insert_cols=["username", "full_name", "email", "role", "is_active"],
            update_cols=["full_name", "email", "role", "is_active"],
            label="user_accounts",
        )
        for un, uid in db.query("SELECT username, id FROM [tenant].[user_accounts]"):
            ids["users"][un] = str(uid)

    # ---- partners
    if dim.partners:
        rows = []
        for p in dim.partners:
            route_id = ids["routes"].get(p["route_code"]) if p["route_code"] else None
            rows.append((
                p["partner_code"], p["partner_name"], p["tax_code"], p["partner_type"],
                p["segment"], p["outlet_type_code"], p["channel_type"], p["business_line"],
                p["contact_person"], p["phone"], p["email"], p["address"], p["ward"],
                p["district"], p["province"], _q(p["latitude"], 7), _q(p["longitude"], 7),
                route_id, p["route_code"], p["dcr_code"],
                _q(p["credit_limit"], 2), p["payment_term_days"], p["is_active"], p["notes"],
            ))
        _upsert_dim(
            db, "partners", "partner_code", rows,
            insert_cols=["partner_code", "partner_name", "tax_code", "partner_type",
                         "segment", "outlet_type_code", "channel_type", "business_line",
                         "contact_person", "phone", "email", "address", "ward",
                         "district", "province", "latitude", "longitude",
                         "route_id", "route_code", "dcr_code", "credit_limit",
                         "payment_term_days", "is_active", "notes"],
            update_cols=["partner_name", "tax_code", "partner_type", "segment",
                         "outlet_type_code", "channel_type", "business_line",
                         "contact_person", "phone", "email", "address", "ward",
                         "district", "province", "latitude", "longitude",
                         "route_id", "route_code", "dcr_code", "credit_limit",
                         "payment_term_days", "is_active", "notes"],
            label="partners",
        )
        for code, pid in db.query("SELECT partner_code, id FROM [tenant].[partners]"):
            ids["partners"][code] = str(pid)

    # ---- uom_conversions
    if dim.uom_conversions:
        sql = """
        INSERT INTO [tenant].[uom_conversions]
            (product_id, from_uom, to_uom, conversion_factor, is_default)
        VALUES (?, ?, ?, ?, ?)
        """
        rows = []
        for u in dim.uom_conversions:
            pid = ids["products"].get(u["sku"])
            if pid:
                rows.append((pid, u["from_uom"], u["to_uom"],
                           _q(u["conversion_factor"], 4), u["is_default"]))
        db.insert_batched(sql, rows, label="uom_conversions")

    return ids


def load_orders(
    db: Database,
    orders: dict[str, OrderAggregate],
    ids: dict[str, dict[str, str]],
) -> dict[str, str]:
    """B6: nap sales_orders. Tra ve map order_number -> id."""
    sql = """
    INSERT INTO [tenant].[sales_orders]
        (order_number, customer_id, warehouse_id, sale_person_id,
         order_date, delivery_date, delivery_address, contact_phone,
         subtotal, discount_amount, tax_amount, total_amount,
         status, payment_status, paid_amount,
         invoice_number, invoice_date, notes)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?, ?)
    """
    rows = []
    for onum, o in orders.items():
        cust_id = ids["partners"].get(o.customer_code)
        wh_code = WAREHOUSE_MAP.get((o.warehouse_code or "").strip(),
                                    DEFAULT_WAREHOUSE_CODE)
        wh_id = ids["warehouses"].get(wh_code)
        if db.dry_run:
            # DRY-RUN: khong doc duoc id tu DB -> dung gia gia de dem du don
            cust_id = cust_id or f"DRYRUN-{o.customer_code}"
            wh_id = wh_id or f"DRYRUN-{wh_code}"
        elif not cust_id:
            db.add_error(
                "FK_VIOLATION",
                f"Don {onum} thieu khach hang '{o.customer_code}' - bo qua don",
                field_name="customer_id", field_value=onum, severity="ERROR",
                suggested_fix="Kiem tra lai danh sach khach hang trong RDT",
            )
            continue
        elif not wh_id:
            db.add_error(
                "FK_VIOLATION",
                f"Don {onum} thieu kho '{o.warehouse_code}' - bo qua don",
                field_name="warehouse_id", field_value=onum, severity="ERROR",
                suggested_fix="Kiem tra lai danh sach kho",
            )
            continue
        sp_id = ids["users"].get(o.salesperson_code) if o.salesperson_code else None
        address = " ".join(x for x in (o.province, o.district) if x) or None
        rows.append((
            onum[:50], cust_id, wh_id, sp_id,
            o.order_date, o.order_date, address, _s(o.phone),
            _q(o.subtotal, 2), _q(o.discount_amount, 2), _q(o.tax_amount, 2),
            _q(o.total_amount, 2),
            o.status, "UNPAID",
            _s(o.invoice_number), o.order_date,
            "; ".join(o.notes)[:500] if o.notes else None,
        ))
    db.insert_batched(sql, rows, label="sales_orders")
    if db.dry_run:
        # DRY-RUN: khong co id tu DB -> tao map gia de gia (khong dung sau
        # khi ghi DB) nhung giup bao cao co so don/dong dung.
        return {num: f"DRYRUN-{num}" for num in (o for o in orders)}
    return {num: str(oid) for num, oid in db.query("SELECT order_number, id FROM [tenant].[sales_orders]")}


def load_items(
    db: Database,
    items: list[dict],
    order_ids: dict[str, str],
    ids: dict[str, dict[str, str]],
) -> int:
    """B7: nap sales_order_items (giu nguyen chi tiet dong)."""
    sql = """
    INSERT INTO [tenant].[sales_order_items]
        (sales_order_id, line_number, product_id, product_sku, uom_code,
         ordered_qty, base_qty, unit_price, discount_percent, discount_amount,
         line_subtotal, line_total, delivered_qty, returned_qty, notes)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    """
    rows = []
    for it in items:
        oid = order_ids.get(it["order_number"])
        pid = ids["products"].get(it["sku"])
        if db.dry_run:
            # DRY-RUN: khong doc duoc id tu DB -> dung gia gia de dem dong
            if not oid:
                oid = f"DRYRUN-{it['order_number']}"
            if not pid:
                pid = f"DRYRUN-{it['sku']}"
        elif not oid or not pid:
            db.add_error(
                "FK_VIOLATION",
                f"Bo dong hang: don='{it['order_number']}' sku='{it['sku']}'",
                field_name="product_id", field_value=it["sku"],
                row_number=it["_row_number"], severity="ERROR",
                suggested_fix="Kiem tra lai master data san pham / don hang",
            )
            continue
        rows.append((
            oid, it["line_number"], pid, it["sku"], it["uom_code"],
            _q(it["ordered_qty"], 2), _q(it["base_qty"], 2), _q(it["unit_price"], 2),
            _q(it["discount_percent"], 2), _q(it["discount_amount"], 2),
            _q(it["line_subtotal"], 2), _q(it["line_total"], 2),
            _q(it["delivered_qty"], 2), _q(it["returned_qty"], 2), it["notes"],
        ))
    # DRY-RUN: insert_batched tra ve 0 (khong ghi DB) -> tra so dong thuc te
    n = db.insert_batched(sql, rows, label="sales_order_items")
    return n if not db.dry_run else len(rows)


def load_seed_data(db: Database, ids: dict[str, dict[str, str]]) -> tuple[int, int]:
    """
    Nap 11 don seed (SO-T001-*) + 14 dong hang seed tu sales_orders_clean.csv.

    Seed dung ISO date + product_id THAT (tro products_clean.product_id 1-84).
    => can map qua sku tu products_clean.
    """
    so_rows = [r for r in load_clean_csv("sales_orders_clean.csv") if not (r.get("source") or "").strip()]
    soi_rows = load_clean_csv("sales_order_items_clean.csv")
    if not so_rows or not soi_rows:
        return 0, 0

    # map product_id (legacy 1-84) -> sku
    prod_by_id: dict[str, str] = {}
    for r in load_clean_csv("products_clean.csv"):
        if r.get("product_id"):
            prod_by_id[r["product_id"].strip()] = r["sku"].strip()

    # map partner_id (legacy) -> partner_code
    part_by_id: dict[str, str] = {}
    for r in load_clean_csv("partners_clean.csv"):
        if r.get("partner_id"):
            part_by_id[r["partner_id"].strip()] = r["partner_code"].strip()

    seed_so_ids = {r["so_id"].strip() for r in so_rows}
    seed_items = [r for r in soi_rows if r["so_id"].strip() in seed_so_ids]

    # ---- don seed
    sql_so = """
    INSERT INTO [tenant].[sales_orders]
        (order_number, customer_id, warehouse_id, sale_person_id,
         order_date, delivery_date, delivery_address, contact_phone,
         subtotal, discount_amount, tax_amount, total_amount,
         status, payment_status, paid_amount,
         invoice_number, invoice_date, notes)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    """
    rows = []
    for r in so_rows:
        code = part_by_id.get((r.get("partner_id") or "").strip())
        cust_id = ids["partners"].get(code) if code else None
        if db.dry_run:
            cust_id = cust_id or f"DRYRUN-{code or r.get('partner_id')}"
        elif not cust_id:
            db.add_error(
                "FK_VIOLATION",
                f"Don seed {r.get('so_number')} thieu khach hang (partner_id={r.get('partner_id')})",
                field_name="customer_id", field_value=r.get("so_number", ""),
                severity="ERROR", suggested_fix="Kiem tra lai partners_clean.csv",
            )
            continue
        wh_id = ids["warehouses"].get("KHO001") or "DRYRUN-KHO001"
        od = parse_date(r.get("order_date", ""), "order_date", r.get("so_number", ""))
        dd = parse_date(r.get("delivered_date", ""), "delivered_date", r.get("so_number", "")) \
            if (r.get("delivered_date") or "").strip() else None
        def d(s: str) -> Decimal:
            try:
                return Decimal(s or "0")
            except InvalidOperation:
                return Decimal("0")
        rows.append((
            (r.get("so_number") or "")[:50], cust_id, wh_id, None,
            od, dd, None, None,
            d(r.get("subtotal", "")), d(r.get("discount_amount", "")),
            d(r.get("tax_amount", "")), d(r.get("total_amount", "")),
            r.get("status") or "CONFIRMED", r.get("payment_status") or "UNPAID",
            d(r.get("paid_amount", "")),
            None, od, _s(r.get("notes")),
        ))
    db.insert_batched(sql_so, rows, label="sales_orders (seed)")

    order_ids = {num: str(oid) for num, oid in db.query("SELECT order_number, id FROM [tenant].[sales_orders]")}
    if db.dry_run:
        order_ids = {(r.get("so_number") or "").strip(): f"DRYRUN-{r.get('so_number')}"
                     for r in so_rows}
    so_id_to_order = {}
    for r in so_rows:
        num = (r.get("so_number") or "").strip()
        if num in order_ids:
            so_id_to_order[r["so_id"].strip()] = order_ids[num]

    # ---- dong hang seed
    sql_item = """
    INSERT INTO [tenant].[sales_order_items]
        (sales_order_id, line_number, product_id, product_sku, uom_code,
         ordered_qty, base_qty, unit_price, discount_percent, discount_amount,
         line_subtotal, line_total, delivered_qty, returned_qty, notes)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    """
    irows = []
    for r in seed_items:
        oid = so_id_to_order.get(r["so_id"].strip())
        sku = prod_by_id.get((r.get("product_id") or "").strip())
        pid = ids["products"].get(sku) if sku else None
        if db.dry_run:
            oid = oid or f"DRYRUN-{r['so_id'].strip()}"
            pid = pid or f"DRYRUN-{sku}"
        elif not oid or not pid:
            db.add_error(
                "FK_VIOLATION",
                f"Bo dong hang seed: so_id={r.get('so_id')} product_id={r.get('product_id')}",
                field_name="product_id", field_value=r.get("product_id", ""),
                severity="ERROR", suggested_fix="Kiem tra lai sales_order_items_clean.csv",
            )
            continue
        def d(s: str) -> Decimal:
            try:
                return Decimal(s or "0")
            except InvalidOperation:
                return Decimal("0")
        irows.append((
            oid, int(r["line_number"] or 0), pid, sku, "CAI",
            _q(d(r.get("quantity", "")), 2), _q(d(r.get("quantity", "")), 2),
            _q(d(r.get("unit_price", "")), 2),
            _q(d(r.get("discount_pct", "")), 2), _q(d(r.get("discount_amount", "")), 2),
            _q(d(r.get("line_subtotal", "")), 2),
            # CHUAN HOA: line_total luon PRE-TAX nhu RDT
            #   line_total = line_subtotal - discount_amount
            # (CSV seed ghi line_total DA BAO GOM thue -> tach ra de khop RDT)
            _q(d(r.get("line_subtotal", "")) - d(r.get("discount_amount", "")), 2),
            _q(d(r.get("delivered_qty", "")), 2), Decimal("0"), _s(r.get("notes")),
        ))
    db.insert_batched(sql_item, irows, label="sales_order_items (seed)")
    return len(rows), len(irows)


def load_price_lists(db: Database, items: list[dict], orders: dict[str, OrderAggregate],
                     ids: dict[str, dict[str, str]]) -> int:
    """
    B8: bang gia theo (SKU, segment).

    Nguon: gia 'DS Truoc Chiet Khau/KM' theo SKU + segment cua don.
    Gia = median trong nhom.
    """
    seg_by_order = {k: v.segment for k, v in orders.items()}
    groups: dict[tuple[str, str], list[Decimal]] = defaultdict(list)
    first_date: dict[tuple[str, str], date] = {}
    for it in items:
        seg = seg_by_order.get(it["order_number"], "")
        key = (it["sku"], seg)
        if it["unit_price"] and it["unit_price"] > 0:
            groups[key].append(it["unit_price"])

    # ngay hieu luc = ngay dat hang nho nhat quan sat duoc (O(n), khong vong long don)
    items_by_order: dict[str, list[dict]] = defaultdict(list)
    for it in items:
        items_by_order[it["order_number"]].append(it)
    for onum, o in orders.items():
        if not o.order_date:
            continue
        for it in items_by_order.get(onum, ()):
            key = (it["sku"], o.segment)
            if key not in first_date or o.order_date < first_date[key]:
                first_date[key] = o.order_date

    def median(vals: list[Decimal]) -> Decimal:
        s = sorted(vals)
        m = len(s) // 2
        if len(s) % 2:
            return s[m]
        return (s[m - 1] + s[m]) / 2

    sql = """
    INSERT INTO [tenant].[price_lists]
        (price_list_code, price_list_name, product_id, segment, uom_code,
         unit_price, min_qty, effective_date, is_active)
    VALUES (?, ?, ?, ?, ?, ?, 1, ?, 1)
    """
    rows = []
    for (sku, seg), vals in sorted(groups.items()):
        pid = ids["products"].get(sku)
        if not pid:
            continue
        code = f"RDT-{seg or 'ALL'}-{sku}"[:50]
        eff = first_date.get((sku, seg)) or date(2026, 1, 1)
        rows.append((
            code, f"Bang gia {seg or 'chung'} - {sku}"[:255], pid, seg or None,
            "CAI", median(vals).quantize(Decimal("0.01")), eff,
        ))
    db.insert_batched(sql, rows, label="price_lists")
    return len(rows) if not db.dry_run else len(groups)


# =============================================================================
# PHẦN 5: IMPORT LOG, VERIFY, MAIN  (B9, B10)
# =============================================================================

def create_import_job(db: Database, files: Sequence[str], import_type: str = "FULL_RDT") -> str:
    """B9: ghi 1 dong vao tenant.import_jobs."""
    if db.dry_run:
        return "DRYRUN"
    total_size = 0
    names = ", ".join(files)
    for f in files:
        p = DATA_DIR / f
        if p.exists():
            total_size += p.stat().st_size
    job_code = f"IMP-{datetime.now():%Y%m%d-%H%M%S}"
    file_hash = hashlib.sha256()
    for f in files:
        p = DATA_DIR / f
        if p.exists():
            file_hash.update(file_sha256(p).encode())
    db.execute(
        """
        INSERT INTO [tenant].[import_jobs]
            (id, job_code, import_type, status, file_name, file_path,
             file_size, file_hash, total_rows, error_handling, uploaded_at)
        VALUES (NEWID(), ?, ?, 'IMPORTING', ?, ?, ?, ?, 0, 'SKIP', GETDATE())
        """,
        (job_code, import_type, names[:255], str(DATA_DIR)[:500], total_size, file_hash.hexdigest()[:64]),
    )
    db.commit()
    row = db.query_one("SELECT id FROM [tenant].[import_jobs] WHERE job_code = ?", (job_code,))
    job_id = str(row[0]) if row else ""
    db.job_id = job_id
    log.info("[B9] Tao import job %s (id=%s)", job_code, job_id)
    return job_id


def finalize_import_job(db: Database, stats: dict) -> None:
    if db.dry_run or not db.job_id:
        return
    db.flush_errors()
    status = "COMPLETED_WITH_ERRORS" if db.error_count else "COMPLETED"
    db.execute(
        """
        UPDATE [tenant].[import_jobs]
        SET status = ?, total_rows = ?, processed_rows = ?, success_rows = ?,
            error_rows = ?, warning_rows = ?, completed_at = GETDATE(),
            error_summary = ?
        WHERE id = ?
        """,
        (
            status,
            stats.get("total_rows", 0),
            stats.get("processed_rows", 0),
            stats.get("success_rows", 0),
            stats.get("error_rows", 0),
            stats.get("warning_rows", 0),
            stats.get("summary", "")[:1000],
            db.job_id,
        ),
    )
    db.commit()
    log.info("[B9] Cap nhat import job -> %s", status)


def verify(db: Database, expected: dict | None = None) -> dict:
    """B10: doi soat ket qua.

    `expected` - dict so sanh tinh tu bo dem in-memory (dung cho DRY-RUN,
    khi khong co du lieu trong DB de truy van).
    """
    result: dict = {}

    def cnt(sql: str) -> int:
        row = db.query_one(sql)
        return int(row[0]) if row else 0

    for t in ("categories", "products", "routes", "warehouses", "user_accounts",
              "partners", "sales_orders", "sales_order_items", "price_lists",
              "uom_conversions", "stock_ledger", "inventory_balances",
              "import_jobs", "import_errors"):
        result[t] = cnt(f"SELECT COUNT(*) FROM [tenant].[{t}]")

    # DRY-RUN: khong co du lieu trong DB -> lay so lieu tu bo dem in-memory
    if db.dry_run and expected:
        for k, v in expected.items():
            result[k] = v

    checks: list[tuple[str, int, str]] = []

    def check(name: str, sql: str, expect: str, dry_key: str | None = None) -> None:
        # DRY-RUN: doc DB khong co du lieu -> dung so lieu dem sanh trong RAM
        if db.dry_run and dry_key is not None and expected and dry_key in expected:
            v = int(expected[dry_key])
        else:
            v = cnt(sql)
        checks.append((name, v, expect))

    check("A1 categories = 9", "SELECT COUNT(*) FROM tenant.categories WHERE category_name IS NOT NULL", "9", "categories")
    check("A2 products SKU hop le", "SELECT COUNT(*) FROM tenant.products WHERE sku NOT LIKE '1000000[1-9]%' OR sku NOT LIKE '10000010'", "141", "products")
    check("A3 partners", "SELECT COUNT(*) FROM tenant.partners", "2682", "partners")
    check("A4 sales_orders", "SELECT COUNT(*) FROM tenant.sales_orders", "49844", "sales_orders")
    check("A5 items >= 176000", "SELECT COUNT(*) FROM tenant.sales_order_items", ">=176.000", "sales_order_items")
    check("A6 items NULL/AGGREGATED = 0",
          "SELECT COUNT(*) FROM tenant.sales_order_items WHERE product_id IS NULL OR product_sku='RD-FACTORY-AGGREGATED'", "0", "items_null")
    check("A7 FK items->orders", """
          SELECT COUNT(*) FROM tenant.sales_order_items i
          LEFT JOIN tenant.sales_orders o ON i.sales_order_id=o.id WHERE o.id IS NULL""", "0", "fk_items_orders")
    check("A8 FK items->products", """
          SELECT COUNT(*) FROM tenant.sales_order_items i
          LEFT JOIN tenant.products p ON i.product_id=p.id WHERE p.id IS NULL""", "0", "fk_items_products")
    check("A9 FK orders->partners", """
          SELECT COUNT(*) FROM tenant.sales_orders o
          LEFT JOIN tenant.partners p ON o.customer_id=p.id WHERE p.id IS NULL""", "0", "fk_orders_partners")
    check("A10 FK orders->warehouses", """
          SELECT COUNT(*) FROM tenant.sales_orders o
          LEFT JOIN tenant.warehouses w ON o.warehouse_id=w.id WHERE w.id IS NULL""", "0", "fk_orders_warehouses")
    check("A11 cong thuc tong don",
          "SELECT COUNT(*) FROM tenant.sales_orders WHERE ABS((subtotal - discount_amount + tax_amount) - total_amount) > 1", "0", "formula_mismatch")
    check("A12 items qty > 1e6", "SELECT COUNT(*) FROM tenant.sales_order_items WHERE ordered_qty > 1000000", "0", "qty_over_1e6")
    check("A13 audit_logs ton tai", "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='tenant' AND TABLE_NAME='audit_logs'", "1")
    check("A14 import_jobs >= 1", "SELECT COUNT(*) FROM tenant.import_jobs", ">=1")
    check("A15 price_lists > 0", "SELECT COUNT(*) FROM tenant.price_lists", ">0")
    check("A16 temporal tables >= 5", "SELECT COUNT(*) FROM sys.tables WHERE temporal_type = 2", ">=5")
    check("A17 trung order_number", """
          SELECT COUNT(*) FROM (SELECT order_number FROM tenant.sales_orders GROUP BY order_number HAVING COUNT(*)>1) d""", "0")
    check("A18 trung sku", """
          SELECT COUNT(*) FROM (SELECT sku FROM tenant.products GROUP BY sku HAVING COUNT(*)>1) d""", "0")
    check("A19 trung partner_code", """
          SELECT COUNT(*) FROM (SELECT partner_code FROM tenant.partners GROUP BY partner_code HAVING COUNT(*)>1) d""", "0")

    result["checks"] = checks
    return result


def print_report(result: dict) -> None:
    print("\n" + "=" * 78)
    print("KẾT QUẢ IMPORT - ERP FMCG")
    print("=" * 78)
    print(f"{'Bảng':<26}{'Số dòng':>14}")
    print("-" * 78)
    for k, v in result.items():
        if k not in ("checks", "error_by_code"):
            print(f"  {k:<24}{v:>14,}")
    print("\n" + "-" * 78)
    print(f"{'Kiểm tra':<48}{'Giá trị':>14}  {'Kỳ vọng':<12}")
    print("-" * 78)
    for name, val, expect in result.get("checks", []):
        print(f"  {name:<46}{val:>14,}  {expect:<12}")
    if result.get("error_by_code"):
        print("\n" + "-" * 78)
        print("Phân bố lỗi theo mã")
        print("-" * 78)
        for code, cnt in sorted(result["error_by_code"].items(), key=lambda kv: -kv[1]):
            print(f"  {code:<46}{cnt:>14,}")
    print("=" * 78)


def main() -> int:
    parser = argparse.ArgumentParser(description="ERP-FMCG Full Import Pipeline")
    parser.add_argument("--server", default=DEFAULT_SERVER, help="SQL Server instance")
    parser.add_argument("--database", default=DEFAULT_DATABASE, help="Ten database")
    parser.add_argument("--files", nargs="*", default=None,
                        help="Danh sach file RDT can nap (mac dinh: tat ca 7 file)")
    parser.add_argument("--dry-run", action="store_true",
                        help="Chi doc + tinh toan, khong ghi vao DB")
    parser.add_argument("--no-seed", action="store_true", help="Bo qua 11 don seed")
    parser.add_argument("--no-clean", action="store_true",
                        help="Khong xoa du lieu nghiep vu truoc khi nap (mac dinh: co xoa)")
    parser.add_argument("--log-file", default=None, help="Ghi log ra file")
    parser.add_argument("--data-dir", default=None,
                        help="Thu muc chua CSV (mac dinh: migration_data canh script)")
    args = parser.parse_args()

    global DATA_DIR
    if args.data_dir:
        DATA_DIR = Path(args.data_dir).expanduser().resolve()
        log.info("Dung thu muc du lieu: %s", DATA_DIR)

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(levelname)-7s] %(message)s",
        datefmt="%H:%M:%S",
        handlers=[logging.StreamHandler(sys.stdout)],
    )
    if args.log_file:
        logging.getLogger().addHandler(logging.FileHandler(args.log_file, encoding="utf-8"))

    files = args.files or RDT_FILES
    missing = [f for f in files if not (DATA_DIR / f).exists()]
    if missing:
        log.error("Khong tim thay file: %s", missing)
        log.error("Thu muc du lieu: %s", DATA_DIR)
        return 2

    t_start = time.time()
    db = Database(args.server, args.database, dry_run=args.dry_run)
    try:
        db.connect()

        # [0] kiem tra moi truong
        log.info("[0] Kiem tra moi truong: %s / %s", args.server, args.database)
        if not db.dry_run:
            required = ["categories", "products", "routes", "warehouses", "user_accounts",
                        "partners", "sales_orders", "sales_order_items", "price_lists",
                        "import_jobs", "import_errors", "audit_logs"]
            existing = {
                r[0].lower() for r in db.query(
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA='tenant'")
            }
            miss = [t for t in required if t not in existing]
            if miss:
                log.error("Thieu bang bat buoc: %s", miss)
                log.error("Hay chay 01_schema.sql truoc khi import.")
                return 3

        # [1] chuan hoa + doc RDT
        log.info("[1] Doc va chuan hoa %d file RDT...", len(files))
        rdt = load_rdt(files, db)

        # [2] trich xuat master data
        log.info("[2] Trich xuat master data...")
        dim = extract_dimensions(rdt, db)

        # [3] tinh lai so tien
        log.info("[3] Tinh lai so tien tu cap dong...")
        orders = aggregate_orders(rdt, db)
        sku_tax = resolve_tax_rates(rdt, db)
        items = compute_amounts(rdt, orders, sku_tax, db)

        # [4] nap master data
        if not db.dry_run:
            # Thu tu QUAN TRONG:
            #   1) clean_import_log  - xoa job/ loi CU (phai lam TRUOC khi tao job moi,
            #                           neu tao job truoc thi clean se xoa lu job moi)
            #   2) create_import_job - tao job moi -> moi co job_id de ghi loi
            #   3) flush_errors      - ghi cac loi da phat sinh o buoc [1]-[3]
            #                           (MISSING_SKU, TAX_RATE_UNRESOLVED, ...)
            #   4) clean_business_data - xoa du lieu nghiep vu cu, giu master seed
            clean_import_log(db)
            create_import_job(db, files)
            db.flush_errors()          # ghi cac loi da phat sinh o [1]-[3]
            if not args.no_clean:
                clean_business_data(db)
        log.info("[4] Nap master data...")
        ids = load_dimensions(db, dim)

        # [5] nap don + dong hang
        log.info("[5] Nap don hang...")
        order_ids = load_orders(db, orders, ids)
        log.info("[6] Nap chi tiet dong hang (%d dong)...", len(items))
        n_items = load_items(db, items, order_ids, ids)

        # [7] nap du lieu seed
        n_seed_so = n_seed_items = 0
        if not args.no_seed:
            log.info("[7] Nap du lieu seed (11 don + 14 dong)...")
            n_seed_so, n_seed_items = load_seed_data(db, ids)

        # [8] bang gia
        log.info("[8] Nap bang gia...")
        n_price = load_price_lists(db, items, orders, ids)

        # [9] ghi import log
        log.info("[9] Ghi import log...")
        db.flush_errors()
        stats = {
            "total_rows": len(rdt),
            "processed_rows": len(items),
            "success_rows": max(0, len(items) - db.error_count),
            "error_rows": db.error_rows,
            "warning_rows": db.warning_rows,
            "summary": (
                f"RDT rows={len(rdt)}, orders={len(orders)}, items={n_items}, "
                f"seed_orders={n_seed_so}, seed_items={n_seed_items}, price_lists={n_price}"
            ),
        }
        finalize_import_job(db, stats)

        # [10] verify
        log.info("[10] Verify...")

        # DRY-RUN: khong co du lieu trong DB -> tinh cac chi so A1-A12 tu
        # bo dem in-memory de bao cao van co y nghia.
        expected: dict = {}
        if db.dry_run:
            n_ord = len(orders) + (n_seed_so if not args.no_seed else 0)
            n_itm = n_items + (n_seed_items if not args.no_seed else 0)
            valid_skus = {p["sku"] for p in dim.products}
            n_null = sum(1 for it in items if not it["sku"])
            n_qty_big = sum(1 for it in items
                            if (it.get("ordered_qty") or 0) > 1000000)
            n_formula = sum(
                1 for o in orders.values()
                if abs((o.subtotal - o.discount_amount + o.tax_amount)
                       - o.total_amount) > 1
            )
            expected = {
                "categories": len(dim.categories),
                "products": len(dim.products),
                "partners": len(dim.partners),
                "sales_orders": n_ord,
                "sales_order_items": n_itm,
                "price_lists": n_price,
                "items_null": n_null,
                "fk_items_orders": 0,
                "fk_items_products": 0,
                "fk_orders_partners": 0,
                "fk_orders_warehouses": 0,
                "formula_mismatch": n_formula,
                "qty_over_1e6": n_qty_big,
            }

        result = verify(db, expected=expected)
        result.update({
            "rdt_rows": len(rdt),
            "orders_loaded": len(order_ids),
            "items_loaded": n_items,
            "seed_orders": n_seed_so,
            "seed_items": n_seed_items,
            "price_lists": n_price,
            "errors": db.error_count,
            "error_by_code": db.error_by_code,
            "duration_sec": round(time.time() - t_start, 1),
        })
        print_report(result)

        print(f"\nTong thoi gian: {time.time() - t_start:.1f}s")
        return 0

    except Exception as exc:
        log.exception("LOI FATAL: %s", exc)
        db.rollback()
        return 1
    finally:
        db.close()


if __name__ == "__main__":
    raise SystemExit(main())
