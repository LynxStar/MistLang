using Mist.Backends.CSharpTranspiler.Razor;

namespace Mist.Tests.Backend.Razor;

[Trait("Tier", "1")]
public sealed class ClrTypeTextTests
{
    public static TheoryData<Type, string> Rows => new()
    {
        { typeof(int), "int" },
        { typeof(string), "string" },
        { typeof(decimal), "decimal" },
        { typeof(int?), "int?" },
        { typeof(string[]), "string[]" },
        { typeof(int[,]), "int[,]" },
        { typeof(Dictionary<int, string>.KeyCollection), "System.Collections.Generic.Dictionary<int, string>.KeyCollection" },
        { typeof(Dictionary<,>.KeyCollection), "System.Collections.Generic.Dictionary<TKey, TValue>.KeyCollection" },
        { typeof(List<int>.Enumerator), "System.Collections.Generic.List<int>.Enumerator" },
        { typeof(Box<int>.Middle<string>.Item<bool>), "Mist.Tests.Backend.Razor.ClrTypeTextTests.Box<int>.Middle<string>.Item<bool>" },
        { typeof(Dictionary<int, string>.KeyCollection[]), "System.Collections.Generic.Dictionary<int, string>.KeyCollection[]" },
        { typeof(DateTime), "System.DateTime" },
        { typeof(List<string>), "System.Collections.Generic.List<string>" },
        { typeof(Dictionary<string, int?>), "System.Collections.Generic.Dictionary<string, int?>" },
        { typeof(Environment.SpecialFolder), "System.Environment.SpecialFolder" },
        { typeof(List<>).GetGenericArguments()[0], "T" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void AClrTypeIsSpelledAsMistWritesIt(Type type, string expected)
    {
        Assert.Equal(expected, ClrTypeText.Of(type));
    }

    [Fact]
    public void AGenericDefinitionIsNamedWithoutItsArity()
    {
        Assert.Equal("System.Collections.Generic.Dictionary", ClrTypeText.Name(typeof(Dictionary<,>)));
    }

    internal sealed class Box<T>
    {
        public sealed class Middle<TMiddle>
        {
            public sealed class Item<TItem>;
        }
    }
}