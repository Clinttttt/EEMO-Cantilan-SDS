# V14 — Fish/Meat requests default on

All schema-ready municipalities now get the existing nonfinancial collector-submitted, Head-reviewed Fish/Meat registration workflow without individual activation. The API uses all nonempty tenant GUIDs by default; authorization, tenant-scoped queries and actual database schema readiness are unchanged.

Explicit `StallTrack:FishMeatRequests:Enabled=false` disables requests. A nonempty optional MunicipalityIds allow-list restricts them. Invalid or duplicate tenant IDs are rejected. The separately guarded Development working-copy path remains isolated. No mandatory registration payment policy, SRC, rate, payment, or financial record changes.

Verification includes default-on, opt-out, allow-list and WorkingCopy tests, and an authenticated PostgreSQL collector submission/Head approval flow with no activation configuration. Verify results before promotion.

Production rollout requires main CI, immutable image publisher and private AWS workflow with exact source SHA. A production explicit opt-out or missing migration/table continues to block the feature; neither condition is bypassed.
