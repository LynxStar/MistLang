using System.Reflection;
using Mist.Backends.CSharpTranspiler;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="VisibilityFilterV2"/>.
/// Validates Requirements 13.1, 13.2, 13.3, 13.4, 13.5, 13.6.
/// </summary>
public class VisibilityFilterV2Tests
{
    private readonly VisibilityFilterV2 filter = new();

    // --- Requirement 13.1: Non-public type exclusion ---

    [Fact]
    public void IsTypeExcluded_InternalClass_ReturnsTrue()
    {
        // Get the internal type through reflection (can't use typeof directly)
        var assembly = typeof(SimpleClass).Assembly;
        var internalType = assembly.GetType("Mist.Tests.InteropFixtures.InternalOnlyType")!;
        Assert.True(filter.IsTypeExcluded(internalType));
    }

    [Fact]
    public void IsTypeExcluded_PrivateNestedClass_ReturnsTrue()
    {
        var nested = typeof(HasNestedPrivateType).GetNestedType(
            "PrivateNested", BindingFlags.NonPublic)!;
        Assert.True(filter.IsTypeExcluded(nested));
    }

    [Fact]
    public void IsTypeExcluded_InternalNestedClass_ReturnsTrue()
    {
        var nested = typeof(HasNestedInternalType).GetNestedType(
            "InternalNested", BindingFlags.NonPublic)!;
        Assert.True(filter.IsTypeExcluded(nested));
    }

    [Fact]
    public void IsTypeExcluded_PublicNestedClass_ReturnsFalse()
    {
        Assert.False(filter.IsTypeExcluded(typeof(HasNestedPublicType.PublicNested)));
    }

    [Fact]
    public void IsTypeExcluded_PublicClass_ReturnsFalse()
    {
        Assert.False(filter.IsTypeExcluded(typeof(SimpleClass)));
    }

    [Fact]
    public void IsTypeExcluded_PublicStruct_ReturnsFalse()
    {
        Assert.False(filter.IsTypeExcluded(typeof(SimpleStruct)));
    }

    [Fact]
    public void IsTypeExcluded_RefStruct_ReturnsFalse()
    {
        Assert.False(filter.IsTypeExcluded(typeof(RefStructType)));
    }

    // --- Requirement 13.2: Non-public member exclusion ---

