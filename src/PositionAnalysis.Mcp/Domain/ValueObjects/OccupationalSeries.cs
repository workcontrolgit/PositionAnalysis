namespace PositionAnalysis.Mcp.Domain.ValueObjects;

/// <summary>
/// Value object representing a 5-digit occupational series code (e.g., 00110)
/// </summary>
public class OccupationalSeries : IEquatable<OccupationalSeries>
{
    public string Code { get; }

    public OccupationalSeries(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Series code cannot be empty", nameof(code));

        code = code.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"^\d{5}$"))
            throw new ArgumentException($"Series code must be 5 digits, got '{code}'", nameof(code));

        Code = code;
    }

    public override string ToString() => Code;
    
    public override bool Equals(object? obj) => Equals(obj as OccupationalSeries);
    
    public bool Equals(OccupationalSeries? other) => other is not null && Code == other.Code;
    
    public override int GetHashCode() => Code.GetHashCode();
    
    public static bool operator ==(OccupationalSeries? left, OccupationalSeries? right) => 
        left is null ? right is null : left.Equals(right);
    
    public static bool operator !=(OccupationalSeries? left, OccupationalSeries? right) => !(left == right);
}
