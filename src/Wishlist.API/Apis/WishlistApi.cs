using System.Security.Claims;
using eShop.Wishlist.API.Infrastructure;
using eShop.Wishlist.API.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Wishlist.API;

public static class WishlistApi
{
    public static RouteGroupBuilder MapWishlistApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Wishlist")
            .MapGroup("/api/wishlist")
            .HasApiVersion(1.0);

        api.MapGet("/", async (WishlistContext context, ClaimsPrincipal user, CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            var items = await context.Items
                .Where(item => item.UserId == userId)
                .OrderByDescending(item => item.AddedAtUtc)
                .Select(item => new WishlistItemResponse(item.ProductId, item.AddedAtUtc))
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(items);
        })
        .WithName("GetWishlist")
        .WithSummary("ウィッシュリストを取得")
        .WithDescription("認証済みユーザー自身の保存済み商品を取得します。");

        api.MapPut("/items/{productId:int}", async Task<Results<NoContent, BadRequest<string>, NotFound<string>>> (
            int productId,
            WishlistContext context,
            ClaimsPrincipal user,
            ICatalogProductClient catalog,
            CancellationToken cancellationToken) =>
        {
            if (productId <= 0)
            {
                return TypedResults.BadRequest("商品IDは1以上を指定してください。");
            }

            if (await catalog.GetProductAsync(productId, cancellationToken) is null)
            {
                return TypedResults.NotFound($"商品ID {productId} はカタログに見つかりません。");
            }

            var userId = GetUserId(user);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO wishlist_items (\"UserId\", \"ProductId\", \"AddedAtUtc\") VALUES ({userId}, {productId}, {DateTime.UtcNow}) ON CONFLICT (\"UserId\", \"ProductId\") DO NOTHING",
                cancellationToken);

            return TypedResults.NoContent();
        })
        .WithName("SaveWishlistItem")
        .WithSummary("商品をウィッシュリストに保存")
        .WithDescription("カタログに存在する商品を保存します。在庫切れ商品も保存できます。");

        api.MapDelete("/items/{productId:int}", async (
            int productId,
            WishlistContext context,
            ClaimsPrincipal user,
            CancellationToken cancellationToken) =>
        {
            var userId = GetUserId(user);
            await context.Items
                .Where(item => item.UserId == userId && item.ProductId == productId)
                .ExecuteDeleteAsync(cancellationToken);

            return TypedResults.NoContent();
        })
        .WithName("DeleteWishlistItem")
        .WithSummary("ウィッシュリストから商品を削除")
        .WithDescription("認証済みユーザー自身の保存商品を削除します。");

        return api;
    }

    private static string GetUserId(ClaimsPrincipal user)
        => user.FindFirstValue("sub") ?? throw new InvalidOperationException("Authenticated user has no subject claim.");
}
