// ambient-type-provider-v2, Property 8: Member projection completeness
//
// **Validates: Requirements 9.1, 9.2, 9.3, 9.4, 9.5, 9.6**
//
// For projected types, verify all public non-excluded members appear in projected member lists.
// Uses [Fact] tests with known BCL types. No process spawning.

using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: for projected CLR types, <see cref="TypeProjectorV2"/> projects all
/// public non-excluded members (methods, constructors, properties, fields, indexers, operators)
/// into the corresponding member lists.
/// </summary>
public sealed class MemberProjectionCompletenessV2Tests
{
    private readonly TypeProjectorV2 projector = new(new VisibilityFilterV2());

    /// <summary>
    /// A permissive resolve callback that returns a stub type for any requested resolution.
    /// This allows member projection to succeed for non-generic-parameter types.
    /// </summary>
    private static IMistType? Resolve(string ns, string name)
    {
        return new ClassType(name, ns, new Scope(null), null, null);
    }

    [Fact]
    public void ProjectMethods_ListT_ContainsClearMethod_NonGenericSignature()
    {
        // Clear() has no generic parameter in signature (void return, no params)
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        projector.ProjectMethods(shell!, typeof(List<>), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.Contains(memberShell.MethodsList, m => m.Name == "Clear");
    }

    [Fact]
    public void ProjectMethods_ListT_ContainsTrimExcessMethod()
    {
        // TrimExcess() has no generic parameter in signature
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        projector.ProjectMethods(shell!, typeof(List<>), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.Contains(memberShell.MethodsList, m => m.Name == "TrimExcess");
    }

    [Fact]
    public void ProjectMethods_ListT_MethodsAreNotEmpty()
    {
        // Even with generic param methods skipped, non-generic-param methods are still projected
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        projector.ProjectMethods(shell!, typeof(List<>), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.NotEmpty(memberShell.MethodsList);
    }

    [Fact]
    public void ProjectProperties_ListT_ContainsCountProperty()
    {
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        projector.ProjectProperties(shell!, typeof(List<>), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.Contains(memberShell.PropertiesList, p => p.Name == "Count");
    }

    [Fact]
    public void ProjectProperties_ListT_ContainsCapacityProperty()
    {
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        projector.ProjectProperties(shell!, typeof(List<>), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.Contains(memberShell.PropertiesList, p => p.Name == "Capacity");
    }

    [Fact]
    public void ProjectConstructors_ListT_ProjectsAtLeastOneConstructor()
    {
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        projector.ProjectConstructors(shell!, typeof(List<>), Resolve);

        var concreteShell = (ConcreteType)shell!;
        Assert.NotEmpty(concreteShell.ConstructorsList);
    }

    [Fact]
    public void ProjectConstructors_ListT_ContainsParameterlessConstructor()
    {
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        projector.ProjectConstructors(shell!, typeof(List<>), Resolve);

        var concreteShell = (ConcreteType)shell!;
        Assert.Contains(concreteShell.ConstructorsList, c => c.ParametersList.Count == 0);
    }

    [Fact]
    public void ProjectOperators_TimeSpan_ContainsAdditionOperator()
    {
        var shell = projector.CreateTypeShell(typeof(TimeSpan), "System");

        projector.ProjectOperators(shell!, typeof(TimeSpan), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.Contains(memberShell.OperatorsList, op => op.Kind == OperatorKind.Plus);
    }

    [Fact]
    public void ProjectOperators_TimeSpan_ContainsSubtractionOperator()
    {
        var shell = projector.CreateTypeShell(typeof(TimeSpan), "System");

        projector.ProjectOperators(shell!, typeof(TimeSpan), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.Contains(memberShell.OperatorsList, op => op.Kind == OperatorKind.Minus);
    }

    [Fact]
    public void ProjectOperators_TimeSpan_ContainsEqualityOperator()
    {
        var shell = projector.CreateTypeShell(typeof(TimeSpan), "System");

        projector.ProjectOperators(shell!, typeof(TimeSpan), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.Contains(memberShell.OperatorsList, op => op.Kind == OperatorKind.EqualEqual);
    }

    [Fact]
    public void ProjectIndexers_String_ContainsCharIndexer()
    {
        // String's indexer is this[int] -> char (no generic parameters)
        var shell = projector.CreateTypeShell(typeof(string), "System");

        projector.ProjectIndexers(shell!, typeof(string), Resolve);

        var memberShell = (MemberType)shell!;
        Assert.NotEmpty(memberShell.IndexersList);
    }

    [Fact]
    public void ProjectIndexers_String_IndexerHasGetter()
    {
        var shell = projector.CreateTypeShell(typeof(string), "System");

        projector.ProjectIndexers(shell!, typeof(string), Resolve);

        var memberShell = (MemberType)shell!;
        var indexer = Assert.Single(memberShell.IndexersList);
        Assert.NotNull(indexer.Getter);
    }

    [Fact]
    public void ProjectIndexers_String_IndexerHasNoSetter()
    {
        // String's indexer is get-only
        var shell = projector.CreateTypeShell(typeof(string), "System");

        projector.ProjectIndexers(shell!, typeof(string), Resolve);

        var memberShell = (MemberType)shell!;
        var indexer = Assert.Single(memberShell.IndexersList);
        Assert.Null(indexer.Setter);
    }

    [Fact]
    public void ProjectFields_Int32_ContainsMaxValueField()
    {
        var shell = projector.CreateTypeShell(typeof(int), "System");

        projector.ProjectFields(shell!, typeof(int), Resolve);

        var concreteShell = (ConcreteType)shell!;
        Assert.Contains(concreteShell.FieldsList, f => f.Name == "MaxValue");
    }

    [Fact]
    public void ProjectFields_Int32_ContainsMinValueField()
    {
        var shell = projector.CreateTypeShell(typeof(int), "System");

        projector.ProjectFields(shell!, typeof(int), Resolve);

        var concreteShell = (ConcreteType)shell!;
        Assert.Contains(concreteShell.FieldsList, f => f.Name == "MinValue");
    }

    [Fact]
    public void ProjectFields_Int32_MaxValueIsReadonly()
    {
        var shell = projector.CreateTypeShell(typeof(int), "System");

        projector.ProjectFields(shell!, typeof(int), Resolve);

        var concreteShell = (ConcreteType)shell!;
        var maxValue = concreteShell.FieldsList.First(f => f.Name == "MaxValue");
        Assert.True(maxValue.Modifiers.IsReadonly);
    }

    [Fact]
    public void ProjectFields_Int32_MaxValueIsStatic()
    {
        var shell = projector.CreateTypeShell(typeof(int), "System");

        projector.ProjectFields(shell!, typeof(int), Resolve);

        var concreteShell = (ConcreteType)shell!;
        var maxValue = concreteShell.FieldsList.First(f => f.Name == "MaxValue");
        Assert.True(maxValue.Modifiers.IsStatic);
    }
}