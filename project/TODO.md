# Future Work

Tracked issues and improvements that don't belong to an active spec but should be addressed.

---

## Emitter Gaps

### Func auto-await does not fire inside interpolation holes

When a Func variable with `Task<T>` return type is invoked directly inside a string interpolation expression (`$"result={asyncFunc(x)}"`), the emitter does not emit `await` for the invocation. The resulting C# calls the delegate and passes the raw `Task<T>` object to string formatting instead of the unwrapped value.

**Workaround:** Hoist the Func invocation into a named variable before using it in interpolation.

```mist
// Broken — prints Task object:
Console.WriteLine($"result={asyncFunc(x)}");

// Works — auto-await fires on the standalone invocation:
var result = asyncFunc(x);
Console.WriteLine($"result={result}");
```

**Root cause:** `IsPostfixAsyncMethodCall` detects Func invocations via `_model.GetTypeOf(postfix.Target)`, which is a node-instance-based lookup. The semantic model may not have type entries for AST nodes inside interpolation expressions (different node instances than those visited during type resolution). Direct async method calls are unaffected because they're detected via `_model.GetDeclaredSymbol` which resolves by identifier name.

**Affected code:** `CSharpEmitter.IsPostfixAsyncMethodCall`, Case 1b (Func invocation detection).
