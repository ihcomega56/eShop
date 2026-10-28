using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using eShop.Wishlist.API.Infrastructure;

#nullable disable

namespace eShop.Wishlist.API.Migrations;

[DbContext(typeof(WishlistContext))]
[Migration("20261027000000_Initial")]
public partial class Initial : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "wishlist_items",
            columns: table => new
            {
                UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ProductId = table.Column<int>(type: "integer", nullable: false),
                AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_wishlist_items", x => new { x.UserId, x.ProductId });
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "wishlist_items");
    }
}
