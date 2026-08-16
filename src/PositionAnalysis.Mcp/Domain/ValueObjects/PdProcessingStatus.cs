namespace PositionAnalysis.Mcp.Domain.ValueObjects;

/// <summary>
/// Processing status for an individual PD.
/// </summary>
public record PdProcessingStatus(
    string PdNbr,
    string Series,
    string Status,
    string Rating,
    string Title = "",
    string OrgCode = "",
    string PayPlan = "",
    string Grade = "",
    int CriteriaMet = 0,
    decimal Score = 0m);
