using PositionAnalysis.Mcp.Domain.Enums;

namespace PositionAnalysis.Mcp.Domain.Entities;

/// <summary>
/// Represents metadata for a staging/evaluation run
/// </summary>
public class RunMetadata
{
    public string RunId { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public int TotalPdCount { get; set; }
    
    public DateTime? StagingCompletedDate { get; set; }
    public int StagedPdCount { get; set; }
    
    public DateTime? ScoringStartedDate { get; set; }
    public DateTime? ScoringCompletedDate { get; set; }
    public int ScoredPdCount { get; set; }
    public int FailedScoringCount { get; set; }
    
    public DateTime? ExportedDate { get; set; }
    public bool WasExported { get; set; }

    public TimeSpan? StagingDuration => StagingCompletedDate.HasValue 
        ? StagingCompletedDate.Value - CreatedDate 
        : null;

    public TimeSpan? ScoringDuration => ScoringCompletedDate.HasValue 
        ? ScoringCompletedDate.Value - (ScoringStartedDate ?? CreatedDate)
        : null;

    public override string ToString() => 
        $"Run {RunId}: {StagedPdCount} staged, {ScoredPdCount} scored, {FailedScoringCount} failed";
}
