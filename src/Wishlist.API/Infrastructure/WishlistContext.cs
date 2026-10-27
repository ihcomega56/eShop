namespace eShop.Wishlist.API.Infrastructure;

public class WishlistContext(DbContextOptions<WishlistContext> options) : DbContext(options)
{
    public DbSet<WishlistItem> Items => Set<WishlistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WishlistItem>(entity =>
        {
            entity.ToTable("wishlist_items");
            entity.HasKey(item => new { item.UserId, item.ProductId });
            entity.Property(item => item.UserId).HasMaxLength(200);
            entity.Property(item => item.AddedAtUtc).IsRequired();
        });
    }
}
