using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayApp.Core.Migrations
{
    /// <inheritdoc />
    public partial class triggers2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Settings_EntityKey",
                table: "Settings",
                column: "EntityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LightningPayments_EntityKey",
                table: "LightningPayments",
                column: "EntityKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LightningChannels_EntityKey",
                table: "LightningChannels",
                column: "EntityKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Settings_EntityKey",
                table: "Settings");

            migrationBuilder.DropIndex(
                name: "IX_LightningPayments_EntityKey",
                table: "LightningPayments");

            migrationBuilder.DropIndex(
                name: "IX_LightningChannels_EntityKey",
                table: "LightningChannels");
        }
    }
}
