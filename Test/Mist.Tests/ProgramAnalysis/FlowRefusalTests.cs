// Tier 2 (Functional): flow rules judge directly constructed bodies, storage paths and allocation sites.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FlowRefusalTests
{
    [Theory]
    [InlineData("auto-property-not-assigned", "MIST0223")]
    [InlineData("auto-property-read-through-this-before-assignment", "MIST0222")]
    [InlineData("bare-array-of-a-class", "MIST0225")]
    [InlineData("bare-array-of-a-function", "MIST0225")]
    [InlineData("bare-array-of-a-newtype", "MIST0225")]
    [InlineData("bare-array-of-a-refinement", "MIST0225")]
    [InlineData("bare-array-of-a-struct-with-a-reference-field", "MIST0225")]
    [InlineData("bare-array-of-a-struct-with-an-inherited-reference-field", "MIST0225")]
    [InlineData("bare-array-of-a-type-parameter", "MIST0225")]
    [InlineData("bare-array-of-an-enum", "MIST0225")]
    [InlineData("bare-array-of-string", "MIST0225")]
    [InlineData("field-assigned-only-inside-lambda", "MIST0223")]
    [InlineData("field-never-assigned", "MIST0223")]
    [InlineData("field-not-assigned-on-all-paths", "MIST0223")]
    [InlineData("field-read-through-this-after-partial-assignment", "MIST0222")]
    [InlineData("field-read-through-this-before-assignment", "MIST0222")]
    [InlineData("field-read-through-this-inside-loop", "MIST0222")]
    [InlineData("local-assigned-on-one-branch", "MIST0220")]
    [InlineData("local-assigned-only-in-for", "MIST0220")]
    [InlineData("local-assigned-only-in-foreach", "MIST0220")]
    [InlineData("local-assigned-only-in-while", "MIST0220")]
    [InlineData("local-assigned-only-inside-async-block", "MIST0220")]
    [InlineData("local-assigned-only-inside-lambda", "MIST0220")]
    [InlineData("local-captured-before-assignment", "MIST0220")]
    [InlineData("local-function-called-before-capture-assigned", "MIST0221")]
    [InlineData("local-function-called-through-another-before-capture-assigned", "MIST0221")]
    [InlineData("local-function-named-before-capture-assigned", "MIST0221")]
    [InlineData("local-function-reads-its-own-local-before-assignment", "MIST0220")]
    [InlineData("local-read-after-loop-body-declares-it", "MIST0220")]
    [InlineData("local-read-before-assignment", "MIST0220")]
    [InlineData("local-read-in-own-initializer", "MIST0220")]
    [InlineData("local-read-through-interpolation", "MIST0220")]
    [InlineData("stackalloc-of-a-class", "MIST0226")]
    [InlineData("stackalloc-of-a-function", "MIST0226")]
    [InlineData("stackalloc-of-a-struct-with-a-reference-field", "MIST0226")]
    [InlineData("stackalloc-of-a-type-parameter", "MIST0226")]
    [InlineData("stampable-declaration-with-reference-storage", "MIST0224")]
    [InlineData("stampable-generic-declaration-without-a-bound", "MIST0224")]
    [InlineData("struct-field-read-through-this-before-assignment", "MIST0222")]
    [InlineData("struct-local-auto-property-needs-whole-receiver", "MIST0220")]
    [InlineData("struct-local-auto-property-write-needs-whole-receiver", "MIST0220")]
    [InlineData("struct-local-declaration-initializer-does-not-assign", "MIST0220")]
    [InlineData("struct-local-different-fields-on-each-branch", "MIST0220")]
    [InlineData("struct-local-field-assigned-on-one-branch", "MIST0220")]
    [InlineData("struct-local-field-assigned-only-in-for", "MIST0220")]
    [InlineData("struct-local-field-assigned-only-in-foreach", "MIST0220")]
    [InlineData("struct-local-field-assigned-only-in-lambda", "MIST0220")]
    [InlineData("struct-local-field-assigned-only-in-while", "MIST0220")]
    [InlineData("struct-local-field-before-any-assignment", "MIST0220")]
    [InlineData("struct-local-field-reset-on-loop-entry", "MIST0220")]
    [InlineData("struct-local-generic-nested-unassigned-sibling", "MIST0220")]
    [InlineData("struct-local-generic-nested-whole-copy", "MIST0220")]
    [InlineData("struct-local-inherited-partial-whole-copy", "MIST0220")]
    [InlineData("struct-local-inherited-unassigned-field", "MIST0220")]
    [InlineData("struct-local-nested-parent-unassigned-sibling", "MIST0220")]
    [InlineData("struct-local-nested-partial-whole-copy", "MIST0220")]
    [InlineData("struct-local-nested-sibling-captured-by-lambda", "MIST0220")]
    [InlineData("struct-local-nested-sibling-local-function-call", "MIST0221")]
    [InlineData("struct-local-nested-unassigned-read-modify-write-target", "MIST0220")]
    [InlineData("struct-local-nested-unassigned-sibling", "MIST0220")]
    [InlineData("struct-local-partial-whole-argument", "MIST0220")]
    [InlineData("struct-local-partial-whole-copy", "MIST0220")]
    [InlineData("struct-local-partial-whole-return", "MIST0220")]
    [InlineData("struct-local-reference-write-does-not-assign-sibling", "MIST0220")]
    [InlineData("struct-local-static-write-does-not-assign-instance", "MIST0220")]
    [InlineData("struct-local-unassigned-opaque-ambient-value", "MIST0220")]
    [InlineData("struct-local-unassigned-read-modify-write-target", "MIST0220")]
    [InlineData("struct-local-unassigned-reference-field-write", "MIST0220")]
    [InlineData("struct-local-unassigned-sibling-captured-by-async", "MIST0220")]
    [InlineData("struct-local-unassigned-sibling-captured-by-lambda", "MIST0220")]
    [InlineData("struct-local-unassigned-sibling-local-function-call", "MIST0221")]
    [InlineData("struct-local-unassigned-sibling-local-function-reference", "MIST0221")]
    [InlineData("struct-local-unassigned-sibling-transitive-local-function", "MIST0221")]
    [InlineData("struct-local-unassigned-sibling", "MIST0220")]
    public void RefusedFragmentKeepsItsDiagnostic(string caseName, string expected)
    {
        var diagnostic = Assert.Single(new FlowFragments().Analyze(caseName));

        Assert.True(diagnostic.Number.Value == expected, $"{caseName}: expected {expected}, got {diagnostic.Number.Value}");
    }

    [Fact]
    public void MissingMemberDoesNotAddADefiniteAssignmentComplaint()
    {
        Assert.Empty(new FlowFragments().Analyze("struct-local-missing-member-does-not-cascade"));
    }

    [Theory]
    [InlineData("local-read-before-assignment")]
    [InlineData("field-never-assigned")]
    [InlineData("field-read-through-this-before-assignment")]
    [InlineData("local-function-called-before-capture-assigned")]
    [InlineData("bare-array-of-a-class")]
    [InlineData("stackalloc-of-a-class")]
    public void RefusalPointsAtTheReadDeclarationOrAllocationThatNeedsChanging(string site)
    {
        var diagnostic = Assert.Single(new FlowFragments().Analyze(site));

        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(38, diagnostic.Position().Start.Column);
    }
}