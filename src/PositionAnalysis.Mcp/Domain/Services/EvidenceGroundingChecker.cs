using System.Text.RegularExpressions;

namespace PositionAnalysis.Mcp.Domain.Services;

/// <summary>
/// Deterministic (non-LLM) text-grounding check: does a quoted/paraphrased evidence string
/// actually appear in a duty text? Shared between Word document generation (duty attribution,
/// Appendix B) and QA Tier 1 automated evidence-grounding checks.
/// </summary>
public static class EvidenceGroundingChecker
{
    /// <summary>
    /// Returns true if <paramref name="evidence"/> appears verbatim in <paramref name="dutyText"/>,
    /// or if at least 60% of its significant (5+ letter) words appear in the duty text.
    /// </summary>
    public static bool IsSupported(string dutyText, string evidence)
    {
        if (string.IsNullOrWhiteSpace(dutyText) || string.IsNullOrWhiteSpace(evidence)) return false;

        var normalizedDuty     = Normalize(dutyText);
        var normalizedEvidence = Normalize(evidence);
        if (normalizedEvidence.Length == 0) return false;

        if (normalizedDuty.Contains(normalizedEvidence, StringComparison.OrdinalIgnoreCase))
            return true;

        // Evidence quotes may be lightly paraphrased; require most significant words to appear in the duty text.
        var evidenceWords = normalizedEvidence.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 5)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (evidenceWords.Length == 0) return false;

        var matchCount = evidenceWords.Count(w => normalizedDuty.Contains(w, StringComparison.OrdinalIgnoreCase));
        return matchCount / (double)evidenceWords.Length >= 0.6;
    }

    public static string Normalize(string text) => Regex.Replace(text, @"[^\w\s]", "").Trim();
}
