-- ============================================================================
-- Excusal audit — READ ONLY. Nothing here writes, updates or deletes.
-- ============================================================================
--
-- WHY THIS EXISTS
--
-- Two excusal faults were fixed on 2026-09-06/07. Both fixes are forward-only:
-- they change what happens from now on and do not correct rows already written.
-- Before deciding whether any repair is warranted, the office needs to know
-- whether either fault actually occurred in its data, and where.
--
--   FAULT 1 (fixed 570a19ce) — reopening a closed stall or section rewrote days
--   already recorded as NOT COLLECTED into EXCUSED. An excused day owes nothing,
--   so the office silently stopped billing a day a collector had recorded as
--   owed. Affected rows carry no marker of their own: after the rewrite they are
--   indistinguishable, field by field, from a day a collector genuinely marked
--   absent.
--
--   What DOES distinguish them is the shape of the write. A reopen excuses every
--   day of the frozen span in ONE act, so a run of absent days for one stall
--   shares a single "UpdatedAt" to the millisecond. A collector marks one day at
--   a time, in the field, minutes or hours apart. Query 1 looks for that shape.
--
--   FAULT 2 (fixed e12d78e2) — a closure excused a month that already had a
--   PARTIAL payment against it, forgiving the remainder AND letting the money
--   paid drift onto other months. That one leaves a direct trace: an exception
--   row of reason TemporaryClosure on a month whose payment record is Partial.
--   Query 3 finds it exactly; no inference needed.
--
-- HOW TO READ THE RESULTS
--
--   Empty results mean the fault did not occur and there is nothing to repair.
--   That is the outcome to hope for and it is a complete answer.
--
--   Non-empty results are CANDIDATES, not proof — for Query 1 especially. A
--   batch of absent days can also be a legitimate closure of a stall that owed
--   nothing, which is exactly what the feature is for. Judge each against
--   whether that stall was actually trading on those days.
--
-- HOW TO RUN
--
--   Against a READ-ONLY connection if one is available. Every statement is a
--   SELECT; there is no transaction to commit and nothing to roll back.
--
-- NOTE ON TENANCY: every table carries "MunicipalityId". The queries report it
-- so Cantilan, Madrid and Carrascal can be told apart, and so a finding is never
-- attributed to the wrong office.
-- ============================================================================


-- ----------------------------------------------------------------------------
-- QUERY 1 — Batches of excused days: the signature of a closure reopen.
--
-- Groups absent daily collections by stall and by the exact moment they were
-- written. A group of two or more is one act covering several days, which a
-- collector in the field does not produce.
-- ----------------------------------------------------------------------------
SELECT
    m."Name"                              AS municipality,
    f."Code"                              AS facility,
    s."StallNo"                           AS stall_no,
    COALESCE(s."CustomSectionName", s."Section"::text, '—') AS area,
    dc."UpdatedBy"                        AS written_by,
    dc."UpdatedAt"                        AS written_at,
    COUNT(*)                              AS days_excused_in_one_act,
    MIN(dc."CollectionDate")              AS earliest_day,
    MAX(dc."CollectionDate")              AS latest_day
FROM "DailyCollections" dc
JOIN "Stalls"          s ON s."Id" = dc."StallId"
JOIN "Facilities"      f ON f."Id" = s."FacilityId"
JOIN "Municipalities"  m ON m."Id" = dc."MunicipalityId"
WHERE dc."IsAbsent" = true
  AND dc."UpdatedAt" IS NOT NULL
GROUP BY m."Name", f."Code", s."StallNo", s."CustomSectionName", s."Section",
         dc."UpdatedBy", dc."UpdatedAt"
HAVING COUNT(*) > 1
ORDER BY dc."UpdatedAt" DESC, m."Name", s."StallNo";


-- ----------------------------------------------------------------------------
-- QUERY 2 — The same, narrowed to stalls in a named area.
--
-- Sari Sari was closed and reopened during the period in question, so it is the
-- most likely place for Fault 1 to have bitten. Change the area name to check
-- another. Listed day by day rather than grouped, so each day can be judged.
-- ----------------------------------------------------------------------------
SELECT
    m."Name"                  AS municipality,
    s."StallNo"               AS stall_no,
    s."CustomSectionName"     AS area,
    c."ActualOccupant"        AS occupant,
    dc."CollectionDate"       AS day_excused,
    dc."UpdatedBy"            AS written_by,
    dc."UpdatedAt"            AS written_at,
    dc."CreatedAt"            AS row_created_at,
    -- A row CREATED well before it was updated was not created by the excusal:
    -- something existed on that day first, which is the Fault 1 shape.
    (dc."UpdatedAt" - dc."CreatedAt") AS created_to_updated_gap
