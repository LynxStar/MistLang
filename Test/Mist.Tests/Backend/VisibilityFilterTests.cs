using System.Reflection;
using Mist.Backends.CSharpTranspiler;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="VisibilityFilter"/>.
/// Validates Requirements 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7.
/// </summary>
public class VisibilityFilterTests
{
    private readonly VisibilityFilter _filter = new();

    // --- Requirement 6.2: in parameters treated as pass-by-value (NOT filtered) ---

    [Fact]
    public void IsMemberExcluded_InParameter_ReturnsFalse()
    {
        var method = typeof(HasInParam).GetMethod(nameof(HasInParam.ReadValue))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_MultipleInParameters_ReturnsFalse()
    {
        var method = typeof(HasInParam).GetMethod(nameof(HasInParam.Sum))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_InParameterWithStringReturn_ReturnsFalse()
    {
        var method = typeof(HasInParam).GetMethod(nameof(HasInParam.Format))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    // --- Requirement 5.8: Events hidden from projection ---

    [Fact]
    public void IsMemberExcluded_Event_ReturnsTrue()
    {
        var evt = typeof(HasEvent).GetEvent(nameof(HasEvent.SomethingHappened))!;
        Assert.True(_filter.IsMemberExcluded(evt));
    }

    [Fact]
    public void IsMemberExcluded_NormalMethodOnTypeWithEvent_ReturnsFalse()
    {
        var method = typeof(HasEvent).GetMethod(nameof(HasEvent.GetValue))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    // --- Ref struct types are no longer excluded (lifted for ref-structs feature) ---

    [Fact]
    public void IsTypeExcluded_RefStruct_ReturnsFalse()
    {
        Assert.False(_filter.IsTypeExcluded(typeof(RefStructType)));
    }

    [Fact]
    public void IsTypeExcluded_NormalStruct_ReturnsFalse()
    {
        Assert.False(_filter.IsTypeExcluded(typeof(SimpleStruct)));
    }

    [Fact]
    public void IsTypeExcluded_NormalClass_ReturnsFalse()
    {
        Assert.False(_filter.IsTypeExcluded(typeof(SimpleClass)));
    }

    [Fact]
    public void IsMemberExcluded_MemberOnRefStruct_ReturnsFalse()
    {
        var method = typeof(RefStructType).GetMethod(nameof(RefStructType.GetValue))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    // --- Requirement 6.1: out parameters exclude method ---

    [Fact]
    public void IsMemberExcluded_OutParameter_ReturnsTrue()
    {
        var method = typeof(HasOutParam).GetMethod(nameof(HasOutParam.TryParse))!;
        Assert.True(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_NormalMethodOnTypeWithOutParam_ReturnsFalse()
    {
        var method = typeof(HasOutParam).GetMethod(nameof(HasOutParam.Parse))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    // --- Requirement 6.2: ref parameters (non-in) exclude method ---

    [Fact]
    public void IsMemberExcluded_RefParameter_ReturnsTrue()
    {
        var method = typeof(HasRefParam).GetMethod(nameof(HasRefParam.Swap))!;
        Assert.True(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_NormalMethodOnTypeWithRefParam_ReturnsFalse()
    {
        var method = typeof(HasRefParam).GetMethod(nameof(HasRefParam.Add))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    // --- Requirement 6.3: ref return excludes method/property ---

    [Fact]
    public void IsMemberExcluded_RefReturnMethod_ReturnsTrue()
    {
        var method = typeof(HasRefReturn).GetMethod(nameof(HasRefReturn.GetRef))!;
        Assert.True(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_NormalMethodOnTypeWithRefReturn_ReturnsFalse()
    {
        var method = typeof(HasRefReturn).GetMethod(nameof(HasRefReturn.GetValue))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_RefReturnProperty_ReturnsTrue()
    {
        var property = typeof(HasRefReturnProperty).GetProperty(nameof(HasRefReturnProperty.RefProp))!;
        Assert.True(_filter.IsMemberExcluded(property));
    }

    [Fact]
    public void IsMemberExcluded_NormalPropertyOnTypeWithRefReturnProp_ReturnsFalse()
    {
        var property = typeof(HasRefReturnProperty).GetProperty(nameof(HasRefReturnProperty.NormalProp))!;
        Assert.False(_filter.IsMemberExcluded(property));
    }

    // --- Requirement 6.4: Pointer types exclude member ---

    [Fact]
    public void IsMemberExcluded_PointerReturnType_ReturnsTrue()
    {
        var method = typeof(HasPointerMembers).GetMethod(nameof(HasPointerMembers.GetPointer))!;
        Assert.True(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_PointerParameter_ReturnsTrue()
    {
        var method = typeof(HasPointerMembers).GetMethod(nameof(HasPointerMembers.ProcessPointer))!;
        Assert.True(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_SafeMethodOnTypeWithPointers_ReturnsFalse()
    {
        var method = typeof(HasPointerMembers).GetMethod(nameof(HasPointerMembers.SafeMethod))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    // --- Requirement 6.7: Non-public members excluded ---

    [Fact]
    public void IsMemberExcluded_PublicField_ReturnsFalse()
    {
        var field = typeof(HasNonPublicMembers).GetField(nameof(HasNonPublicMembers.PublicField))!;
        Assert.False(_filter.IsMemberExcluded(field));
    }

    [Fact]
    public void IsMemberExcluded_InternalField_ReturnsTrue()
    {
        var field = typeof(HasNonPublicMembers).GetField("InternalField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(_filter.IsMemberExcluded(field));
    }

    [Fact]
    public void IsMemberExcluded_ProtectedField_ReturnsTrue()
    {
        var field = typeof(HasNonPublicMembers).GetField("ProtectedField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(_filter.IsMemberExcluded(field));
    }

    [Fact]
    public void IsMemberExcluded_PrivateField_ReturnsTrue()
    {
        var field = typeof(HasNonPublicMembers).GetField("_privateField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(_filter.IsMemberExcluded(field));
    }

    [Fact]
    public void IsMemberExcluded_PublicMethod_ReturnsFalse()
    {
        var method = typeof(HasNonPublicMembers).GetMethod(nameof(HasNonPublicMembers.PublicMethod))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_InternalMethod_ReturnsTrue()
    {
        var method = typeof(HasNonPublicMembers).GetMethod("InternalMethod",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_PublicProperty_ReturnsFalse()
    {
        var property = typeof(HasNonPublicMembers).GetProperty(nameof(HasNonPublicMembers.PublicProp))!;
        Assert.False(_filter.IsMemberExcluded(property));
    }

    [Fact]
    public void IsMemberExcluded_InternalProperty_ReturnsTrue()
    {
        var property = typeof(HasNonPublicMembers).GetProperty("InternalProp",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.True(_filter.IsMemberExcluded(property));
    }

    // --- Requirement 6.6: Signature referencing excluded type → member excluded ---
    // Note: Ref struct types are no longer excluded by IsTypeExcluded, so members
    // referencing ref struct types in their signatures are no longer excluded.

    [Fact]
    public void IsMemberExcluded_ParameterReferencesRefStruct_ReturnsFalse()
    {
        var method = typeof(ReferencesExcludedType).GetMethod(nameof(ReferencesExcludedType.AcceptRefStruct))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_ReturnTypeReferencesRefStruct_ReturnsFalse()
    {
        var method = typeof(ReferencesExcludedType).GetMethod(nameof(ReferencesExcludedType.ReturnRefStruct))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    [Fact]
    public void IsMemberExcluded_SafeMethodOnTypeReferencingExcluded_ReturnsFalse()
    {
        var method = typeof(ReferencesExcludedType).GetMethod(nameof(ReferencesExcludedType.SafeMethod))!;
        Assert.False(_filter.IsMemberExcluded(method));
    }

    // --- IsTypeExcluded: non-public types ---

    [Fact]
    public void IsTypeExcluded_PublicClass_ReturnsFalse()
    {
        Assert.False(_filter.IsTypeExcluded(typeof(SimpleClass)));
    }

    [Fact]
    public void IsTypeExcluded_PublicInterface_ReturnsFalse()
    {
        Assert.False(_filter.IsTypeExcluded(typeof(ISimpleInterface)));
    }

    [Fact]
    public void IsTypeExcluded_PublicEnum_ReturnsFalse()
    {
        Assert.False(_filter.IsTypeExcluded(typeof(SimpleEnum)));
    }
}
