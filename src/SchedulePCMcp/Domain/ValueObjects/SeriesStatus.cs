namespace SchedulePCMcp.Domain.ValueObjects;

/// <summary>
/// Value object representing processing status for a specific occupational series
/// </summary>
public class SeriesStatus
{
    public OccupationalSeries Series { get; }
    public int Staged { get; }
    public int InProgress { get; }
    public int Complete { get; }
    public int Failed { get; }

    public SeriesStatus(OccupationalSeries series, int staged = 0, int inProgress = 0, int complete = 0, int failed = 0)
    {
        Series = series ?? throw new ArgumentNullException(nameof(series));
        Staged = staged;
        InProgress = inProgress;
        Complete = complete;
        Failed = failed;
    }

    public int Total => Staged + InProgress + Complete + Failed;
    
    public decimal PercentComplete => Total > 0 ? (decimal)Complete / Total * 100 : 0;
    
    public bool IsFullyComplete => Staged == 0 && InProgress == 0 && (Total == Complete);

    public override string ToString() => 
        $"{Series}: Staged={Staged}, InProgress={InProgress}, Complete={Complete}, Failed={Failed} ({PercentComplete:F1}%)";
}
