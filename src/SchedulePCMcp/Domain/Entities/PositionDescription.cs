using SchedulePCMcp.Domain.ValueObjects;

namespace SchedulePCMcp.Domain.Entities;

/// <summary>
/// Represents a Position Description entity from the TEMP Schedule PC source
/// </summary>
public class PositionDescription
{
    public int PdSeqNum { get; set; }
    public string PdNbr { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public OccupationalSeries Series { get; set; } = null!;
    public Grade Grade { get; set; } = null!;
    public string OrganizationCode { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string BureauCode { get; set; } = string.Empty;
    public string BureauName { get; set; } = string.Empty;
    public string PayPlan { get; set; } = string.Empty;
    public string ManagerLevel { get; set; } = string.Empty;
    public string PositionSensitivity { get; set; } = string.Empty;
    public string PublicTrust { get; set; } = string.Empty;
    public string ServiceCategory { get; set; } = string.Empty;
    public string EffectiveDate { get; set; } = string.Empty;
    public string IntroText { get; set; } = string.Empty;
    public List<MajorDuty> Duties { get; set; } = new();
    public DateTime CreatedDate { get; set; }

    public override string ToString() => $"[{PdNbr}] {Title} ({Series}/{Grade}) - {OrganizationName}";
}

/// <summary>
/// Represents a major duty within a Position Description
/// </summary>
public class MajorDuty
{
    public int SequenceNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public decimal PercentTimeAllotted { get; set; }
    public bool IsCritical { get; set; }

    public override string ToString() => $"Duty {SequenceNumber}: {Text.Substring(0, Math.Min(50, Text.Length))}... ({PercentTimeAllotted}%)";
}
