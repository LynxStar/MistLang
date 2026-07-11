// ambient-type-provider-v2, Property 2: Alias substitution in member signatures
//
// **Validates: Requirements 3.3**
//
// For projected methods with aliased types in signatures, verify Standard Library references used.
// Uses [Fact] tests with known BCL types. No process spawning.

using Mist.Backends.CSharpTranspiler;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property test: when a projected method's CLR parameter type or return type is an aliased type,
/// the projected Method instance should have the Standard Library type rather than a newly projected type.
/// </summary>
public sealed class AliasSubstitutionInSignaturesV2Tests
{
    private readonly TypeProjectorV2 projector = new(new VisibilityFilterV2());
    private readonly ClassType standardString = new("String", "Standard", new Scope(null), null, null);
    private readonly ClassType standardInt32 = new("Int32", "Standard", new Scope(null), null, null);
    private readonly ClassType standardBoolean = new("Boolean", "Standard", new Scope(null), null, null);
    private readonly ClassType standardVoid = new("Void", "Standard", new Scope(null), null, null);

    /// <summary>
    /// Resolve callback that returns Standard Library instances for aliased types
    /// and stub types for everything else.
    /// </summary>
    private IMistType? Resolve(string ns, string name)
    {
        if(ns == "System" && name == "String")
        {
            return standardString;
        }

        if(ns == "System" && name == "Int32")
        {
            return standardInt32;
        }

        if(ns == "System" && name == "Boolean")
        {
            return standardBoolean;
        }

        if(ns == "System" && name == "Void")
        {
            return standardVoid;
        }

        if(ns == "Standard" && name == "String")
        {
            return standardString;
        }

        if(ns == "Standard" && name == "Int32")
        {
            return standardInt32;
        }

        if(ns == "Standard" && name == "Boolean")
        {
            return standardBoolean;
        }

        if(ns == "Standard" && name == "Void")
        {
            return standardVoid;
        }

        return new ClassType(name, ns, new Scope(null), null, null);
    }

    [Fact]
    public void ProjectMethods_MethodReturningString_UsesStandardStringInstance()
    {
        // object.ToString() returns System.String
        var shell = projector.CreateTypeShell(typeof(object), "System");

        projector.ProjectMethods(shell!, typeof(object), Resolve);

        var memberShell = (MemberType)shell!;
        var toString = memberShell.MethodsList.FirstOrDefault(m => m.Name == "ToString");
        Assert.NotNull(toString);
        Assert.True(
            ReferenceEquals(toString.ReturnType, standardString),
            "ToString() return type should be the exact Standard.String instance, not a new projection.");
    }

    [Fact]
    public void ProjectMethods_MethodWithInt32Parameter_UsesStandardInt32Instance()
    {
        // string.PadLeft(int totalWidth) has System.Int32 parameter
        var shell = projector.CreateTypeShell(typeof(string), "System");

        projector.ProjectMethods(shell!, typeof(string), Resolve);

        var memberShell = (MemberType)shell!;
        var padLeft = memberShell.MethodsList.FirstOrDefault(
            m => m.Name == "PadLeft" && m.ParametersList.Count == 1);
        Assert.NotNull(padLeft);
        Assert.True(
            ReferenceEquals(padLeft.ParametersList[0].Type, standardInt32),
            "PadLeft(int) parameter type should be the exact Standard.Int32 instance.");
    }

    [Fact]
    public void ProjectMethods_MethodWithBooleanReturn_UsesStandardBooleanInstance()
    {
        // string.Contains(string) returns System.Boolean
        var shell = projector.CreateTypeShell(typeof(string), "System");

        projector.ProjectMethods(shell!, typeof(string), Resolve);

        var memberShell = (MemberType)shell!;
        var contains = memberShell.MethodsList.FirstOrDefault(
            m => m.Name == "Contains" && m.ParametersList.Count == 1
                && ReferenceEquals(m.ParametersList[0].Type, standardString));
        Assert.NotNull(contains);
        Assert.True(
            ReferenceEquals(contains.ReturnType, standardBoolean),
            "Contains(string) return type should be the exact Standard.Boolean instance.");
    }

    [Fact]
    public void ProjectMethods_VoidReturningMethod_UsesStandardVoidInstance()
    {
        // List<T>.Clear() returns System.Void
        var shell = projector.CreateTypeShell(typeof(List<>), "System.Collections.Generic");

        projector.ProjectMethods(shell!, typeof(List<>), Resolve);

        var memberShell = (MemberType)shell!;
        var clear = memberShell.MethodsList.FirstOrDefault(m => m.Name == "Clear");
        Assert.NotNull(clear);
        Assert.True(
            ReferenceEquals(clear.ReturnType, standardVoid),
            "Clear() return type should be the exact Standard.Void instance.");
    }
}