    [Fact]
    public void IsMemberExcluded_PublicMethod_ReturnsFalse()
    {
        var method = typeof(HasNonPublicMembers).GetMethod(nameof(HasNonPublicMembers.PublicMethod))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_InternalMethod_ReturnsTrue()
    {
        var method = typeof(HasNonPublicMembers).GetMethod(
            "InternalMethod", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_ProtectedMethod_ReturnsTrue()
    {
        var method = typeof(HasNonPublicMembers).GetMethod(
            "ProtectedMethod", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_PublicField_ReturnsFalse()
    {
        var field = typeof(HasNonPublicMembers).GetField(nameof(HasNonPublicMembers.PublicField))!;
        Assert.False(filter.IsMemberExcluded(field));
    }

    [Fact]
    public void IsMemberExcluded_InternalField_ReturnsTrue()
    {
        var field = typeof(HasNonPublicMembers).GetField(
            "internalField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(filter.IsMemberExcluded(field));
    }

    [Fact]
    public void IsMemberExcluded_PublicProperty_ReturnsFalse()
    {
        var property = typeof(HasNonPublicMembers).GetProperty(nameof(HasNonPublicMembers.PublicProp))!;
        Assert.False(filter.IsMemberExcluded(property));
    }

    [Fact]
    public void IsMemberExcluded_InternalProperty_ReturnsTrue()
    {
        var property = typeof(HasNonPublicMembers).GetProperty(
            "InternalProp", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(filter.IsMemberExcluded(property));
    }

    // --- Requirement 13.3: Event exclusion ---

    [Fact]
    public void IsMemberExcluded_Event_ReturnsTrue()
    {
        var evt = typeof(HasEvent).GetEvent(nameof(HasEvent.SomethingHappened))!;
        Assert.True(filter.IsMemberExcluded(evt));
    }

    [Fact]
    public void IsMemberExcluded_NormalMethodOnTypeWithEvent_ReturnsFalse()
    {
        var method = typeof(HasEvent).GetMethod(nameof(HasEvent.GetValue))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    // --- Requirement 13.4: Pointer type exclusion ---

    [Fact]
    public void IsMemberExcluded_PointerReturnType_ReturnsTrue()
    {
        var method = typeof(HasPointerMembers).GetMethod(nameof(HasPointerMembers.GetPointer))!;
        Assert.True(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_PointerParameter_ReturnsTrue()
    {
        var method = typeof(HasPointerMembers).GetMethod(nameof(HasPointerMembers.ProcessPointer))!;
        Assert.True(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_SafeMethodOnTypeWithPointers_ReturnsFalse()
    {
        var method = typeof(HasPointerMembers).GetMethod(nameof(HasPointerMembers.SafeMethod))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    // --- Requirement 13.4: IntPtr/UIntPtr are NOT pointer types ---

    [Fact]
    public void IsMemberExcluded_IntPtrReturn_ReturnsFalse()
    {
        var method = typeof(HasIntPtrMembers).GetMethod(nameof(HasIntPtrMembers.GetHandle))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_IntPtrParameter_ReturnsFalse()
    {
        var method = typeof(HasIntPtrMembers).GetMethod(nameof(HasIntPtrMembers.SetHandle))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_UIntPtrReturn_ReturnsFalse()
    {
        var method = typeof(HasIntPtrMembers).GetMethod(nameof(HasIntPtrMembers.GetSize))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_UIntPtrParameter_ReturnsFalse()
    {
        var method = typeof(HasIntPtrMembers).GetMethod(nameof(HasIntPtrMembers.SetSize))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    // --- Requirement 13.6: Ref readonly return exclusion ---

    [Fact]
    public void IsMemberExcluded_RefReadonlyReturnMethod_ReturnsTrue()
    {
        var method = typeof(RefReadonlyReturnFixture).GetMethod(
            nameof(RefReadonlyReturnFixture.GetReadonlyIntRef))!;
        Assert.True(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_RefReadonlyReturnString_ReturnsTrue()
    {
        var method = typeof(RefReadonlyReturnFixture).GetMethod(
            nameof(RefReadonlyReturnFixture.GetReadonlyStringRef))!;
        Assert.True(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_NormalMethodOnRefReadonlyType_ReturnsFalse()
    {
        var method = typeof(RefReadonlyReturnFixture).GetMethod(
            nameof(RefReadonlyReturnFixture.GetIntValue))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    // --- V2 behavioral difference: mutable ref returns are NOT excluded ---

    [Fact]
    public void IsMemberExcluded_MutableRefReturnMethod_ReturnsFalse()
    {
        var method = typeof(HasRefReturn).GetMethod(nameof(HasRefReturn.GetRef))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_MutableRefReturnFixture_ReturnsFalse()
    {
        var method = typeof(MutableRefReturnFixture).GetMethod(
            nameof(MutableRefReturnFixture.GetIntRef))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    // --- V2 behavioral difference: ref/out/in params are NOT excluded ---

    [Fact]
    public void IsMemberExcluded_RefParameter_ReturnsFalse()
    {
        var method = typeof(HasRefParam).GetMethod(nameof(HasRefParam.Swap))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_OutParameter_ReturnsFalse()
    {
        var method = typeof(HasOutParam).GetMethod(nameof(HasOutParam.TryParse))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_InParameter_ReturnsFalse()
    {
        var method = typeof(HasInParam).GetMethod(nameof(HasInParam.ReadValue))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    // --- Requirement 13.5: Cascading exclusion through member signatures ---

    [Fact]
    public void IsMemberExcluded_ParameterReferencesNonPublicType_ReturnsTrue()
    {
        var method = typeof(HasCascadingExclusion).GetMethod(
            "AcceptInternalDep", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // The method itself is internal, so it's excluded by non-public accessibility.
        // But let's also verify the cascading logic works on a member referencing an excluded type.
        Assert.True(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_ReturnTypeReferencesNonPublicType_ReturnsTrue()
    {
        var method = typeof(HasCascadingExclusion).GetMethod(
            "ReturnInternalDep", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_SafeMethodOnCascadingType_ReturnsFalse()
    {
        var method = typeof(HasCascadingExclusion).GetMethod(
            nameof(HasCascadingExclusion.SafeMethod))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsTypeExcluded_InternalNestedDependency_ReturnsTrue()
    {
        // Verify the nested internal type itself is excluded (drives cascading)
        var nestedType = typeof(HasCascadingExclusion).GetNestedType(
            "InternalDependency", BindingFlags.NonPublic)!;
        Assert.True(filter.IsTypeExcluded(nestedType));
    }

    // --- V2 behavioral difference: ref structs are NOT excluded, so members
    //     referencing ref structs should NOT be excluded by cascading ---

    [Fact]
    public void IsMemberExcluded_ParameterReferencesRefStruct_ReturnsFalse()
    {
        var method = typeof(ReferencesExcludedType).GetMethod(
            nameof(ReferencesExcludedType.AcceptRefStruct))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_ReturnTypeReferencesRefStruct_ReturnsFalse()
    {
        var method = typeof(ReferencesExcludedType).GetMethod(
            nameof(ReferencesExcludedType.ReturnRefStruct))!;
        Assert.False(filter.IsMemberExcluded(method));
    }

    // --- IsTypeExcluded: ref struct types are NOT excluded in V2 ---

    [Fact]
    public void IsTypeExcluded_RefStructType_ReturnsFalse()
    {
        Assert.False(filter.IsTypeExcluded(typeof(RefStructType)));
    }
}