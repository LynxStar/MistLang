using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

public sealed class CarrierNameTests
{
    private static ClassType Named(string name)
    {
        return new ClassType(name, "App", new Scope(null), null, null);
    }

    [Fact]
    public void PlainReceiver_KeepsItsName()
    {
        Assert.Equal("GreetingExtensions_Program_0", CarrierNames.For(Named("Greeting"), "/x/Program.mist", 0));
    }

    [Fact]
    public void IndexDistinguishesBlocksInOneFile()
    {
        Assert.Equal("GreetingExtensions_Program_2", CarrierNames.For(Named("Greeting"), "/x/Program.mist", 2));
    }

    [Fact]
    public void ArrayReceiver_LosesItsBrackets()
    {
        var name = CarrierNames.For(Named("Array<,>"), "/x/Program.mist", 0);

        Assert.Equal("ArrayExtensions_Program_0", name);
        Assert.DoesNotContain('<', name);
        Assert.DoesNotContain(',', name);
    }

    [Fact]
    public void GenericReceiver_LosesItsArguments()
    {
        var name = CarrierNames.For(Named("Box<T>"), "/x/Lib.mist", 1);

        Assert.DoesNotContain('<', name);
        Assert.DoesNotContain('>', name);
    }

    /// <summary>
    /// The bracket cases above are carried by StripArity, which truncates at the first angle bracket.
    /// Any other illegal character survives that, so the reduction has to be a filter; removing it
    /// leaves the bracket tests passing.
    /// </summary>
    [Fact]
    public void ReceiverNameKeepsOnlyIdentifierCharacters()
    {
        Assert.Equal("OuterInnerExtensions_Lib_0", CarrierNames.For(Named("Outer.Inner"), "/x/Lib.mist", 0));
    }

    [Fact]
    public void FileStem_IsSanitisedToo()
    {
        var name = CarrierNames.For(Named("Greeting"), "/x/my-file name.mist", 0);

        Assert.Equal("GreetingExtensions_myfilename_0", name);
    }

    [Fact]
    public void NoSourcePath_StillProducesAnIdentifier()
    {
        Assert.Equal("GreetingExtensions_X_0", CarrierNames.For(Named("Greeting"), null, 0));
    }
}