using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;

namespace Mist.ProgramAnalysis.Facts;

internal sealed class Term : IEquatable<Term>
{
    private static IComparer<Value> Order { get; } = Comparer<Value>.Create((left, right) => left.Id.CompareTo(right.Id));
    private readonly int hashCode;

    public Term(IEnumerable<KeyValuePair<Value, BigInteger>> coefficients, BigInteger constant = default)
        : this(Combined(coefficients), constant)
    {
    }

    public Term(BigInteger constant) : this(([], []), constant)
    {
    }

    public Term(Value value) : this(([value], [BigInteger.One]), BigInteger.Zero)
    {
    }

    private Term((ImmutableArray<Value> Variables, ImmutableArray<BigInteger> Multipliers) sum, BigInteger constant)
    {
        Variables = sum.Variables;
        Multipliers = sum.Multipliers;
        Constant = constant;
        var hash = new HashCode();
        hash.Add(Constant);
        for(var i = 0; i < Variables.Length; i++)
        {
            hash.Add(KeyValuePair.Create(Variables[i], Multipliers[i]));
        }

        hashCode = hash.ToHashCode();
    }

    private static (ImmutableArray<Value>, ImmutableArray<BigInteger>) Combined(IEnumerable<KeyValuePair<Value, BigInteger>> coefficients)
    {
        var pairs = coefficients.ToList();
        pairs.Sort((left, right) => left.Key.Id.CompareTo(right.Key.Id));
        var variables = ImmutableArray.CreateBuilder<Value>(pairs.Count);
        var multipliers = ImmutableArray.CreateBuilder<BigInteger>(pairs.Count);
        for(var i = 0; i < pairs.Count;)
        {
            var value = pairs[i].Key;
            var sum = BigInteger.Zero;
            for(; i < pairs.Count && ReferenceEquals(pairs[i].Key, value); i++)
            {
                sum += pairs[i].Value;
            }

            if(!sum.IsZero)
            {
                variables.Add(value);
                multipliers.Add(sum);
            }
        }

        return (variables.DrainToImmutable(), multipliers.DrainToImmutable());
    }

    public ImmutableSortedDictionary<Value, BigInteger> Coefficients => field ??= Variables.Zip(Multipliers, KeyValuePair.Create).ToImmutableSortedDictionary(Order);
    public BigInteger Constant { get; }
    public ImmutableArray<Value> Variables { get; }
    public ImmutableArray<BigInteger> Multipliers { get; }

    public BigInteger Multiplier(Value value)
    {
        var index = Variables.IndexOf(value);
        return index < 0 ? BigInteger.Zero : Multipliers[index];
    }

    public Term Add(Term other)
    {
        var variables = ImmutableArray.CreateBuilder<Value>(Variables.Length + other.Variables.Length);
        var multipliers = ImmutableArray.CreateBuilder<BigInteger>(Variables.Length + other.Variables.Length);
        int mine = 0, theirs = 0;
        while(mine < Variables.Length || theirs < other.Variables.Length)
        {
            var order = mine == Variables.Length ? 1 : theirs == other.Variables.Length ? -1 : Variables[mine].Id.CompareTo(other.Variables[theirs].Id);
            var value = order <= 0 ? Variables[mine] : other.Variables[theirs];
            var sum = (order <= 0 ? Multipliers[mine++] : BigInteger.Zero) + (order >= 0 ? other.Multipliers[theirs++] : BigInteger.Zero);
            if(!sum.IsZero)
            {
                variables.Add(value);
                multipliers.Add(sum);
            }
        }

        return new Term((variables.DrainToImmutable(), multipliers.DrainToImmutable()), Constant + other.Constant);
    }

    public Term Scale(BigInteger multiplier)
    {
        return multiplier.IsZero ? new Term(BigInteger.Zero)
            : new Term((Variables, [.. Multipliers.Select(coefficient => coefficient * multiplier)]), Constant * multiplier);
    }

    public Term Subtract(Term other)
    {
        return Add(other.Scale(-1));
    }

    public bool Equals(Term? other)
    {
        return ReferenceEquals(this, other) || (other is not null && hashCode == other.hashCode && Constant == other.Constant
            && Variables.SequenceEqual(other.Variables) && Multipliers.SequenceEqual(other.Multipliers));
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Term);
    }

    public override int GetHashCode()
    {
        return hashCode;
    }

    public override string ToString()
    {
        var parts = Pairs().Select(pair => pair.Value.IsOne ? pair.Key.DisplayName
            : string.Create(CultureInfo.InvariantCulture, $"{pair.Value} * {pair.Key.DisplayName}")).ToList();
        if(!Constant.IsZero || parts.Count == 0)
        {
            parts.Add(Constant.ToString(CultureInfo.InvariantCulture));
        }

        return string.Join(" + ", parts);
    }

    private IEnumerable<KeyValuePair<Value, BigInteger>> Pairs()
    {
        return Variables.Zip(Multipliers, KeyValuePair.Create);
    }
}