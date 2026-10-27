namespace eShop.Wishlist.API.Model;

public class WishlistItem
{
    public required string UserId { get; set; }

    public int ProductId { get; set; }

    public DateTime AddedAtUtc { get; set; }
}
