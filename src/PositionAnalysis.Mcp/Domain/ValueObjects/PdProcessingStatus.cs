namespace PositionAnalysis.Mcp.Domain.ValueObjects;

/// <summary>
/// Processing status for an individual PD.
/// </summary>
public record PdProcessingStatus(string PdNbr, string Series, string Status, string Rating);
