namespace Mist.Tests.InteropFixtures;

// ═══════════════════════════════════════════════════════════════════════════
//  Delegate Visibility Fixtures
//
//  Provides named delegate types to exercise the VisibilityFilter's delegate-
//  specific exclusion logic (Requirements 5.1, 5.2, 5.5):
//  - Public delegates with safe Invoke signatures → visible
//  - Delegates with pointer params in Invoke → hidden
//  - Delegates with pointer return types → hidden
// ═══════════════════════════════════════════════════════════════════════════

// ─── Safe delegates (should be visible) ───────────────────────────────────

/// <summary>A simple non-generic delegate with no excluded types.</summary>
public delegate void SafeVoidDelegate();

/// <summary>A delegate taking an int and returning bool.</summary>
public delegate bool SafePredicateDelegate(int value);

/// <summary>A delegate with multiple params, all safe types.</summary>
public delegate string SafeMultiParamDelegate(int a, string b, double c);

/// <summary>A generic delegate with safe types.</summary>
public delegate TResult SafeGenericDelegate<T, TResult>(T value);

/// <summary>A delegate returning a value type.</summary>
public delegate int SafeIntReturningDelegate(string input);

// ─── Excluded delegates (pointer types in Invoke) ─────────────────────────

/// <summary>A delegate with a pointer parameter — should be hidden.</summary>
public unsafe delegate void PointerParamDelegate(int* ptr);

/// <summary>A delegate with a pointer return type — should be hidden.</summary>
public unsafe delegate int* PointerReturnDelegate();

/// <summary>A delegate with a void* parameter — should be hidden.</summary>
public unsafe delegate void VoidPointerParamDelegate(void* data);

/// <summary>A delegate with a pointer among other params — should be hidden.</summary>
public unsafe delegate int MixedPointerDelegate(int a, byte* buffer, int length);

/// <summary>A method that takes a safe delegate as parameter (should NOT be hidden).</summary>
public class DelegateConsumer
{
    // IDE0060 suppressed: parameters exist to test visibility filter behavior on delegate-typed signatures.
#pragma warning disable IDE0060
    public void AcceptSafeDelegate(SafePredicateDelegate pred) { }

    public void AcceptPointerDelegate(PointerParamDelegate handler) { }
#pragma warning restore IDE0060
}