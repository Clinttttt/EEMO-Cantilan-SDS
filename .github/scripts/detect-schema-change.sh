#!/usr/bin/env bash
# Lists the files in a commit range that can change the database schema. Fails CLOSED: any path that looks like an EF Core
# migration, designer or model snapshot anywhere in the repository counts, so a new migrations folder convention cannot
# silently skip the production backup gate. Usage: detect-schema-change.sh <before> <after>
set -euo pipefail
before="$1"; after="$2"
# Directory named Migrations/Migration, files ending .Designer.cs beside a migration, and any *ModelSnapshot*.cs.
PATTERN='(^|/)migrations?/|ModelSnapshot|^[^ ]*/[0-9]{14}_[A-Za-z0-9_]+(\.Designer)?\.cs$'
git diff --name-only "$before" "$after" | grep -Ei "$PATTERN" || true
