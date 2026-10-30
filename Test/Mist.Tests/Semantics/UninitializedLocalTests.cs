using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// Tier 3: <c>T x;</c> declares the local, so later assignments and reads resolve.
/// <para>
/// THE FAILURE WAS NOT A DIAGNOSTIC ABOUT THE DECLARATION. An unbound declaration reports nothing
/// at all; what surfaces is MIST0296 at every LATER mention, which reads as
/// the uses being wrong rather than the declaration never having happened. Whether the local holds
/// a value before it is read is a judgement on the finished body -- the C# it becomes carries the
/// same declaration and Roslyn makes that call itself.
/// </para>
/// </summary>
public sealed class UninitializedLocalTests : ProgramModelProbe
{
    public UninitializedLocalTests()
        : base("MistUninitLocal_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    private const string DeclaredThenAssignedOnBothBranches =
        "int category; if (1 > 0) { category = 1; } else { category = 0; } var used = category;";

    [Fact]
    public void AnUninitializedDeclarationBindsAndItsUsesResolve()
    {
        Assert.Empty(BuildRun(DeclaredThenAssignedOnBothBranches)
            .Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void TheLocalIsDeclaredWithNoInitializer()
    {
        var declaration = BodyIn(DeclaredThenAssignedOnBothBranches)
            .Statements.OfType<LocalDeclarationStatement>().First();

        Assert.Equal("category", declaration.Local.Name);
        Assert.Null(declaration.Initializer);
    }
}