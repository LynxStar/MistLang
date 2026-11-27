using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class FunctionParameterOrderInterningTests
{
    [Fact]
    public void FunctionTypesAreInternedByOrderedParameterIdentity()
    {
        var interner = new TypeInterner();
        var integer = new ClassType("Int32", "Standard", null, null, null);
        var text = new ClassType("String", "Standard", null, null, null);
        var flag = new ClassType("Boolean", "Standard", null, null, null);

        var integerThenText = interner.GetOrCreateFunctionType([integer, text], flag);
        var textThenInteger = interner.GetOrCreateFunctionType([text, integer], flag);

        Assert.NotSame(integerThenText, textThenInteger);
        Assert.Same(integerThenText, interner.GetOrCreateFunctionType([integer, text], flag));
        Assert.Same(textThenInteger, interner.GetOrCreateFunctionType([text, integer], flag));

        Assert.Collection(
            integerThenText.ParameterTypes,
            parameter => Assert.Same(integer, parameter),
            parameter => Assert.Same(text, parameter));
        Assert.Collection(
            textThenInteger.ParameterTypes,
            parameter => Assert.Same(text, parameter),
            parameter => Assert.Same(integer, parameter));
        Assert.Same(flag, integerThenText.ReturnType);
        Assert.Same(flag, textThenInteger.ReturnType);
    }
}