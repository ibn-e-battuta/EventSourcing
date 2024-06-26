using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shipments.Packages;
using Shipments.Products;
using Shipments.Storage;

namespace Shipments;

public static class Config
{
    public static IServiceCollection AddShipmentsModule(this IServiceCollection services, IConfiguration config) =>
        services
            .AddEntityFramework(config)
            .AddPackages()
            .AddProducts();

    private static IServiceCollection AddEntityFramework(this IServiceCollection services, IConfiguration config) =>
        services.AddDbContext<ShipmentsDbContext>(
            options =>
            {
                var schemaName = Environment.GetEnvironmentVariable("SchemaName")!;
                var connectionString = config.GetConnectionString("ShipmentsDatabase");
                options.UseNpgsql(
                    $"{connectionString}; searchpath = {schemaName.ToLower()}",
                    x => x.MigrationsHistoryTable("__EFMigrationsHistory", schemaName.ToLower()));
            });

    public static void ConfigureShipmentsModule(this IApplicationBuilder app)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        if (environment == "Development")
        {
            using var serviceScope = app.ApplicationServices.CreateScope();
            serviceScope.ServiceProvider.GetRequiredService<ShipmentsDbContext>().Database.Migrate();
        }
    }
}
