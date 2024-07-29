using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayApp.Core.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LightningChannels",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LightningChannels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LightningPayments",
                columns: table => new
                {
                    PaymentHash = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentId = table.Column<string>(type: "TEXT", nullable: false),
                    Inbound = table.Column<bool>(type: "INTEGER", nullable: false),
                    Preimage = table.Column<string>(type: "TEXT", nullable: true),
                    Secret = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PaymentRequest = table.Column<string>(type: "TEXT", nullable: false),
                    AdditionalData = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LightningPayments", x => new { x.PaymentHash, x.Inbound, x.PaymentId });
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LightningChannels");

            migrationBuilder.DropTable(
                name: "LightningPayments");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
