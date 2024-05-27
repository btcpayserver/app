using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayApp.Core.Migrations
{
    /// <inheritdoc />
    public partial class upd2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_LightningPayments",
                table: "LightningPayments");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentId",
                table: "LightningPayments",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_LightningPayments",
                table: "LightningPayments",
                columns: new[] { "PaymentHash", "Inbound", "PaymentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_LightningPayments",
                table: "LightningPayments");

            migrationBuilder.AlterColumn<string>(
                name: "PaymentId",
                table: "LightningPayments",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LightningPayments",
                table: "LightningPayments",
                columns: new[] { "PaymentHash", "Inbound" });
        }
    }
}
