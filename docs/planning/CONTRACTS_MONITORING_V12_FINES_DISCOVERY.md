# V12 fines monitoring discovery

Status: implementation evidence at `ea101648`, not a new business ruling, production activation, or fine workflow.
Contracts Monitoring does not query, create, assess or settle fines.

## Existing authority

- `EEMOCantilanSDS.Domain/Entities/Revenue/FineAssessment.cs`: a source-owned non-cash liability with exact ContractId or ObligationAccountId, governing policy/version, original principal, incident/violation evidence, affected period, actor, UTC timestamp and retry identity. FineAssessmentEvent retains assessment history.
- `EEMOCantilanSDS.Application/Common/Revenue/FineAssessmentWorkflow.cs`: Head-authorized assessment; Head/Admin reads. `ReadAsync` returns assessed, settled and remaining amounts, with Outstanding/Partial/Settled status. It rejects unreconciled settlement evidence rather than inventing a balance.
- `EEMOCantilanSDS.Application/Common/Revenue/FineAssessmentCollectionSource.cs`: settlement comes from canonical allocations and correction effects; no duplicate paid counter or ledger is needed.
- `GET /api/fine-assessments`: existing tenant-scoped account-kind/account-id/search register. It takes 201 records and rejects more than 200 matches instead of silently truncating totals. It is an authoritative bounded liability register, but has no general paginated open-fine monitoring API.
- `PenaltyDefinitionWorkflow.GetSummaryAsync`: existing period-scoped outstanding fine count/amount is derived from assessment/settlement evidence, distinct from collected fine revenue.
- `DirectFineCollectionWorkflow` / `DirectFineAuthority`: authorized direct cash collection. A direct collection is not evidence of an unpaid liability, and cannot be included as an open fine by guessing a liable account from payer text.

## Future bounded slice

Reuse FineAssessment and its settlement reader. Agree summary scope and open definition (remaining > 0), date basis (violation/assessment/affected period), correction handling, filters, stable pagination and typed account/source drill-down. Preserve policy gating, Head-only assessment, tenant filters, exact liable source IDs and existing reconciliation refusals. Test partial settlement, full settlement, corrections/reversals and cross-tenant summary/search isolation.

Do not introduce an independent fines ledger, infer a violation from an expired contract, or convert Direct Fine Collections into assessed debt. A monitoring UI and a paginated read projection can be a later task; new assessment, closure, waiver and enforcement semantics need their own explicit approval.
