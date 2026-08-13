# Schedule P/C Batch Scoring — Cost & Time Estimate

_Generated 2026-08-13 from live Serilog output (`schedulepcmcp-20260813.log`), Azure OpenAI `gpt-5.1` deployment at `https://devops-cicd.openai.azure.us/`._

## Sample runs observed

| Run | Series | PDs scored | Failed | Total cost | Avg cost/PD | Total time | Avg time/PD |
|---|---|---|---|---|---|---|---|
| 1 | 00855 | 13 | 0 | $0.1611 | $0.0124 | 129.5 s | 9.96 s |
| 2 | 01301 | 60 | 0 | $0.8583 | $0.0143 | 749.8 s | 12.50 s |
| **Combined** | — | **73** | **0** | **$1.0194** | **$0.0140** | **879.3 s** | **12.05 s** |

Per-PD cost across both runs ranged from $0.0062 (E09764, smallest prompt) to $0.0253 (S19681, largest prompt: 5,488 prompt + 1,848 completion tokens).

## Extrapolation to full table (14,120 evaluation results)

Using the combined 73-PD sample average as the estimate basis:

| Metric | Per PD | × 14,120 PDs |
|---|---|---|
| Cost | $0.0140 | **≈ $197** |
| Time (sequential, single-threaded) | 12.05 s | **≈ 170,146 s ≈ 47.3 hours (~2.0 days)** |

### Sensitivity range

Using the lower/higher of the two individual run averages instead of the blended average:

| Basis | Cost/PD | Est. total cost (14,120) | Time/PD | Est. total time (14,120) |
|---|---|---|---|---|
| Run 1 (00855, low end) | $0.0124 | ~$175 | 9.96 s | ~39.1 hrs |
| Run 2 (01301, larger sample) | $0.0143 | ~$202 | 12.50 s | ~49.0 hrs |
| **Blended (recommended)** | **$0.0140** | **~$197** | **12.05 s** | **~47.3 hrs** |

## Caveats

- Sample size is 73 of 14,120 PDs (~0.5%). Cost varies meaningfully by position complexity/description length (observed per-PD cost spans ~4x), so the estimate carries real variance.
- This assumes **all** 14,120 rows still need scoring. In practice many rows in that table are likely already scored from prior runs, so the actual remaining cost/time to fully process the backlog is probably lower.
- Time estimate assumes strictly sequential single-series processing as observed in these runs; no parallelism was used.
- Azure OpenAI pricing/token rates are assumed constant at current `gpt-5.1` deployment rates; any pricing change would shift the estimate proportionally.
