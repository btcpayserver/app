using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayApp.Core.Migrations
{
    /// <inheritdoc />
    public partial class checkpoitnchannels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Checkpoint",
                table: "LightningChannels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Checkpoint",
                table: "LightningChannels");
        }
    }
}
