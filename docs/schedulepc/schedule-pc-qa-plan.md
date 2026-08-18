# Schedule P/C QA Plan — Word Evaluation Form Review

**Audience:** QA analysts reviewing AI-generated Schedule P/C evaluation forms.
**Scope:** 14,120 generated Word documents (`reports/schedule-pc/form-word/*.docx`) and the matching Excel tracker.
**Model configuration:** Azure OpenAI `gpt-5.1`, `Temperature = 0.2` (see `src/PositionAnalysis.Mcp/appsettings.json`).
**References:**
- [schedule-pc-scoring-criteria.docx](schedule-pc-scoring-criteria.docx) — the 4 criteria and candidacy rules
- [word-template-field-traceability.docx](word-template-field-traceability.docx) — where every field on the form comes from (Backend/LLM/Derived/Static)
- [schedule-pc-rating-and-ai-score-color-coding.docx](schedule-pc-rating-and-ai-score-color-coding.docx) — Rating vs. AI Score


---

## 1. Goal

Confirm two things for every reviewed form:

1. **Accuracy** — every field on the Word form matches its source of truth (Oracle PD data or the model's
   own evidence quotes), per the field traceability matrix.
2. **No hallucination** — every LLM-authored statement (evidence quotes, position purpose, justification
   summary) is actually supported by the PD's duty text, not invented or overstated.

A form passes QA only if both checks pass. `Temperature = 0.2` keeps the model close to deterministic,
low-creativity output, but it does **not** eliminate hallucination risk — QA must still verify evidence
against source text on every reviewed form.

---

## 2. What is generated vs. what to verify

Per [word-template-field-traceability.docx](word-template-field-traceability.docx), fields fall into 4 categories.
QA effort should be weighted toward the categories that can be wrong in different ways:

| Source | Risk type | QA action |
|---|---|---|
| **Backend** (PD Number, Title, Org, Pay Plan/Series/Grade, Effective Date, Manager Level, Sensitivity, Public Trust) | Data-mapping bug, stale lookup table, code-to-label mismatch | Spot-check against Oracle `TEMP_PD_SCHED_PC` directly — these should be 100% exact, zero tolerance for mismatch |
| **Derived** (Bureau/Org display, code+label lookups) | Wrong lookup table entry, formatting bug | Confirm code-to-label pairs are current (e.g. Manager Level, Sensitivity, Public Trust tables in the traceability doc) |
| **LLM** (Rating, criteria triggered + evidence, Position Purpose, Justification Summary, Is Candidate) | Hallucination, misapplied criteria, unsupported evidence | Full evidence-to-duty-text check — this is the core of the QA plan (Section 4) |
| **Static** (`"Competitive"` literal, `"(Pending Human Review)"`) | Should never vary | Confirm literal text is unchanged; flag as a code defect if it varies at all |

---

## 3. Sampling strategy for 14,120 documents

Reviewing all 14,120 forms individually is not practical. Use tiered sampling:

1. **100% positive-baseline check** — all 90 PDs with human `SCHEDULE_PC_IND = 'Y'` (the confirmed-candidate
   population). These have a known-correct answer and are the highest-value check: any AI `No` result here
   is a **discrepancy requiring review**, not just a sample miss. This measures **sensitivity** (false
   negatives) only.
2. **Negative-baseline check (specificity)** — the positive baseline alone cannot catch the AI recommending
   `YES` on positions humans would call non-candidates. Work with the data owner to pull a comparably sized
   sample of PDs with a known/confirmed **non-candidate** determination (e.g. `SCHEDULE_PC_IND <> 'Y'`
   combined with any other human non-candidate source of record) and check for AI `Is Candidate = YES`
   false positives. If no confirmed negative population exists yet, flag this as an open QA-data gap rather
   than skipping the check silently.
3. **Random statistical sample** — pull a random sample sized for a 95% confidence level (e.g. ~375 forms
   for a 5% margin of error against 14,120 total) stratified by:
   - Rating (HIGH / MEDIUM / LOW) — oversample HIGH and MEDIUM since they drive the candidate recommendation.
   - Series/Grade — ensure coverage across occupational series, not just GS-15/0301-style clusters.
