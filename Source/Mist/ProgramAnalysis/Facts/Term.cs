using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;

namespace Mist.ProgramAnalysis.Facts;

internal sealed class Term : IEquatable<Term>
{
    private static IComparer<Value> Order { get; } = Comparer<Value>.Create((left, right) => left.Id.CompareTo(right.Id));

    public Term(IEnumerable<KeyValuePair<Value, BigInteger>> coefficients, BigInteger constant = default)
    {
        var result = ImmutableSortedDictionary.CreateBuilder<Value, BigInteger>(Order);
        foreach(var (value, coefficient) in coefficients)
        {
            result[value] = result.GetValueOrDefault(value) + coefficient;
        }

        Coefficients = result.Where(pair => !pair.Value.IsZero).ToImmutableSortedDictionary(Order);
        Constant = constant;
    }

    public Term(BigInteger constant) : this([], constant)
    {
    }

    public Term(Value value) : this([KeyValuePair.Create(value, BigInteger.One)])
    {
    }

    public ImmutableSortedDictionary<Value, BigInteger> Coefficients { get; }
    public BigInteger Constant { get; }

    public Term Add(Term other)
    {
        return new Term(Coefficients.Concat(other.Coefficients), Constant + other.Constant);
    }

    public Term Scale(BigInteger multiplier)
    {
        return new Term(Coefficients.Select(pair => KeyValuePair.Create(pair.Key, pair.Value * multiplier)), Constant * multiplier);
    }

    public Term Subtract(Term other)
    {
        return Add(other.Scale(-1));
    }

    public bool Equals(Term? other)
    {
        return other is not null && Constant == other.Constant && Coefficients.SequenceEqual(other.Coefficients);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Term);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Constant);
        foreach(var pair in Coefficients)
        {
            hash.Add(pair);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        var parts = Coefficients.Select(pair => pair.Value.IsOne ? pair.Key.DisplayName
            : string.Create(CultureInfo.InvariantCulture, $"{pair.Value} * {pair.Key.DisplayName}")).ToList();
        if(!Constant.IsZero || parts.Count == 0)
        {
            parts.Add(Constant.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(" + ", parts);
    }
}