namespace SchedulePCMcp.Domain.ValueObjects;

/// <summary>
/// Value object representing filter criteria for staging position descriptions
/// </summary>
public class StagingFilter
{
    public Grade? GradeMin { get; set; }
    public Grade? GradeMax { get; set; }
    public OccupationalSeries? Series { get; set; }
    public string? OrganizationCode { get; set; }

    public StagingFilter() { }

    public StagingFilter(Grade? gradeMin, Grade? gradeMax, OccupationalSeries? series, string? orgCode)
    {
        if (gradeMin != null && gradeMax != null && gradeMin.Value > gradeMax.Value)
            throw new ArgumentException("GradeMin cannot be greater than GradeMax");

        GradeMin = gradeMin;
        GradeMax = gradeMax;
        Series = series;
        OrganizationCode = orgCode;
    }

    public bool IsEmpty => GradeMin == null && GradeMax == null && Series == null && OrganizationCode == null;

    public override string ToString()
    {
        var parts = new List<string>();
        if (GradeMin != null) parts.Add($"GradeMin={GradeMin}");
        if (GradeMax != null) parts.Add($"GradeMax={GradeMax}");
        if (Series != null) parts.Add($"Series={Series}");
        if (OrganizationCode != null) parts.Add($"Org={OrganizationCode}");
        
        return parts.Any() ? string.Join(", ", parts) : "(no filters)";
    }
}
