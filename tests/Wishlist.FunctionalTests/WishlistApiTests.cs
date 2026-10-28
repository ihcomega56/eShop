using System.Net;
using System.Net.Http.Json;
using eShop.Wishlist.API.Model;

namespace eShop.Wishlist.FunctionalTests;

public sealed class WishlistApiTests(WishlistApiFixture fixture) : IClassFixture<WishlistApiFixture>
{
    [Fact]
    public async Task WishlistLifecycleIsPersistentIdempotentAndScopedToUser()
    {
        var userId = $"owner-{Guid.NewGuid():N}";
        using var owner = fixture.CreateClient(userId);
        using var otherUser = fixture.CreateClient($"other-{Guid.NewGuid():N}");

        var save = await owner.PutAsync("/api/wishlist/items/42?api-version=1.0", content: null, TestContext.Current.CancellationToken);
        var repeatedSave = await owner.PutAsync("/api/wishlist/items/42?api-version=1.0", content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedSave.StatusCode);

        var ownerItems = await owner.GetFromJsonAsync<List<WishlistItemResponse>>(
            "/api/wishlist/?api-version=1.0",
            TestContext.Current.CancellationToken);
        var otherItems = await otherUser.GetFromJsonAsync<List<WishlistItemResponse>>(
            "/api/wishlist/?api-version=1.0",
            TestContext.Current.CancellationToken);

        var savedItem = Assert.Single(ownerItems!);
        Assert.Equal(42, savedItem.ProductId);
        Assert.Equal(DateTimeKind.Utc, savedItem.AddedAtUtc.Kind);
        Assert.Empty(otherItems!);

        var otherUserDelete = await otherUser.DeleteAsync(
            "/api/wishlist/items/42?api-version=1.0",
            TestContext.Current.CancellationToken);
        using var newOwnerSession = fixture.CreateClient(userId);
        var persistedItems = await newOwnerSession.GetFromJsonAsync<List<WishlistItemResponse>>(
            "/api/wishlist/?api-version=1.0",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, otherUserDelete.StatusCode);
        Assert.Single(persistedItems!);

        var delete = await owner.DeleteAsync(
            "/api/wishlist/items/42?api-version=1.0",
            TestContext.Current.CancellationToken);
        var otherUserItemsAfterDelete = await otherUser.GetFromJsonAsync<List<WishlistItemResponse>>(
            "/api/wishlist/?api-version=1.0",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(await owner.GetFromJsonAsync<List<WishlistItemResponse>>(
            "/api/wishlist/?api-version=1.0",
            TestContext.Current.CancellationToken) ?? []);
        Assert.Empty(otherUserItemsAfterDelete!);
    }

    [Fact]
    public async Task SaveAcceptsOutOfStockProductAndRejectsUnknownProduct()
    {
        using var client = fixture.CreateClient($"owner-{Guid.NewGuid():N}");

        var outOfStockSave = await client.PutAsync(
            "/api/wishlist/items/42?api-version=1.0",
            content: null,
            TestContext.Current.CancellationToken);
        var unknownProductSave = await client.PutAsync(
            "/api/wishlist/items/404?api-version=1.0",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, outOfStockSave.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownProductSave.StatusCode);
        Assert.Contains(
            (await client.GetFromJsonAsync<List<WishlistItemResponse>>(
                "/api/wishlist/?api-version=1.0",
                TestContext.Current.CancellationToken))!,
            item => item.ProductId == 42);
    }

    [Fact]
    public async Task WishlistRequiresAuthenticationAndPositiveProductId()
    {
        using var anonymous = fixture.CreateAnonymousClient();
        using var authenticated = fixture.CreateClient($"owner-{Guid.NewGuid():N}");

        var anonymousList = await anonymous.GetAsync("/api/wishlist/?api-version=1.0", TestContext.Current.CancellationToken);
        var invalidProduct = await authenticated.PutAsync(
            "/api/wishlist/items/0?api-version=1.0",
            content: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidProduct.StatusCode);
    }
}
