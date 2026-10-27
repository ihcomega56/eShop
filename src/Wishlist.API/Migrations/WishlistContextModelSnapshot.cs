using eShop.Wishlist.API.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace eShop.Wishlist.API.Migrations;

[DbContext(typeof(WishlistContext))]
partial class WishlistContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.12")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("eShop.Wishlist.API.Model.WishlistItem", entity =>
        {
            entity.Property<string>("UserId")
                .HasMaxLength(200)
                .HasColumnType("character varying(200)");

            entity.Property<int>("ProductId")
                .HasColumnType("integer");

            entity.Property<DateTime>("AddedAtUtc")
                .HasColumnType("timestamp with time zone");

            entity.HasKey("UserId", "ProductId");

            entity.ToTable("wishlist_items");
        });
    }
}
