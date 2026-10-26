"""
Build Pack/erp-fmcg-database/01_schema.sql - gộp DDL từ database/01_schema/*.sql

Yêu cầu: UTF-8 KHÔNG BOM (SQL Server đọc BOM sẽ lỗi ﻿ ở dòng đầu)

Cấu hình (KHÔNG hardcode đường dẫn — T5):
  - ERP_FMCG_SCHEMA_SRC : thư mục chứa các file nguồn .sql
                           (mặc định: tìm ngược lên từ pack root)
  - ERP_FMCG_SCHEMA_OUT : file .sql đầu ra
                           (mặc định: <pack root>/01_schema.sql)

Ví dụ:
  set ERP_FMCG_SCHEMA_SRC=E:\\Work\\Database\\erp-fmcg-csharp\\database\\01_schema
  python scripts\\build_schema.py
"""
import os
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

PACK_ROOT = Path(__file__).resolve().parent.parent

# Thu muc nguon: uu tien bien moi truong, sau do thu muc "database/01_schema"
# nam cung cap voi pack root (vi du khi pack nam trong erp-fmcg-csharp).
_env_src = os.environ.get("ERP_FMCG_SCHEMA_SRC")
if _env_src:
    SRC = Path(_env_src).expanduser().resolve()
else:
    _candidates = [
        PACK_ROOT.parent / "erp-fmcg-csharp" / "database" / "01_schema",
        PACK_ROOT / "database" / "01_schema",
        PACK_ROOT.parent / "database" / "01_schema",
    ]
    SRC = next((c for c in _candidates if c.is_dir()),
               _candidates[0])

# File dau ra: uu tien bien moi truong, mac dinh ngay canh pack root.
_env_out = os.environ.get("ERP_FMCG_SCHEMA_OUT")
OUT = (Path(_env_out).expanduser().resolve() if _env_out
       else PACK_ROOT / "01_schema.sql")

# Thu tu quan trong: DB/schema -> core -> business -> staging -> triggers -> temporal -> seed
PARTS = [
    "00_create_database.sql",
    "01_core_tables.sql",
    "02_business_tables.sql",
    "03_staging_tables.sql",
    "04_triggers.sql",
    "06_temporal_audit.sql",
    "05_seed_data.sql",
]

BANNER = """-- ===================================================================
-- ERP-FMCG - MS SQL Server 2022 - SCHEMA DDL TỔNG HỢP
-- -------------------------------------------------------------------
-- File này được SINH TỰ ĐỘNG bởi scripts/build_schema.py
-- KHÔNG sửa trực tiếp - hãy sửa file nguồn trong database/01_schema/
-- -------------------------------------------------------------------
-- Target : MS SQL Server 2022 (Express) - COLLATE Vietnamese_CI_AS
-- Schema : tenant
-- Encoding: UTF-8 KHÔNG BOM
-- -------------------------------------------------------------------
-- Thứ tự:
--   1. 00_create_database.sql  - tạo DB + schema tenant
--   2. 01_core_tables.sql      - 6 bảng core (2 temporal)
--   3. 02_business_tables.sql  - 6 bảng business (2 temporal)
--   4. 03_staging_tables.sql   - 4 bảng staging (import pipeline)
--   5. 04_triggers.sql         - trigger tồn kho
--   6. 06_temporal_audit.sql   - audit_logs + views
--   7. 05_seed_data.sql        - seed nền tảng
-- ===================================================================

"""


def main() -> int:
    if not SRC.is_dir():
        print(f"[ERROR] Khong tim thay thu muc nguon: {SRC}")
        print("        Dat bien moi truong ERP_FMCG_SCHEMA_SRC tro toi thu muc chua file .sql")
        return 2

    print(f"Nguon : {SRC}")
    print(f"Dau ra: {OUT}")

    chunks = [BANNER]
    missing = 0
    for p in PARTS:
        f = SRC / p
        if not f.exists():
            print(f"  [MISSING] {p}")
            missing += 1
            continue
        raw = f.read_bytes()
        if raw.startswith(b"\xef\xbb\xbf"):
            raw = raw[3:]
        text = raw.decode("utf-8")
        chunks.append(f"\n\n-- ===== {p} =====\n")
        chunks.append(text)
        print(f"  [OK] {p} ({len(text):,} chars)")

    if missing:
        print(f"\n[ERROR] Thiếu {missing} file nguon - khong ghi file dau ra.")
        return 1

    out = "".join(chunks)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(out.encode("utf-8"))  # no BOM
    print(f"\nĐã ghi: {OUT} ({OUT.stat().st_size:,} bytes, UTF-8 no BOM)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
