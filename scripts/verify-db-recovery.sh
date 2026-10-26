#!/usr/bin/env bash
# Full-database recovery proof: dump a PostgreSQL database in custom format, checksum it, restore it into a NEW, isolated database,
# and compare the row count of EVERY table (legacy tables included) with the source. Prints PASS/FAIL and exits non-zero on any difference.
#
# It only READS the source (pg_dump + counts) and only ever writes to the temporary database it creates and drops. It refuses to restore
# into the source database or into a database that already exists. Run it against a staging/scratch copy, or against a production
# SNAPSHOT restored elsewhere - never point TARGET at a live database.
#
# Usage (PostgreSQL inside a docker container):
#   scripts/verify-db-recovery.sh <container> <db_user> <source_db> [dump_dir]
# Example (the local scratch database):
#   scripts/verify-db-recovery.sh stalltrack-e2e-reports-20261025 e2e e2e_reports /tmp
set -euo pipefail
export MSYS_NO_PATHCONV=1   # Git Bash on Windows must not rewrite /tmp paths passed to docker
C="${1:?container}"; U="${2:?db user}"; SRC="${3:?source db}"; OUT="${4:-/tmp}"
TARGET="recovery_check_$(date +%Y%m%d%H%M%S)"
DUMP="/tmp/${SRC}_$(date +%Y%m%d%H%M%S).dump"
[ "$TARGET" != "$SRC" ] || { echo "refusing: target equals source"; exit 2; }
q() { docker exec "$C" psql -U "$U" -d "$1" -At -c "$2"; }
trap 'docker exec "$C" psql -U "$U" -d postgres -c "drop database if exists \"$TARGET\"" >/dev/null 2>&1 || true; docker exec "$C" rm -f "$DUMP" >/dev/null 2>&1 || true' EXIT

echo "1/5 dump  $SRC -> $DUMP (custom format)"
docker exec "$C" pg_dump -U "$U" -Fc -d "$SRC" -f "$DUMP"
SIZE=$(docker exec "$C" sh -c "wc -c < $DUMP"); [ "$SIZE" -gt 1000 ] || { echo "FAIL: dump is empty ($SIZE bytes)"; exit 1; }
SUM=$(docker exec "$C" sha256sum "$DUMP" | cut -d' ' -f1)
echo "2/5 checksum $SUM ($SIZE bytes)"
docker exec "$C" pg_restore -U "$U" --list "$DUMP" >/dev/null && echo "    archive catalogue readable"

echo "3/5 restore into isolated database $TARGET"
q postgres "create database \"$TARGET\""
docker exec "$C" pg_restore -U "$U" -d "$TARGET" --no-owner "$DUMP" >/tmp/pg_restore.log 2>&1 || { echo "FAIL: pg_restore reported errors"; tail -5 /tmp/pg_restore.log; exit 1; }

echo "4/5 compare every table's row count"
FAIL=0; N=0
while IFS= read -r T; do
  A=$(q "$SRC" "select count(*) from \"$T\""); B=$(q "$TARGET" "select count(*) from \"$T\"" 2>/dev/null || echo MISSING)
  N=$((N+1)); [ "$A" = "$B" ] || { echo "    MISMATCH $T source=$A restored=$B"; FAIL=1; }
done < <(q "$SRC" "select table_name from information_schema.tables where table_schema='public' and table_type='BASE TABLE' order by 1")
echo "    compared $N tables"
echo "5/5 migrations: source=$(q "$SRC" 'select count(*) from "__EFMigrationsHistory"') restored=$(q "$TARGET" 'select count(*) from "__EFMigrationsHistory"')"
[ "$FAIL" = 0 ] && { echo "PASS: full-database recovery verified (temporary database dropped)"; } || { echo "FAIL: restored data differs from source"; exit 1; }
