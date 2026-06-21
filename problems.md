# Mist: Known Limitations & Issues

## Ambiguous Extension Method Resolution with Multiple Overloads

When an extension method has multiple overloads on the same receiver type, Mist reports ambiguity even when argument count should disambiguate:

```mist
// This FAILS — Configure has 4 overloads on IServiceCollection
builder.Services.Configure<AppSettings>(configuration);
```

```
error MIST_TC_0082: Ambiguous extension method resolution: member 'Configure' is provided by multiple equally-specific extension blocks: extension(IServiceCollection), extension(IServiceCollection).
```

The overloads differ by parameter count (`IConfiguration` vs `String, IConfiguration` vs `IConfiguration, Action<BinderOptions>`), so the 2-arg call should unambiguously resolve to `Configure(IServiceCollection, IConfiguration)`.

**Workaround:** Use the static call form which does resolve correctly:

```mist
OptionsConfigurationServiceCollectionExtensions.Configure<AppSettings>(builder.Services, configuration);
```

---