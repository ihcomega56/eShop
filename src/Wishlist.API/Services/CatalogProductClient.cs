using System.Net;
using System.Net.Http.Json;

namespace eShop.Wishlist.API.Services;

public sealed class CatalogProductClient(HttpClient httpClient) : ICatalogProductClient
{
    public async Task<CatalogProduct?> GetProductAsync(int productId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            $"api/catalog/items/{productId}?api-version=2.0",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CatalogProduct>(cancellationToken)
            ?? throw new InvalidOperationException("カタログAPIの商品レスポンスが空です。");
    }
}
