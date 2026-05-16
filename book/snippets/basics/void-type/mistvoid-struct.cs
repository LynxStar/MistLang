public readonly struct MistVoid : IEquatable<MistVoid>
{
    public static readonly MistVoid Instance = default;
    public bool Equals(MistVoid other) => true;
    public override bool Equals(object? obj) => obj is MistVoid;
    public override int GetHashCode() => 0;
    public static bool operator ==(MistVoid left, MistVoid right) => true;
    public static bool operator !=(MistVoid left, MistVoid right) => false;
    public override string ToString() => "Void";
}
