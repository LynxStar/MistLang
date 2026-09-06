using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// The name an extension block's carrier class is emitted under.
/// <para>
/// Sanitisation is the whole point and it is not cosmetic. A receiver's model name carries whatever
/// the type system calls it, and several of those are not C# identifiers. Emitted verbatim, C#
/// re-parses the angle brackets as a generic class declaration, so a carrier for a
/// <c>Box&lt;T&gt;</c> receiver collides with the user's own <c>Box&lt;T&gt;</c> and reports errors
/// against types they DID write; an array receiver cascades into unbalanced braces. The repo already
/// contains such a receiver -- samples/FirstClassFunctions declares <c>extension(int[] self)</c>.
/// </para>
/// </summary>
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

    /// <summary>
    /// The array case, which is the one present in the repo today.
    /// </summary>
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
    /// The two bracket cases above are carried by StripArity, which truncates at the first angle
    /// bracket. This one is not: a name whose ILLEGAL character is anything else survives that and
    /// reaches the identifier, so the reduction has to be a filter rather than a truncation. Pinned
    /// separately because removing the filter leaves the two tests above passing.
    /// </summary>
    [Fact]
    public void ReceiverNameKeepsOnlyIdentifierCharacters()
    {
        Assert.Equal("OuterInnerExtensions_Lib_0", CarrierNames.For(Named("Outer.Inner"), "/x/Lib.mist", 0));
    }

    /// <summary>
    /// A file name is as untrusted as a type name: it reaches the identifier too, and a dash or a
    /// space in it is just as illegal.
    /// </summary>
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