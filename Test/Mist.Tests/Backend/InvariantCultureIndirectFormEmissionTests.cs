// Tier 3 (Component): purity Requirement 13.2 for the forms that reach a culture-reading member C# chooses rather
// than one Mist names -- a method group, a conditional access, a list whose type is not written List, a CLR value's
// own ToString, a Console argument C# converts to a number, or to a char, which it writes as a char, and a list of
// newtype clones, for which C# finds no order at all. Built by hand and handed to the body emitter; no parser, no
// Roslyn, no process.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class InvariantCultureIndirectFormEmissionTests
{
    private const string Provider = "System.Globalization.CultureInfo.InvariantCulture";

    private static readonly MemberModifiers Instance = new() { Accessibility = Accessibility.Public, IsIntrinsic = true };

    private static readonly MemberModifiers Static = new() { Accessibility = Accessibility.Public, IsIntrinsic = true, IsStatic = true };

    private readonly HandBuiltModel model = new();

    private readonly ClassType text;

    private readonly ClassType real;

    private readonly ClassType list;

    private readonly Method sort;

    public InvariantCultureIndirectFormEmissionTests()
    {
        text = model.Type("String");
        real = model.Type("Double");
        list = new ClassType("List<>", "Standard", new Scope(null), null, null);
        list.TypeParametersList.Add(new TypeParameter("T", 0, list.Scope, list));
        sort = new Method("Sort", model.Type("Void"), [], Instance) { DeclaringType = list };
    }

    [Fact]
    public void AMethodGroupOfCaseMapping_CapturesItsReceiverAndMapsInvariantly()
    {
        var upper = new Method("ToUpper", text, [], Instance) { DeclaringType = text };

        Assert.Equal(
            "((System.Func<string, System.Func<string>>)(__receiver => () => __receiver.ToUpperInvariant()))(s)",
            Emit(Group(Local("s", text), upper, [])));
    }

    [Fact]
    public void AMethodGroupOfAConversionFromText_NamesTheInvariantCulture()
    {
        var parse = new Method("ToInt32", model.Type("Int32"), [new Parameter("value", text)], Static) { DeclaringType = model.Type("Convert") };

        Assert.Equal(
            $"((System.Func<string, int>)((__arg0) => System.Convert.ToInt32(__arg0, {Provider})))",
            Emit(Group(null, parse, [text])));
    }

    [Fact]
    public void AMethodGroupOfSort_OrdersTextInvariantly()
    {
        var words = model.Interner.GetOrCreateConstructedType(list, [text]);

        Assert.EndsWith(
            ", System.Action>)(__receiver => () => __receiver.Sort(System.StringComparer.InvariantCulture)))(words)",
            Emit(Group(Local("words", words), sort, [])),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMethodGroupOfAFullMatch_IsTheRespelledCall()
    {
        var regex = model.Type("Regex");
        var full = new Method("IsFullMatch", model.Type("Boolean"), [new Parameter("input", text)], Instance) { DeclaringType = regex };
        var emitted = Emit(Group(Local("r", regex), full, [text]));

        Assert.StartsWith("((System.Func<System.Text.RegularExpressions.Regex, System.Func<string, bool>>)(__receiver => (__arg0) => ", emitted, StringComparison.Ordinal);
        Assert.Contains("System.Text.RegularExpressions.Regex.IsMatch(__arg0, ", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain(".IsFullMatch", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AMethodGroupOfANumbersOwnToString_FormatsInvariantly()
    {
        Assert.Equal(
            $"((System.Func<double, System.Func<string>>)(__receiver => () => __receiver.ToString({Provider})))(d)",
            Emit(Group(Local("d", real), Universal(), [])));
    }

    [Fact]
    public void AConditionalToStringOfAnOptionalNumber_NamesTheInvariantCulture()
    {
        Assert.Equal($"d?.ToString({Provider})", Emit(ConditionalText(Local("d", Optional(real)))));
    }

    [Fact]
    public void AConditionalToStringOfAnOptionalTypeParameter_KeepsAbsenceAsNull()
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);

        Assert.Equal("MistInvariant.Optional(value)", Emit(ConditionalText(Local("value", Optional(parameter)))));
    }

    [Fact]
    public void AConditionalToStringOfOptionalText_IsLeftAsWritten()
    {
        Assert.Equal("w?.ToString()", Emit(ConditionalText(Local("w", Optional(text)))));
    }

    [Fact]
    public void SortOnASubclassOfAListOfText_OrdersTextInvariantly()
    {
        var words = HandBuiltModel.Class("Words", model.Interner.GetOrCreateConstructedType(list, [text]));

        Assert.Equal("words.Sort(System.StringComparer.InvariantCulture)", Emit(Sort(Local("words", words))));
    }

    [Fact]
    public void SortOnAGenericSubclassClosedOverText_OrdersTextInvariantly()
    {
        var bag = HandBuiltModel.Class("Bag<>");
        var element = new TypeParameter("T", 0, bag.Scope, bag);
        bag.TypeParametersList.Add(element);
        bag.BaseType = model.Interner.GetOrCreateConstructedType(list, [element]);

        Assert.Equal(
            "bag.Sort(System.StringComparer.InvariantCulture)",
            Emit(Sort(Local("bag", model.Interner.GetOrCreateConstructedType(bag, [text])))));
    }

    [Fact]
    public void SortOnATypeParameterBoundToAListOfText_OrdersTextInvariantly()
    {
        var bounded = new TypeParameter("L", 0, new Scope(null), null);
        bounded.ConstraintsList.Add(new TypeConstraint(model.Interner.GetOrCreateConstructedType(list, [text])));

        Assert.Equal("words.Sort(System.StringComparer.InvariantCulture)", Emit(Sort(Local("words", bounded))));
    }

    [Fact]
    public void SortOnAListOfATextClone_OrdersTheBackingTextInvariantly()
    {
        var labels = model.Interner.GetOrCreateConstructedType(list, [Clone(HandBuiltModel.Class("Label"), text)]);

        Assert.Equal(
            "labels.Sort(MistInvariant.ByValue<Probe.Label, string>(static item => item.Value, System.StringComparer.InvariantCulture))",
            Emit(Sort(Local("labels", labels))));
    }

    [Fact]
    public void SortOnAListOfANumericClone_OrdersByTheBackingValue()
    {
        var lengths = model.Interner.GetOrCreateConstructedType(list, [Clone(HandBuiltModel.Struct("Meters"), real)]);

        Assert.Equal(
            "lengths.Sort(MistInvariant.ByValue<Probe.Meters, double>(static item => item.Value, System.Collections.Generic.Comparer<double>.Default))",
            Emit(Sort(Local("lengths", lengths))));
    }

    [Fact]
    public void ConsoleOutputOfANewtypeOverANumber_FormatsTheNumberInvariantly()
    {
        var meters = HandBuiltModel.Struct("Meters");
        _ = HandBuiltModel.Conversion(meters, meters, real);

        Assert.Equal($"WriteLine(((double)m).ToString({Provider}))", Emit(WriteLine(Local("m", meters))));
    }

    [Fact]
    public void ConsoleOutputOfAnIntegralClone_FormatsTheConversionCSharpWouldPick()
    {
        var delta = HandBuiltModel.Struct("Delta");

        foreach(var target in new[] { "Int64", "Single", "Double", "Int32" })
        {
            _ = HandBuiltModel.Conversion(delta, delta, model.Type(target));
        }

        Assert.Equal($"WriteLine(((int)d).ToString({Provider}))", Emit(WriteLine(Local("d", delta))));
    }

    [Fact]
    public void ConsoleOutputOfAnEnumOverANumber_FormatsItsValueInvariantly()
    {
        var temperature = HandBuiltModel.Enum("Temperature", real);
        temperature.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);

        Assert.Equal($"WriteLine(((double)t).ToString({Provider}))", Emit(WriteLine(Local("t", temperature))));
    }

    [Fact]
    public void ConsoleOutputOfAClrEnum_IsLeftAsWritten()
    {
        var day = HandBuiltModel.Enum("DayOfWeek", model.Type("Int32"));
        day.Assembly = MistAssembly.Ambient;

        Assert.Equal("WriteLine(d)", Emit(WriteLine(Local("d", day))));
    }

    [Theory]
    [InlineData("Byte Int32", "int")]
    [InlineData("Int32 Int64", "int")]
    [InlineData("Byte Int64", "byte")]
    [InlineData("Single Double", "float")]
    public void ConsoleOutputOfATypeWithSeveralNumericConversions_FormatsTheOneCSharpPicks(string targets, string picked)
    {
        Assert.Equal($"WriteLine((({picked})b).ToString({Provider}))", Emit(WriteLine(Local("b", Converting(targets)))));
    }

    [Theory]
    [InlineData("Double Decimal")]
    [InlineData("Single Decimal")]
    [InlineData("Int16 UInt16")]
    [InlineData("Int32 Boolean")]
    public void ConsoleOutputOfATypeWhoseConversionsCSharpFindsAmbiguous_IsLeftAsWritten(string targets)
    {
        Assert.Equal("WriteLine(b)", Emit(WriteLine(Local("b", Converting(targets)))));
    }

    [Fact]
    public void ConsoleOutputOfAGenericTypeConvertingToItsArgument_FormatsTheClosedNumber()
    {
        var box = HandBuiltModel.Class("Box<>");
        box.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        var element = new TypeParameter("T", 0, box.Scope, box);
        box.TypeParametersList.Add(element);
        _ = HandBuiltModel.Conversion(box, box, element);

        Assert.Equal(
            $"WriteLine(((double)b).ToString({Provider}))",
            Emit(WriteLine(Local("b", model.Interner.GetOrCreateConstructedType(box, [real])))));
    }

    [Fact]
    public void ConsoleOutputOfAStructWithNoNumericConversion_IsLeftAsWritten()
    {
        var point = HandBuiltModel.Struct("Point");
        point.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);

        Assert.Equal("WriteLine(p)", Emit(WriteLine(Local("p", point))));
    }

    [Fact]
    public void ConsoleOutputOfAChar_IsLeftAsWritten()
    {
        Assert.Equal("WriteLine(c)", Emit(WriteLine(Local("c", Character()))));
    }

    [Fact]
    public void ConsoleOutputOfANewtypeOverAChar_IsLeftAsWritten()
    {
        var letter = HandBuiltModel.Struct("Letter");
        letter.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        letter.CloneSource = Character();

        foreach(var target in new[] { "Int32", "Double" })
        {
            _ = HandBuiltModel.Conversion(letter, letter, model.Type(target));
        }

        Assert.Equal("WriteLine(l)", Emit(WriteLine(Local("l", letter))));
    }

    [Fact]
    public void ConsoleOutputOfATypeConvertingToACharAndANumber_IsLeftAsWritten()
    {
        var glyph = HandBuiltModel.Struct("Glyph");
        glyph.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        _ = HandBuiltModel.Conversion(glyph, glyph, model.Type("Int32"));
        _ = HandBuiltModel.Conversion(glyph, glyph, Character());

        Assert.Equal("WriteLine(g)", Emit(WriteLine(Local("g", glyph))));
    }

    [Fact]
    public void AnExplicitToStringOnAClrValue_FormatsThroughTheInvariantHelper()
    {
        var (moment, toString) = ClrValue(stackOnly: false);

        Assert.Equal("MistInvariant.Text(moment)", Emit(Call(Local("moment", moment), toString)));
    }

    [Fact]
    public void AMethodGroupOfAClrValuesToString_FormatsThroughTheInvariantHelper()
    {
        var (moment, toString) = ClrValue(stackOnly: false);
        var emitted = Emit(Group(Local("moment", moment), toString, []));

        Assert.StartsWith("((System.Func<", emitted, StringComparison.Ordinal);
        Assert.EndsWith(", System.Func<string>>)(__receiver => () => MistInvariant.Text(__receiver)))(moment)", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitToStringOnAClrRefStruct_IsLeftAsWritten()
    {
        var (reader, toString) = ClrValue(stackOnly: true);

        Assert.Equal("reader.ToString()", Emit(Call(Local("reader", reader), toString)));
    }

    private static LocalReferenceExpression Local(string name, IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, type));
    }

    private static string Emit(Expression expression)
    {
        var diagnostics = new DiagnosticBag();
        var emitted = BodyEmitter.Render(expression, diagnostics);

        Assert.Empty(diagnostics.ToImmutableArray());
        return emitted;
    }

    private ClassType Character()
    {
        var character = model.Type("Char");

        if(character.CastOperatorsList.Count == 0)
        {
            character.Modifiers = Instance;

            foreach(var target in new[] { "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Single", "Double", "Decimal" })
            {
                _ = HandBuiltModel.Conversion(character, character, model.Type(target), isIntrinsic: true);
            }
        }

        return character;
    }

    private StructType Converting(string targets)
    {
        var converting = HandBuiltModel.Struct("Both");
        converting.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);

        foreach(var target in targets.Split(' '))
        {
            _ = HandBuiltModel.Conversion(converting, converting, model.Type(target));
        }

        return converting;
    }

    private static ConcreteType Clone(ConcreteType clone, ClassType source)
    {
        source.Modifiers = Instance;
        clone.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        clone.CloneSource = source;

        return clone;
    }

    private (StructType Type, Method Formats) ClrValue(bool stackOnly)
    {
        var type = new StructType(stackOnly ? "Utf8Reader" : "DateTime", "System", new Scope(null), null, null)
        {
            Assembly = MistAssembly.Ambient,
            Modifiers = new MemberModifiers { Accessibility = Accessibility.Public, IsRef = stackOnly },
        };

        return (type, new Method("ToString", text, [], new MemberModifiers { Accessibility = Accessibility.Public }) { DeclaringType = type });
    }

    private static InvocationExpression Call(Expression receiver, Method method)
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType);

        return new InvocationExpression(HandBuiltModel.Node(), access, [], method.ReturnType, method, null);
    }

    private Method Universal()
    {
        return new Method("ToString", text, [], new MemberModifiers { Accessibility = Accessibility.Public })
        {
            DeclaringBlock = new ExtensionBlock("self", "Standard", new Scope(null), null),
        };
    }

    private FunctionReferenceExpression Group(Expression? receiver, Method method, IMistType[] parameters)
    {
        return new FunctionReferenceExpression(
            HandBuiltModel.Node(), receiver, method, model.Interner.GetOrCreateFunctionType(parameters, method.ReturnType));
    }

    private InvocationExpression ConditionalText(Expression receiver)
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "ToString", Optional(text), isConditional: true);

        return new InvocationExpression(HandBuiltModel.Node(), access, [], Optional(text), Universal(), null);
    }

    private InvocationExpression Sort(Expression receiver)
    {
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Sort", sort.ReturnType);

        return new InvocationExpression(HandBuiltModel.Node(), access, [], sort.ReturnType, sort, null);
    }

    private InvocationExpression WriteLine(Expression argument)
    {
        var console = model.Type("Console");
        var writeLine = new Method("WriteLine", model.Type("Void"), [new Parameter("value", UnknownType.Instance)], Static) { DeclaringType = console };

        return new InvocationExpression(HandBuiltModel.Node(), new MethodReferenceExpression(HandBuiltModel.Node(), writeLine), [argument], writeLine.ReturnType, writeLine, null);
    }

    private IMistType Optional(IMistType present)
    {
        return model.Interner.GetOrCreateUnionType([present, model.Type("Void")]);
    }
}