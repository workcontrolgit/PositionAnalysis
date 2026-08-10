namespace SchedulePCMcp.Domain.ValueObjects;

/// <summary>
/// Value object representing a grade supplied by Oracle
/// </summary>
public class Grade : IEquatable<Grade>
{
    public int Value { get; }

    public Grade(int value)
    {
        Value = value;
    }

    public override string ToString() => Value.ToString("D2");
    
    public override bool Equals(object? obj) => Equals(obj as Grade);
    
    public bool Equals(Grade? other) => other is not null && Value == other.Value;
    
    public override int GetHashCode() => Value.GetHashCode();
    
    public static bool operator ==(Grade? left, Grade? right) => 
        left is null ? right is null : left.Equals(right);
    
    public static bool operator !=(Grade? left, Grade? right) => !(left == right);
}
