


---

## Issue 5: `mist inspect` Crashes with Duplicate Assembly When Packages Overlap Shared Framework — RESOLVED

**Symptom:** `mist inspect Microsoft.EntityFrameworkCore.DbContext` and `mist inspect Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory` both crash with:

```
Unhandled exception. System.IO.FileLoadException: The assembly 'Microsoft.Extensions.Logging.Abstractions, Version=10.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60' has already loaded been loaded into this MetadataLoadContext.
```

**Reproduction:**

```toml
# mist.toml
[packages]
"Npgsql.EntityFrameworkCore.PostgreSQL" = "9.0.4"
"Microsoft.AspNetCore.Mvc.Testing" = "10.0.0"

[framework_references]
"Microsoft.AspNetCore.App" = true
```

Then run:
```bash
mist inspect Microsoft.EntityFrameworkCore.DbContext
# crashes with FileLoadException
```

**Root cause:** The `inspect` and `restore` CLI commands called `PackageResolver.ResolvePackages` using the two-argument overload that doesn't pass framework references for deduplication. The build path already used the four-argument overload with framework references, which is why `mist build` worked fine.

**Fix:** Updated both the `inspect` and `restore` CLI code paths to pass `config.FrameworkReferences` to `PackageResolver.ResolvePackages`, giving them the same framework-provided assembly deduplication as the build path.