4. **Targeted review** — every form where:
   - `Is Candidate = YES` (these carry legal/HR consequence and should get 100% justification review before
     being finalized), or
   - AI Score is borderline (roughly 40–60), where the model itself signaled low confidence.
5. **Regression sample after any prompt, model, or template change** — re-run the same fixed sample set
   used in the prior QA pass so results are comparable release over release. Record the prompt/template
   version and model deployment name with every QA pass (see Section 7) so "the same conditions" is
   actually verifiable, not assumed.

Track sample results in the Excel tracker (filter by `Is Candidate`, `Rating`, `AI Score`, `Series`) — see
[how-to-use-schedule-pc-excel-tracker-and-word-forms.docx](../instructions/how-to-use-schedule-pc-excel-tracker-and-word-forms.docx).

### Reviewer calibration

Before a new QA analyst signs off independently, or after any material rubric change, run a calibration
round: 2+ analysts independently review the same 15–20 form sample (mixing HIGH/MEDIUM/LOW and at least a
few borderline AI Scores). Compare pass/fail calls and hallucination flags. Disagreements should be
resolved and documented before that analyst's findings count toward the release decision in Section 6.

---

## 4. Per-document QA checklist

For each document pulled into a sample, check in this order.

> **Automate what's deterministic:** Section 4A (title-stripping, code-to-label lookups) and 4C
> (rating-vs-criteria-count math) are objective and scriptable directly against the Excel tracker/Oracle
> data — script these checks and reserve manual analyst time for the subjective evidence-to-duty-text work
> in 4B/4D. Manually re-deriving arithmetic that a script can verify in bulk is a poor use of QA capacity.

### A. Identity and classification fields (Backend/Derived — zero tolerance)
- [ ] PD Number matches the source record.
- [ ] Position Title matches `PD_POSITION_TITLE_TEXT`, with the leading numeric prefix stripped
      (e.g. source `"040 - INTERAGENCY DETAIL (UNCLASSIFIED DUTIES)"` → form shows `"INTERAGENCY DETAIL
      (UNCLASSIFIED DUTIES)"`).
- [ ] Pay Plan / Series / Grade match source codes exactly.
- [ ] Bureau/Org Code display, Manager Level, Position Sensitivity, and Public Trust labels match the
      code-to-label tables in the traceability doc (not just "looks plausible").
- [ ] Effective Date is correctly parsed/formatted (or blank if the source value wasn't parseable —
      confirm it isn't silently wrong).

### B. Criteria findings (LLM — evidence-to-duty-text check)
For **each** of the 4 criteria (Policy-Determining, Policy-Making, Policy-Advocating, Confidential):
- [ ] Open the PD's duty text (from `TEMP_PD_SCHED_PC_DUTIES` or the PD source) and locate the quoted/
      paraphrased evidence on the form.
- [ ] Confirm the evidence text actually appears in (or is a faithful paraphrase of) the duty text — flag
      any quote that cannot be located verbatim or in substance. **This is the primary hallucination check.**
- [ ] Confirm the criterion definition was applied correctly per
      [schedule-pc-scoring-criteria.docx](schedule-pc-scoring-criteria.docx) — e.g. reject a Policy-Making
      "Yes" if the cited duty language is really program management ("develops," "implements,"
      "coordinates," "recommends") without independent policy authority.
- [ ] Confirm "Not Triggered" findings aren't missing an obvious criterion match elsewhere in the duty text
      (false negative check, not just false positive check).
- [ ] If structural signals (Manager Level, Sensitivity, Public Trust) were cited as corroborating a
      finding, confirm they did **not** trigger the criterion on their own — the rubric requires duty-text
      evidence as the primary basis.

### C. Rating, score, and recommendation consistency
- [ ] Criteria-triggered count matches the stated Rating (3–4 triggered → HIGH, 1–2 → MEDIUM, 0 → LOW) —
      this should always be internally consistent; any mismatch is a code defect, not a judgment call.
- [ ] `Is Candidate` (Yes/No) matches the candidacy rule: 2+ criteria triggered, or exactly 1 with clearly
      unambiguous language.
- [ ] AI Score (0–100) is directionally consistent with the strength of evidence found in step B — a HIGH
      rating with very thin/ambiguous evidence but a high AI Score is worth flagging as an overconfidence
      case.

### D. Narrative fields (LLM — hallucination and overreach check)
- [ ] **Position Purpose** — 1–2 sentence summary; confirm it's descriptive (restates what the position
      does) and not evaluative, and doesn't introduce facts absent from the PD intro/duties.
