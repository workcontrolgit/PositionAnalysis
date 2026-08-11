# Schedule P/C AI Scoring Criteria (Current)

**Source:** `src/SchedulePCMcp/templates/score-prompt.txt`
**Legal basis:** EO "Implementing Schedule Policy/Career in the Excepted Service" (June 3, 2026); 5 U.S.C. § 7511(b)(2); 5 U.S.C. § 3302.
**Documented:** 2026-08-11

This document describes the criteria the AI uses to score a position description (PD) for Schedule Policy/Career (Schedule P/C) status. It is a human-readable reference to the scoring rubric embedded in the LLM prompt template; if the two ever disagree, the prompt template is authoritative.

## Evidence rules

- **Duty text is the primary evidence source** for all four criteria. The AI scans the full duty text (ordered by percent of time spent) before rendering any verdict.
- The PD's introductory text is **not** used as evidence.
- Structural fields — supervisory/manager level, position sensitivity, and public trust risk level — are **corroborating signals only**. They can tip a borderline duty-text finding toward a positive result, but none of them can trigger a criterion by itself.

## The four criteria

### 1. Policy-Determining
Exercising authority to establish, decide, or direct government/agency policy — not merely advising on or implementing decisions made by others.
- Triggers on: binding policy determinations, final say on policy direction, directing other offices on policy positions, final approval authority over policy proposals.
- Does **not** trigger on: implementing directives from above, giving input without decision authority, or developing recommendations that go to senior officials for review.

### 2. Policy-Making
Formulating, developing, or substantially shaping policy proposals, regulations, or official policy positions — beyond implementing or reviewing policy set by others.
- Triggers on: originating/drafting regulations or sub-regulatory guidance, leading or substantially contributing to rulemaking, developing agency policy frameworks or strategic priorities.
- Does **not** trigger on: reviewing others' policy work for compliance, implementing already-set policy, technical/scientific analysis that only informs others' decisions, or general strategic/program planning.
- **Common false positive:** verbs like "develops," "implements," "coordinates," "assesses," "evaluates," "researches," "recommends" usually describe program management, not policy-making, unless the incumbent originates substantive policy content with independent authority.

### 3. Policy-Advocating
Being officially designated to represent, promote, or defend agency/Administration policy positions to stakeholders outside the immediate decision chain (Congress, OMB, other agencies, foreign governments, industry, the public).
- Triggers on: testifying on the agency's behalf, presenting/defending agency positions in official forums, serving as a designated agency spokesperson on policy.
- Does **not** trigger on: routine peer coordination, occasional internal briefings, attending meetings without a formal presenting role, or representing the agency in litigation/administrative proceedings (a legal function, not policy advocacy).

### 4. Confidential
Serving in a close, confidential working relationship with senior officials (political appointees or senior career officials) on sensitive, pre-decisional policy matters.
- Triggers on: acting as a trusted personal advisor to political appointees/SES officials, handling pre-decisional or deliberative-process-protected materials, attending non-public policy-decision meetings, being specifically designated for access to sensitive deliberations.
- Does **not** trigger on: routine access to classified information without deliberative content, standard legal/technical advice without a close senior-official relationship, or occasional leadership-meeting attendance without a designated advisory role.

## Corroborating structural signals

These never trigger a criterion alone — they only tip a borderline duty-text finding:

| Signal | Corroborates (positive) | Neutral | Counter-signal (negative) |
|---|---|---|---|
| Supervisor/Manager level | Supervisor or Manager; Supervisor CSRA; Management Official CSRA | Non-supervisory professional/technical codes | Non-supervisory, non-managerial code |
| Position sensitivity | Critical Sensitive; Special Sensitive | — | Non-sensitive / low-sensitivity codes |
| Public trust risk level | High Risk; Moderate Risk | Low Risk | Not applicable / not set |

*(Applies to all four criteria; Public Trust is also explicitly used for Policy-Advocating and Confidential.)*

## Borderline determination guidance

When duty text uses policy-adjacent verbs ("develops," "formulates," "implements," "advises," "coordinates," "recommends") without clearly establishing independent policy authority, the AI asks:

1. Does the incumbent set agency policy on behalf of leadership, or does leadership approve what the incumbent drafts?
2. Does the incumbent exercise substantial independent discretion over policy direction, or do recommendations pass through normal review/approval?
3. Does the incumbent speak for the agency on major policy issues in official forums?
4. Are the incumbent's recommendations normally accepted with little modification (de facto policy authority)?
5. Does the incumbent resolve controversial policy matters that set agency precedent?
6. Does the incumbent have delegated authority to approve or issue agency-wide policy?

- **None of these clearly favor the position** → score as Borderline (not Yes).
- **Three or more clearly favor the position** → raise from Borderline to Medium or High.

Guiding principle: "develops," "implements," "coordinates," "assesses," "evaluates," "researches," and "recommends" typically describe program management, not policy authority.

## Overall candidacy scoring

| Candidate result | Condition |
|---|---|
| **Yes** | 2 or more criteria clearly triggered |
| **Yes** | Exactly 1 criterion clearly triggered, with direct and unambiguous duty language |
| **Borderline** | Policy-adjacent language present, but no criterion clearly triggered |
| **No** | No criterion clearly triggered |

## Confidence rating

| Rating | Candidate result | Condition |
|---|---|---|
| **High** | Yes | 2 or more criteria clearly triggered |
| **Medium** | Yes | Exactly 1 criterion clearly triggered |
| **Borderline** | Borderline | Policy-adjacent language present, no criterion clearly triggered |
| **Low** | No | No qualifying language found |

## Required documentation per position

For every scored position, the AI records:
- A 1-2 sentence objective **position purpose** summary (descriptive, not evaluative).
- The **candidate result** (Yes / Borderline / No) and **confidence rating** (High / Medium / Borderline / Low).
- A **justification summary** of 3+ sentences citing the EO and specific duty language.
- Per-criterion **triggered/not-triggered** status with a verbatim quote or explicit negative finding as evidence.
- Which specific duty statements matched which criteria.

This evidentiary record is what supports the individual per-position Word determination documents and the audit trail retained for review or litigation.
