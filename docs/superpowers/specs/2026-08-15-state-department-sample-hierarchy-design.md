# State Department Sample Hierarchy Design

## Goal

Populate all `TEMP_PD_SCHED_PC` rows with deterministic sample Bureau and
Organization values for document generation and filtering demonstrations.

## Scope

- Update all 5,005 existing `TEMP_PD_SCHED_PC` headers.
- Preserve the existing backfilled `POSITION_SENSITIVITY` and
  `GM_PUBLIC_TRUST` values.
- Treat all Bureau and Organization values created by this work as synthetic
  sample data, not real Department of State assignments.

## Hierarchy

The Bureau code is a six-digit parent code. Organization codes are six-digit
children that share the Bureau's two-digit prefix and never use the `0000`
child suffix.

| Bureau code | Bureau description | Child organization range |
| --- | --- | --- |
| `100000` | Bureau of African Affairs | `100001`-`109999` |
| `200000` | Bureau of East Asian and Pacific Affairs | `200001`-`209999` |
| `300000` | Bureau of European and Eurasian Affairs | `300001`-`309999` |
| `400000` | Bureau of Near Eastern Affairs | `400001`-`409999` |
| `500000` | Bureau of Western Hemisphere Affairs | `500001`-`509999` |

## Assignment Rule

For each header, use `MOD(PD_SEQ_NUM, 5)` to select one of the five Bureau
records. Generate the child organization suffix from `PD_SEQ_NUM` so the same
row receives the same sample organization code on every rerun. Set
`ORG_DESC` to a matching synthetic office name that identifies its parent
Bureau.

## Guardrails

- Populate only blank `BUREAU_CODE`, `BUREAU_DESC`, `PD_ORIGIN_ORG_CODE`, and
  `ORG_DESC` values.
- Do not update position title, series, grade, duties, evaluation results,
  Position Sensitivity, or Public Trust.
- Verify every resulting organization code matches `^\d{6}$` and has the
  same two-digit prefix as its Bureau code.

## Validation

After the update:

1. All 5,005 headers have nonblank Bureau and Organization values.
2. Every organization code is six digits.
3. Every organization code is a child of the recorded Bureau code.
4. The five configured Bureaus all have assigned headers.