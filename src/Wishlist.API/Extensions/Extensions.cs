using eShop.Wishlist.API.Services;

internal static class Extensions
{
    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.AddDefaultAuthentication();
        builder.AddNpgsqlDbContext<WishlistContext>("wishlistdb");
        builder.Services.AddMigration<WishlistContext>();
        builder.Services.AddHttpClient<ICatalogProductClient, CatalogProductClient>(client =>
            client.BaseAddress = new Uri("https+http://catalog-api"));
    }
}
