using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.App.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "BTCPayServer.Plugins.App");

            migrationBuilder.CreateTable(
                name: "AppStorageItems",
                schema: "BTCPayServer.Plugins.App",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppStorageItems", x => new { x.Key, x.Version, x.UserId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppStorageItems_Key_UserId",
                schema: "BTCPayServer.Plugins.App",
                table: "AppStorageItems",
                columns: new[] { "Key", "UserId" },
                unique: true);

            migrationBuilder.Sql("CREATE FUNCTION \"BTCPayServer.Plugins.App\".\"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"() RETURNS trigger as $LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA$\r\nBEGIN\r\n  DELETE FROM \"BTCPayServer.Plugins.App\".\"AppStorageItems\"\r\n  WHERE NEW.\"UserId\" = \"BTCPayServer.Plugins.App\".\"AppStorageItems\".\"UserId\" AND NEW.\"Key\" = \"BTCPayServer.Plugins.App\".\"AppStorageItems\".\"Key\" AND NEW.\"Version\" > \"BTCPayServer.Plugins.App\".\"AppStorageItems\".\"Version\";\r\nRETURN NEW;\r\nEND;\r\n$LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA$ LANGUAGE plpgsql;\r\nCREATE TRIGGER LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA BEFORE INSERT\r\nON \"BTCPayServer.Plugins.App\".\"AppStorageItems\"\r\nFOR EACH ROW EXECUTE PROCEDURE \"BTCPayServer.Plugins.App\".\"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION \"BTCPayServer.Plugins.App\".\"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"() CASCADE;");

            migrationBuilder.DropTable(
                name: "AppStorageItems",
                schema: "BTCPayServer.Plugins.App");
        }
    }
}
