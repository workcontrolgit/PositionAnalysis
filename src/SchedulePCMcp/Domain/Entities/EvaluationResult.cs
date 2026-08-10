using SchedulePCMcp.Domain.ValueObjects;

namespace SchedulePCMcp.Domain.Entities;

/// <summary>
/// Represents the evaluation result for a Position Description after LLM scoring
/// </summary>
public class EvaluationResult
{
    public string PdNbr { get; set; } = string.Empty;
    public OccupationalSeries Series { get; set; } = null!;
    public Grade Grade { get; set; } = null!;
    
    public decimal OverallScore { get; set; }
    public string Rating { get; set; } = string.Empty; // HIGH, MEDIUM, LOW, DOES_NOT_MEET
    public bool IsCandidate { get; set; }
    
    public List<CriterionScore> CriteriaScores { get; set; } = new();
    public string JustificationSummary { get; set; } = string.Empty;
    public string RawLlmResponse { get; set; } = string.Empty;
    
    public DateTime EvaluatedDate { get; set; }
    public string EvaluatedBy { get; set; } = "LLM";

    public override string ToString() => 
        $"[{PdNbr}] Rating={Rating}, Score={OverallScore:F1}, Candidate={IsCandidate}";
}

/// <summary>
/// Represents a single Schedule PC criterion evaluation
/// </summary>
public class CriterionScore
{
    public string CriterionName { get; set; } = string.Empty;
    public bool Triggered { get; set; }
    public string Evidence { get; set; } = string.Empty;

    public override string ToString() => $"{CriterionName}: {(Triggered ? "Triggered" : "Not triggered")}";
}