- [ ] **Justification Summary** — confirm every factual claim traces back to the PD (duty text, EO
      citation, criteria findings already shown on the form). Flag any sentence that:
  - Cites specifics (names, programs, statistics) not present anywhere in the source PD.
  - Cites the EO or statute incorrectly or in a way not supported by the actual finding.
  - Draws a conclusion stronger than what the per-criterion evidence supports.

### E. Static/template integrity
- [ ] `"Competitive"` literal appears unchanged in both Section 1 and Appendix A.
- [ ] `"(Pending Human Review)"` literal appears unchanged, and Evaluator Name/Date is left blank for
      manual entry (not pre-filled by the app).
- [ ] Appendix B duty rows are complete (one row per `PDD_SEQ_NUM`) and verbatim — no truncation or
      reordering versus the source duty records.

---

## 5. Hallucination-specific red flags

Treat any of these as an automatic fail requiring escalation, even outside the structured checklist above:

- An evidence quote that doesn't appear anywhere in the PD's duty text or intro.
- A justification summary referencing a duty, office, or program not found in the source PD.
- A criterion marked "Triggered" based solely on a structural signal (Manager Level/Sensitivity/Public
  Trust) with no supporting duty-text quote.
- Internally contradictory statements (e.g. evidence text argues against a criterion, but the checkbox says
  "Triggered").
- Numeric inconsistency between the Word form and the Excel tracker for the same PD (Rating, AI Score,
  criteria count) — indicates a data pipeline defect, not a model defect, but must still be logged.

---

## 6. Pass/fail thresholds and escalation

| Result | Action |
|---|---|
| All 90 human-baseline PDs agree with AI `Is Candidate = YES`, or disagreements are individually justified | Baseline check passes |
| Any human-baseline PD scores AI `Is Candidate = NO` | Escalate immediately — review duty text manually; log as a discrepancy regardless of root cause |
| Sample fields (Section 4A/E) mismatch source data | Code/data defect — file as a bug, not a model quality issue |
| Sample evidence (Section 4B/D) fails the evidence-to-duty-text check | Model quality issue — log the PD, criterion, and quoted text; escalate for prompt/rubric review if the pattern recurs across multiple PDs |
| Sample pass rate falls below the QA threshold — starting values: **99% field accuracy** (Section 4A/E, zero tolerance for identity/classification data), **95% evidence-support rate** (Section 4B/D). Revisit these numbers with stakeholders after the first full pass, but do not ship without a numeric gate. | Do not release the batch; re-run scoring or escalate for prompt tuning before proceeding |

**Escalation ownership:** a disagreement between a QA analyst's evidence-to-duty-text call and the AI's
finding is not self-adjudicating — route it to a second reviewer (or the QA lead) for a binding call before
logging it as a confirmed defect. Reserve stakeholder/legal escalation for patterns that recur across
multiple PDs or affect a `YES` candidacy recommendation.

---

## 7. Defect logging

