# Schedule P/C Rating Scale and AI Score Color Coding

**Audience:** HR evaluators reviewing generated Word evaluation documents and Excel trackers.
**Source of truth:** `src/PositionAnalysis.Mcp/Application/Services/ScoringOrchestrator.cs`

This document explains what the **HIGH / MEDIUM / LOW rating**, the **AI Score**, and the **colors** on your evaluation documents mean, and how they relate to each other.

---

## 1. The Rating (HIGH / MEDIUM / LOW)

The rating is **not** something the AI freely decides. It is calculated directly in code from the number of the four Schedule P/C criteria the AI found to be triggered for a position:

- Policy-Determining
- Policy-Making
- Policy-Advocating
- Confidential

| Criteria triggered (out of 4) | Rating |
|---|---|
| 3 or 4 | **HIGH** |
| 1 or 2 | **MEDIUM** |
| 0 | **LOW** |

Because the rating is a direct, deterministic function of the criteria-met count, **the rating and the "X of 4 criteria met" label will always agree** — you will never see, for example, "HIGH -- 1 of 4 criteria met" or "MEDIUM -- 0 of 4 criteria met."


---

## 2. The AI Score (0–100)

Separate from the rating, the AI also returns a numeric **confidence score from 0 to 100** for each position, shown as `AI Score: NN/100` next to the rating.

**What the AI Score means:** it is the AI's own estimate of how strongly the duty text supports its criteria findings — think of it as "how confident/strong is the case," not "how many criteria were met." Two positions can have the exact same rating (e.g. both MEDIUM) and criteria-met count, but different AI Scores, because one position's duty language may be clearer and more direct evidence than the other's.

**Important:** the AI Score does **not** determine the rating. The rating is fixed by the criteria-met count (see section 1). The AI Score is purely a supplementary signal so an evaluator can tell, at a glance, whether a rating sits at the strong end or the weak end of its bucket — e.g. a MEDIUM at 65/100 is a stronger case than a MEDIUM at 40/100, even though both are labeled MEDIUM.

---

## 3. Color coding

Colors on both the Word document rating cell and the Excel export are driven by the **AI Score**, using a continuous red → yellow → green gradient (not a flat color per rating bucket):

| AI Score | Color |
|---|---|
| 0 | Red (`#F8696B`) |
| 50 | Yellow (`#FFEB84`) |
| 100 | Green (`#63BE7B`) |

Scores between these points are blended proportionally. This means:
- A score of 82 will render a strong green.
- A score of 45 will render an orange/amber shade (between red and yellow).
- A score of 10 will render a strong red.

Because the gradient is continuous, two positions with the **same rating** (e.g. both HIGH) can still show **visibly different shades** depending on their AI Score — a HIGH at 95 will look noticeably greener than a HIGH at 78.

### Where you'll see this
- **Word document** — Section 1 "Schedule PC Rating" cell: label reads `RATING -- N of 4 criteria met (AI Score: NN/100)`, shaded per the AI Score.
- **Excel export** — two separate columns:
  - **Rating** — text bucket (HIGH/MEDIUM/LOW).
  - **AI Score** — the raw 0–100 number, shaded with the same gradient.

---

## 4. Quick reference for evaluators

| What you see | What it tells you |
|---|---|
| Rating (HIGH/MEDIUM/LOW) | How many of the 4 criteria were triggered — the official bucket used for candidacy determination. |
| "N of 4 criteria met" | The exact count driving the rating above; always consistent with the rating. |
| AI Score (0–100) | How strong/confident the AI's evidence was for its findings — a secondary strength indicator, not a criteria count. |
| Cell color | A visual proxy for the AI Score (red=weak, yellow=middling, green=strong) — use it to spot the stronger vs. weaker cases within the same rating bucket. |
