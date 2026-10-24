using App.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace App.Migrations;

public sealed class ContextFactory : IDesignTimeDbContextFactory<StoreContext>
{
    public StoreContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder();
        _ = builder.UseSqlite(
            "Data Source=../store.db",
            sqlite => sqlite.MigrationsAssembly(typeof(ContextFactory).Assembly.FullName));
        return new StoreContext(builder.Options);
    }
}
