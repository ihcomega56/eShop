namespace eShop.Wishlist.API.Services;

public interface ICatalogProductClient
{
    Task<CatalogProduct?> GetProductAsync(int productId, CancellationToken cancellationToken);
}

public sealed record CatalogProduct(int Id, int AvailableStock);
