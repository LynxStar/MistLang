using System.Reflection;
using System.Runtime.CompilerServices;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

public sealed class ClrValueTupleTests
{
    [Fact]
    public void NameSlots_MatchesTheNameCountTheCompilerWroteForEverySignature()
    {
        var named = Signatures().Where(signature => signature.Names is not null).ToList();

        Assert.True(named.Count >= 12, $"Only {named.Count} named signatures in the fixture.");

        foreach(var (label, type, names) in named)
        {
            Assert.True(
                ClrValueTuple.NameSlots(type) == names!.Count,
                $"{label}: counted {ClrValueTuple.NameSlots(type)} slots, the compiler wrote {names.Count}.");
        }
    }

    [Fact]
    public void Cardinality_FlattensTheRestAndRefusesWhatIsNotATuple()
    {
        Assert.Equal(2, ClrValueTuple.Cardinality(ReturnOf(nameof(TupleSignatures.MakePair))));
        Assert.Equal(8, ClrValueTuple.Cardinality(ReturnOf(nameof(TupleSignatures.Octet))));
        Assert.Equal(9, ClrValueTuple.Cardinality(ReturnOf(nameof(TupleSignatures.Nonet))));
        Assert.Equal(16, ClrValueTuple.Cardinality(ReturnOf(nameof(TupleSignatures.Sixteen))));
        Assert.Equal(1, ClrValueTuple.Cardinality(ReturnOf(nameof(TupleSignatures.Single))));

        Assert.Null(ClrValueTuple.Cardinality(typeof(ValueTuple<,>)));
        Assert.Null(ClrValueTuple.Cardinality(typeof(ValueTuple<int, int, int, int, int, int, int, int>)));
        Assert.Null(ClrValueTuple.Cardinality(typeof(KeyValuePair<int, int>)));
        Assert.Null(ClrValueTuple.Cardinality(typeof(int)));
    }

    [Fact]
    public void Of_DropsNamesThatDoNotFitTheType()
    {
        var divide = typeof(TupleSignatures).GetMethod(nameof(TupleSignatures.Divide))!;

        Assert.NotNull(TupleElementNames.Of(divide.ReturnParameter, divide.ReturnType));
        Assert.Null(TupleElementNames.Of(divide.ReturnParameter, ReturnOf(nameof(TupleSignatures.Octet))));
    }

    private static Type ReturnOf(string method)
    {
        return typeof(TupleSignatures).GetMethod(method)!.ReturnType;
    }

    private static IEnumerable<(string Label, Type Type, IList<string?>? Names)> Signatures()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach(var method in typeof(TupleSignatures).GetMethods(flags).Where(method => !method.IsSpecialName))
        {
            yield return ($"{method.Name} result", method.ReturnType, NamesOn(method.ReturnParameter));

            foreach(var parameter in method.GetParameters())
            {
                yield return ($"{method.Name}({parameter.Name})", parameter.ParameterType, NamesOn(parameter));
            }
        }

        foreach(var property in typeof(TupleSignatures).GetProperties(flags))
        {
            yield return (property.Name, property.PropertyType, NamesOn(property));
        }

        foreach(var field in typeof(TupleSignatures).GetFields(flags))
        {
            yield return (field.Name, field.FieldType, NamesOn(field));
        }
    }

    private static IList<string?>? NamesOn(ICustomAttributeProvider owner)
    {
        return owner.GetCustomAttributes(typeof(TupleElementNamesAttribute), inherit: false)
            .Cast<TupleElementNamesAttribute>()
            .SingleOrDefault()?
            .TransformNames;
    }
}