FROM "DailyCollections" dc
JOIN "Stalls"          s ON s."Id" = dc."StallId"
JOIN "Municipalities"  m ON m."Id" = dc."MunicipalityId"
LEFT JOIN "Contracts"  c ON c."StallId" = s."Id" AND c."IsActive" = true
WHERE dc."IsAbsent" = true
  AND s."CustomSectionName" ILIKE '%Sari%'
ORDER BY m."Name", s."StallNo", dc."CollectionDate";


-- ----------------------------------------------------------------------------
-- QUERY 2b — The strongest single indicator for Fault 1, across all areas.
--
-- An absent row whose CreatedAt is meaningfully earlier than its UpdatedAt was
-- NOT created as an excusal: a record already existed for that day and was later
-- rewritten. That is precisely what the fixed code used to do. A row created and
-- excused in one act has the two timestamps together.
--
-- The one-minute threshold allows for ordinary write latency.
-- ----------------------------------------------------------------------------
SELECT
    m."Name"                  AS municipality,
    f."Code"                  AS facility,
    s."StallNo"               AS stall_no,
    COALESCE(s."CustomSectionName", s."Section"::text, '—') AS area,
    dc."CollectionDate"       AS day_now_excused,
    dc."CreatedAt"            AS originally_recorded_at,
    dc."UpdatedAt"            AS rewritten_at,
    dc."UpdatedBy"            AS rewritten_by
FROM "DailyCollections" dc
JOIN "Stalls"          s ON s."Id" = dc."StallId"
JOIN "Facilities"      f ON f."Id" = s."FacilityId"
JOIN "Municipalities"  m ON m."Id" = dc."MunicipalityId"
WHERE dc."IsAbsent" = true
  AND dc."UpdatedAt" IS NOT NULL
  AND dc."UpdatedAt" > dc."CreatedAt" + INTERVAL '1 minute'
ORDER BY dc."UpdatedAt" DESC, m."Name", s."StallNo", dc."CollectionDate";


-- ----------------------------------------------------------------------------
-- QUERY 3 — Fault 2, found exactly: a closure-excused month that has money on it.
--
-- Under the rule as of e12d78e2 this combination cannot be created. Any row here
-- predates the fix. "Partial" is the case that drifted; "Paid" is included
-- because it should never have been excused either and is worth seeing.
--
-- PaymentStatus is stored as an int: Unpaid = 1, Partial = 2, Paid = 3. Verified
-- against the enum rather than assumed — an inverted filter here would report
-- unpaid months as carrying money and miss the paid ones entirely.
-- ----------------------------------------------------------------------------
SELECT
    m."Name"                  AS municipality,
    f."Code"                  AS facility,
    s."StallNo"               AS stall_no,
    c."ActualOccupant"        AS occupant,
    e."BillingYear"           AS excused_year,
    e."BillingMonth"          AS excused_month,
    e."Reason"                AS excusal_reason,
    e."Note"                  AS excusal_note,
    e."CreatedBy"             AS excused_by,
    e."CreatedAt"             AS excused_at,
    pr."Status"               AS payment_status,
    pr."BaseRentalAmount"     AS month_rent,
    pr."PartialAmount"        AS amount_paid,
    pr."ORNumber"             AS receipt
FROM "StallMonthlyExceptions" e
JOIN "Stalls"          s  ON s."Id" = e."StallId"
JOIN "Facilities"      f  ON f."Id" = s."FacilityId"
JOIN "Municipalities"  m  ON m."Id" = e."MunicipalityId"
LEFT JOIN "Contracts"  c  ON c."StallId" = s."Id" AND c."IsActive" = true
JOIN "PaymentRecords" pr ON pr."StallId"      = e."StallId"
                        AND pr."BillingYear"  = e."BillingYear"
                        AND pr."BillingMonth" = e."BillingMonth"
WHERE pr."Status" IN (2, 3)   -- PaymentStatus: Unpaid = 1, Partial = 2, Paid = 3
ORDER BY m."Name", f."Code", s."StallNo", e."BillingYear", e."BillingMonth";


-- ----------------------------------------------------------------------------
-- QUERY 4 — Scale, so the answer is a number before it is a list.
-- ----------------------------------------------------------------------------
SELECT
    (SELECT COUNT(*) FROM "DailyCollections"
      WHERE "IsAbsent" = true)                                   AS excused_days_total,
    (SELECT COUNT(*) FROM "DailyCollections"
      WHERE "IsAbsent" = true
        AND "UpdatedAt" > "CreatedAt" + INTERVAL '1 minute')      AS excused_days_rewritten_later,
    (SELECT COUNT(*) FROM "StallMonthlyExceptions")               AS excused_months_total,
    (SELECT COUNT(*)
       FROM "StallMonthlyExceptions" e
       JOIN "PaymentRecords" pr ON pr."StallId"      = e."StallId"
                               AND pr."BillingYear"  = e."BillingYear"
                               AND pr."BillingMonth" = e."BillingMonth"
      WHERE pr."Status" IN (2, 3))                                AS excused_months_with_money;