For every failed check, record at minimum:
- PD Number and Word form filename.
- Section/field that failed (use the traceability doc's field names).
- Expected value/source text vs. actual value on the form.
- Category: Data mapping / Derived lookup / Model evidence / Model narrative / Template static.
- Whether it's a one-off or a recurring pattern (helps decide between a data fix and a prompt fix).
- **Batch provenance:** model deployment name, prompt/template version, and evaluation run date/ID for the
  reviewed form, so a defect can be tied back to the exact configuration that produced it.

---

## 8. Notes on model configuration

- Model: `gpt-5.1` (Azure OpenAI deployment), `Temperature = 0.2`.
- A low temperature reduces output variability run-to-run but does **not** guarantee factual grounding —
  QA must still verify evidence against duty text rather than assuming consistency implies correctness.
- If the model, prompt template, or temperature setting changes, re-run the full baseline check (Section 3,
  item 1) and the fixed regression sample (Section 3, item 4) before accepting the new configuration.

---

## 9. AI-assisted QA automation

AI can accelerate this QA process, but only for specific parts of it — it does not replace the human
reviewer, especially for any `Is Candidate = YES` result.

### Where to use automation (not AI)
Section 4A/C/E checks are deterministic (rating = f(criteria count), code→label lookups, static literals).
Script these directly against the Excel tracker and Oracle `TEMP_PD_SCHED_PC` data — no AI needed, and this
catches 100% of these mismatches with no false positives, freeing analyst time for Section 4B/D.

### Where AI can help (triage, not adjudication)
- **Tier 1 — deterministic text matching, no LLM, $0 cost.** Reuse the existing fuzzy-match logic
  (`DutySupportsCriterion` in
  [OpenXmlDocumentStrategy.cs](../../src/PositionAnalysis.Mcp/Infrastructure/DocumentGeneration/OpenXmlDocumentStrategy.cs),
  substring or ≥60% significant-word overlap) to auto-flag evidence quotes that don't appear in the duty
  text, across all 14,120 forms, before any human review or any LLM call.
- **Tier 2 — LLM-as-judge pass, only for Tier 1 failures.** A second model call (*"given this duty text and
  this quoted evidence, does the quote appear verbatim or as a faithful paraphrase? Yes/No/Partial"*) can
  catch paraphrase-level grounding issues plain text-matching misses. Only run this on the subset that
  failed Tier 1 — not the full 14,120 — to keep LLM spend proportional to the actual problem rate.

### LLM cost estimate for Tier 2
Using the rates in [llm-pricing.json](../../src/PositionAnalysis.Mcp/llm-pricing.json):

| Model | Input $/1K tokens | Output $/1K tokens | Est. cost per judge call (~500 in / ~10 out tokens) |
|---|---|---|---|
| `gpt-5.1` (current scoring model) | $0.00125 | $0.01000 | ~$0.0007 |
| `gpt-5.1-codex-mini` | $0.00025 | $0.00200 | ~$0.00015 |

Worst case (every one of the 14,120 forms × 4 criteria = 56,480 checks fails Tier 1 and needs a judge call):
~$40 on `gpt-5.1`, ~$8.50 on `gpt-5.1-codex-mini`. In practice only a fraction will fail the free Tier 1
match — at a 10% failure rate (~5,650 checks), cost drops to roughly **$4 on `gpt-5.1` or under $1 on
`gpt-5.1-codex-mini`**.

**Recommendation:** route Tier 2 judge calls to `gpt-5.1-codex-mini`, not the main scoring model. This is a
short 3-way classification task (Grounded / Paraphrased / Not-Found), not open-ended reasoning — it doesn't
need `gpt-5.1`-tier reasoning quality, and using the cheaper model keeps QA automation cost roughly an order
of magnitude lower with no expected loss in judge accuracy for this task.

### Why AI can't be the final QA gate
- **Circularity risk** — grading a model's output with the same model family risks correlated blind spots;
  a judge model can confidently agree with a hallucination it would have produced itself.
- **The judge needs its own QA** — AI-judge calls still need human spot-checking (this is what the
  calibration step in Section 3 is for).
- **Legal consequence** — every `Is Candidate = YES` result carries HR/legal weight under the EO cited in
  this plan and must stay 100% human-reviewed, regardless of how confident the AI triage is.

### Recommended workflow
1. Script the objective checks (4A/C/E) — no ambiguity, no AI.
2. Run Tier 1 evidence-grounding (fuzzy match, $0 cost) across all 14,120 forms.
3. Run Tier 2 (LLM-as-judge, on `gpt-5.1-codex-mini`) only on the Tier 1 failures to produce a "likely
   hallucination" flag and confidence score per document.
4. Human QA analysts focus review time on: the positive/negative baselines, all AI-flagged likely-
   hallucination cases, and the statistical random sample — instead of scanning all 14,120 forms cold.
5. Every `YES` candidacy recommendation still gets human sign-off regardless of the AI triage result.
