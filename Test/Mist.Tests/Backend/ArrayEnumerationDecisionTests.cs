using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ArrayEnumerationDecisionTests
{
    [Fact]
    public void AUserArrayWithItsOwnEnumeratorKeepsOrdinaryTypeEmission()
    {
        var array = new ClassType("Array<,>", "Probe", null, null, null);
        var enumerator = new StructType("Enumerator", "Probe", array.Scope, array, null);
        var enclosing = new ConstructedType(array, [SizedSequenceTypes.StringType, SizedSequenceTypes.IntegerType]);
        var nested = new ConstructedType(enumerator, [], enclosing);

        Assert.Null(ArrayEnumeration.Emit(nested, _ => "string"));
    }

    [Fact]
    public void TheStandardArrayEnumeratorUsesTheEnclosingElementType()
    {
        var element = SizedSequenceTypes.StringType;
        var enclosing = SizedSequenceTypes.ArrayOf(element, 3);
        var array = Assert.IsType<ClassType>(enclosing.Definition);
        var enumerator = new StructType("Enumerator", "Standard", array.Scope, array, null);
        var nested = new ConstructedType(enumerator, [], enclosing);

        var emitted = ArrayEnumeration.Emit(nested, type =>
        {
            Assert.Same(element, type);
            return "string";
        });

        Assert.Equal("MistArrayEnumerator<string>", emitted);
    }
}