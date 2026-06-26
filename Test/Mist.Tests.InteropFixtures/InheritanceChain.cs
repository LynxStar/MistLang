namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Base class in a multi-level inheritance chain for testing inheritance projection.
/// </summary>
public class Base
{
    public int Id { get; set; }

    public Base() { }
    public Base(int id) => Id = id;

    public virtual string Describe()
    {
        return $"Base({Id})";
    }
}

/// <summary>
/// Middle class in the inheritance chain: Middle : Base.
/// </summary>
public class Middle : Base
{
    public string Label { get; set; } = "";

    public Middle() { }
    public Middle(int id, string label) : base(id) => Label = label;

    public override string Describe()
    {
        return $"Middle({Id}, {Label})";
    }
}

/// <summary>
/// Derived class in the inheritance chain: Derived : Middle : Base.
/// </summary>
public class Derived : Middle
{
    public double Score { get; set; }

    public Derived() { }
    public Derived(int id, string label, double score) : base(id, label) => Score = score;

    public override string Describe()
    {
        return $"Derived({Id}, {Label}, {Score})";
    }
}