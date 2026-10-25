// Tier 2 (Functional): flow rules judge directly constructed bodies, storage paths and allocation sites.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class FlowRefusalTests
{
    [Theory]
    [InlineData("auto-property-not-assigned", "MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED")]
    [InlineData("auto-property-read-through-this-before-assignment", "MIST_PA_FIELD_READ_BEFORE_ASSIGNMENT")]
    [InlineData("bare-array-of-a-class", "MIST_PA_ARRAY_REQUIRES_INITIALIZER")]
    [InlineData("bare-array-of-a-function", "MIST_PA_ARRAY_REQUIRES_INITIALIZER")]
    [InlineData("bare-array-of-a-newtype", "MIST_PA_ARRAY_REQUIRES_INITIALIZER")]
    [InlineData("bare-array-of-a-refinement", "MIST_PA_ARRAY_REQUIRES_INITIALIZER")]
    [InlineData("bare-array-of-a-struct-with-a-reference-field", "MIST_PA_ARRAY_REQUIRES_INITIALIZER")]
    [InlineData("bare-array-of-a-struct-with-an-inherited-reference-field", "MIST_PA_ARRAY_REQUIRES_INITIALIZER")]
    [InlineData("bare-array-of-a-type-parameter", "MIST_PA_ARRAY_REQUIRES_INITIALIZER")]
    [InlineData("bare-array-of-an-enum", "MIST_PA_ARRAY_REQUIRES_INITIALIZER")]
    [InlineData("bare-array-of-string", "MIST_PA_ARRAY_REQUIRES_INITIALIZER")]
    [InlineData("field-assigned-only-inside-lambda", "MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED")]
    [InlineData("field-never-assigned", "MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED")]
    [InlineData("field-not-assigned-on-all-paths", "MIST_PA_FIELD_NOT_DEFINITELY_ASSIGNED")]
    [InlineData("field-read-through-this-after-partial-assignment", "MIST_PA_FIELD_READ_BEFORE_ASSIGNMENT")]
    [InlineData("field-read-through-this-before-assignment", "MIST_PA_FIELD_READ_BEFORE_ASSIGNMENT")]
    [InlineData("field-read-through-this-inside-loop", "MIST_PA_FIELD_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-assigned-on-one-branch", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-assigned-only-in-for", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-assigned-only-in-foreach", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-assigned-only-in-while", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-assigned-only-inside-async-block", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-assigned-only-inside-lambda", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-captured-before-assignment", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-function-called-before-capture-assigned", "MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED")]
    [InlineData("local-function-called-through-another-before-capture-assigned", "MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED")]
    [InlineData("local-function-named-before-capture-assigned", "MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED")]
    [InlineData("local-function-reads-its-own-local-before-assignment", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-read-after-loop-body-declares-it", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-read-before-assignment", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-read-in-own-initializer", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("local-read-through-interpolation", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("stackalloc-of-a-class", "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE")]
    [InlineData("stackalloc-of-a-function", "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE")]
    [InlineData("stackalloc-of-a-struct-with-a-reference-field", "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE")]
    [InlineData("stackalloc-of-a-type-parameter", "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE")]
    [InlineData("stampable-declaration-with-reference-storage", "MIST_PA_STAMPABLE_DECLARATION_NOT_SATISFIED")]
    [InlineData("stampable-generic-declaration-without-a-bound", "MIST_PA_STAMPABLE_DECLARATION_NOT_SATISFIED")]
    [InlineData("struct-field-read-through-this-before-assignment", "MIST_PA_FIELD_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-auto-property-needs-whole-receiver", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-auto-property-write-needs-whole-receiver", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-declaration-initializer-does-not-assign", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-different-fields-on-each-branch", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-field-assigned-on-one-branch", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-field-assigned-only-in-for", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-field-assigned-only-in-foreach", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-field-assigned-only-in-lambda", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-field-assigned-only-in-while", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-field-before-any-assignment", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-field-reset-on-loop-entry", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-generic-nested-unassigned-sibling", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-generic-nested-whole-copy", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-inherited-partial-whole-copy", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-inherited-unassigned-field", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-nested-parent-unassigned-sibling", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-nested-partial-whole-copy", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-nested-sibling-captured-by-lambda", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-nested-sibling-local-function-call", "MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED")]
    [InlineData("struct-local-nested-unassigned-read-modify-write-target", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-nested-unassigned-sibling", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-partial-whole-argument", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-partial-whole-copy", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-partial-whole-return", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-reference-write-does-not-assign-sibling", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-static-write-does-not-assign-instance", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-unassigned-opaque-ambient-value", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-unassigned-read-modify-write-target", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-unassigned-reference-field-write", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-unassigned-sibling-captured-by-async", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-unassigned-sibling-captured-by-lambda", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
    [InlineData("struct-local-unassigned-sibling-local-function-call", "MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED")]
    [InlineData("struct-local-unassigned-sibling-local-function-reference", "MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED")]
    [InlineData("struct-local-unassigned-sibling-transitive-local-function", "MIST_PA_LOCAL_FUNCTION_READS_UNASSIGNED")]
    [InlineData("struct-local-unassigned-sibling", "MIST_PA_LOCAL_READ_BEFORE_ASSIGNMENT")]
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

        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }
}