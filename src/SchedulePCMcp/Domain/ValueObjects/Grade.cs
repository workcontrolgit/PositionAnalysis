namespace SchedulePCMcp.Domain.ValueObjects;

/// <summary>
/// Value object representing a GS grade (1-15)
/// </summary>
public class Grade : IEquatable<Grade>
{
    public int Value { get; }

    public Grade(int value)
    {
        if (value < 1 || value > 15)
            throw new ArgumentException($"Grade must be between 1 and 15, got {value}", nameof(value));

        Value = value;
    }

    public override string ToString() => $"GS-{Value:D2}";
    
    public override bool Equals(object? obj) => Equals(obj as Grade);
    
    public bool Equals(Grade? other) => other is not null && Value == other.Value;
    
    public override int GetHashCode() => Value.GetHashCode();
    
    public static bool operator ==(Grade? left, Grade? right) => 
        left is null ? right is null : left.Equals(right);
    
    public static bool operator !=(Grade? left, Grade? right) => !(left == right);
}
