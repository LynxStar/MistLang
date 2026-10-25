# EfCoreMigrations

Entity Framework Core migrations for a data model written in Mist.
Three projects: `App` and `App.Data` are Mist, `App.Migrations` is the C# host that EF's
`dotnet ef` tool requires, since it can target nothing but an MSBuild project.

This is a manual demonstration, not a banked sample. It needs NuGet and the `dotnet-ef` tool, so
`AllFeaturesV2` does not reference it.

## Projects and dependencies

| Project | Folder | Kind | Depends on |
| --- | --- | --- | --- |
| `App.Data` | `App.Data/` | Mist library: the `Item` entity and `StoreContext : DbContext` | nothing |
| `App.Migrations` | `App.Migrations/` | C# `dotnet ef` host: design-time factory, MSBuild bridge, generated `Migrations/` | `App.Data`, through the bridge |
| `App` | `App/` | Mist executable that migrates, then reads and writes the database | `App.Data` and `App.Migrations`, in its `mist.toml` |

`App/mist.toml` references both, and `App.Migrations` is a directory holding exactly one `.csproj`,
so the compiler builds it with `dotnet build` before it compiles `App`; `App` then names it in
`MigrationsAssembly("App.Migrations")` and applies the checked-in migrations on start with
`Database.Migrate()`. Mist composes `App.Data`'s source into `App.dll` while `App.Migrations` was
compiled against `App.Data.dll`, so the compiler writes an `App.Data.dll` facade into `App/bin` that
forwards every `App.Data` type to `App.dll`, which makes the two halves share one `StoreContext`.
`App` and `App.Migrations` also share `store.db` at this directory's root.

## How the bridge works

`App.Migrations.csproj` sets `MistProject` to `../App.Data` and `MistAssemblyName` to `App.Data`,
then imports `MistProject.targets`. Its one target runs before `ResolveAssemblyReferences`,
executes `mist build ../App.Data --backend csharp --quiet`, and adds `App.Data/bin/App.Data.dll`
as a copy-local `Reference`. Every build of the host rebuilds the Mist library, so a change to a
`.mist` entity reaches EF's model differ on the next `dotnet build`.

The project's `RootNamespace` is `App`, so EF names everything it generates into `Migrations/`
`App.Migrations` and keeps the model snapshot in that folder.

`mist` is taken from `PATH`. To use a compiler built from this checkout instead, pass
`-p:MistCommand=<repo>/Source/Mist.CLI/bin/Release/net10.0/osx-arm64/mist` to `dotnet build`, and
export `MistCommand` as that path too, because `mist run App` builds the host itself and MSBuild
reads the environment variable as the property. Run that same path in place of `mist` in
`mist run App`.

## Prerequisites

- .NET SDK 10
- `mist` on `PATH` (`publish.sh` at the repository root installs it)
- Network access for NuGet on the first build

## Run the current state

Both migrations are checked in, and `App` applies them. From this directory:

```sh
mist run App
```

`mist run App` builds `App.Migrations` first, writes the `App.Data` facade, copies the native SQLite
library that `SQLitePCLRaw.bundle_e_sqlite3` supplies for this machine's runtime identifier into
`App/bin`, then runs `App` and prints the row below. Nothing is staged by hand.

Expected output:

```
items=1, name=from Mist, quantity=7
```

`App` inserts one row only when the table is empty, so repeated runs print the same line.

## Reproduce the migrations from scratch

Delete `store.db` and `App.Migrations/Migrations/`, and remove the `Quantity` property from
`App.Data/Item.mist`. Then, from `App.Migrations/`:

```sh
dotnet tool restore
dotnet build App.Migrations.csproj -p:UseSharedCompilation=false --disable-build-servers
dotnet tool run dotnet-ef -- migrations add Initial --project App.Migrations.csproj --startup-project App.Migrations.csproj --output-dir Migrations --no-build
dotnet build App.Migrations.csproj -p:UseSharedCompilation=false --disable-build-servers
dotnet tool run dotnet-ef -- database update --project App.Migrations.csproj --startup-project App.Migrations.csproj --no-build
```

Put `Quantity` back in `App.Data/Item.mist`:

```mist
public int Quantity { get; set; }
```

Then:

```sh
dotnet build App.Migrations.csproj -p:UseSharedCompilation=false --disable-build-servers
dotnet tool run dotnet-ef -- migrations add AddQuantity --project App.Migrations.csproj --startup-project App.Migrations.csproj --output-dir Migrations --no-build
dotnet build App.Migrations.csproj -p:UseSharedCompilation=false --disable-build-servers
dotnet tool run dotnet-ef -- database update --project App.Migrations.csproj --startup-project App.Migrations.csproj --no-build
cd .. && mist run App
```

EF generates `AddColumn<int>("Quantity", "Items", nullable: false, defaultValue: 0)` from the new
Mist property. `dotnet ef` runs with `--no-build` and the host is built explicitly because a
migration file it has just written must be compiled before `database update` can see it. Every
`database update` above is optional, since `mist run App` applies the same migrations through `App`.

## Workarounds in this sample and why

- **`Items => Set<Item>()` instead of an auto-property.** Mist refuses
  `public DbSet<Item> Items { get; set; }` with `MIST0223`: the type
  is not defaultable and the constructor never assigns it. A language rule, not a defect; C# only
  warns, and EF documents the computed form.
- **`Microsoft.EntityFrameworkCore.Sqlite.Core` instead of the `Sqlite` metapackage in `App`.**
  The metapackage has only `_._` placeholders, and the package resolver adds its name to the
  framework-assembly exclusion set, which then hides `Microsoft.EntityFrameworkCore.Sqlite.dll`
  from the core package. Dropping the metapackage means listing `SQLitePCLRaw.bundle_e_sqlite3`
  and calling `SQLitePCL.Batteries.Init()` by hand.
- **`using System.Linq`.** The emitted project disables implicit usings, so `Count()` and
  `First()` need it.

The first and last are how Mist behaves. The middle one is a compiler gap.
