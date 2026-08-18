# How To: Use the Schedule P/C Excel Tracker and Word Evaluation Forms

**Audience:** HR staff and evaluators reviewing Schedule P/C candidate results.

---

## 1) What's in this folder

```
schedule-pc/
  tracker-excel/     One Excel workbook per export run, e.g. PositionAnalysis-Eval-Tracker-2026-08-18.xlsx
  form-word/         One Word document per position, e.g. PD-123456_JOB-TITLE_GS-0301-13.docx
```

- **tracker-excel** — a summary spreadsheet listing every evaluated position, its rating, score, and key
  details. Use this file first to filter, sort, and find the positions you care about.
- **form-word** — the full Schedule P/C evaluation write-up for a single position, formatted for printing
  or filing. Each row in the Excel tracker links directly to its matching Word document.


---

## 2) Excel tracker: column reference

Columns appear in this order:

| Column | What it shows |
|---|---|
| PD Number | Position Description number |
| Word Form Filename | Clickable link to the matching Word evaluation form |
| Rating | HIGH / MEDIUM / LOW — see section 4 below |
| Criteria Met Count | How many of the 4 Schedule P/C criteria were triggered (0–4) |
| AI Score | AI confidence score, 0–100, shaded red→yellow→green |
| Is Candidate | YES/NO — whether the position is recommended as a Schedule P/C candidate |
| Confirmed Schedule PC | Human-confirmed Schedule P/C indicator from the source HR data |
| Position Title | Position title (leading position-number prefix, e.g. "040 - ", is stripped) |
| Series, Grade, Org Code, Pay Plan | Position classification details |
| Manager Level, Position Sensitivity, Public Trust, Service Category | Additional position attributes |
| Policy-Determining / Policy-Making / Policy-Advocating / Confidential (+ Evidence) | Each of the 4 criteria: TRUE/FALSE plus the supporting evidence text |
| Justification Summary | AI's written rationale for the overall recommendation |
| Eval Date | Date the position was evaluated |

The header row is frozen and the first two columns (PD Number, Word Form Filename) stay visible while you
scroll right through the evidence columns.

---

## 3) Filtering and sorting in Excel

The tracker has AutoFilter enabled on every column, so you can filter without setting anything up:

1. Open the workbook and click the dropdown arrow in any column header (e.g. **Rating**, **Is Candidate**,
   **Series**).
2. Uncheck values you don't want, or use **Text/Number Filters** for more advanced conditions (e.g.
   "AI Score greater than 70").
3. To see only likely candidates: filter **Is Candidate** to `YES`.
4. To focus on strong cases: filter **Rating** to `HIGH`, or filter **AI Score** for values above a
   threshold.
5. To review a specific occupational series or grade: filter **Series** and/or **Grade**.
6. Combine multiple column filters at once — Excel applies them together (AND logic).
7. To clear all filters, go to **Data > Clear** (or reopen the file).

You can also sort any column (e.g. sort **AI Score** descending to see the strongest cases first) using
the same dropdown's **Sort** options.

---

## 4) Navigating from Excel to the Word form

Each row's **Word Form Filename** cell is a hyperlink:

1. Filter/sort the tracker down to the position(s) you want to review.
2. Click the **Word Form Filename** cell for that row.
3. Excel opens the matching document from the `form-word` folder in Microsoft Word.
4. The Word form contains the full evaluation: position details, the 4 criteria with evidence, the overall
   rating/score, and the justification summary — ready for human sign-off.

If the link doesn't open (e.g. "file not found"), confirm the `form-word` folder has not been moved or
renamed relative to `tracker-excel` — the link is a relative path between the two folders, so both must
stay together.

---

## 5) Quick workflow summary

1. Open the latest file in `tracker-excel`.
2. Filter **Is Candidate = YES** (or filter by **Rating**, **Series**, **Grade**, etc.) to narrow the list.
3. Sort by **AI Score** to prioritize the strongest cases.
4. Click **Word Form Filename** on each row to open the full evaluation form for review/sign-off.
