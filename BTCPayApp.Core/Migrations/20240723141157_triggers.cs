using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayApp.Core.Migrations
{
    /// <inheritdoc />
    public partial class triggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Aliases",
                table: "LightningChannels");

            migrationBuilder.AddColumn<bool>(
                name: "Backup",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<ulong>(
                name: "Version",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);

            migrationBuilder.AddColumn<ulong>(
                name: "Version",
                table: "LightningPayments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);

            migrationBuilder.AddColumn<ulong>(
                name: "Version",
                table: "LightningChannels",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0ul);

            migrationBuilder.CreateTable(
                name: "ChannelAliases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelAliases_LightningChannels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "LightningChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutboxItems",
                columns: table => new
                {
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Entity = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: false, defaultValueSql: "date()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxItems", x => new { x.Entity, x.Key, x.ActionType, x.Version });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelAliases_ChannelId",
                table: "ChannelAliases",
                column: "ChannelId");

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_DELETE_APPLIGHTNINGPAYMENT\r\nAFTER DELETE ON \"LightningPayments\"\r\nFOR EACH ROW\r\nBEGIN\r\n  INSERT INTO \"OutboxItems\" (\"Entity\", \"Version\", \"Key\", \"ActionType\") SELECT 'Payment', \r\n  OLD.\"Version\", \r\n  OLD.\"PaymentHash\" || '_' || OLD.\"PaymentId\" || '_' || OLD.\"Inbound\", \r\n  2;\r\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_INSERT_APPLIGHTNINGPAYMENT\r\nAFTER INSERT ON \"LightningPayments\"\r\nFOR EACH ROW\r\nBEGIN\r\n  INSERT INTO \"OutboxItems\" (\"Entity\", \"Version\", \"Key\", \"ActionType\") SELECT 'Payment', \r\n  NEW.\"Version\", \r\n  NEW.\"PaymentHash\" || '_' || NEW.\"PaymentId\" || '_' || NEW.\"Inbound\", \r\n  0;\r\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_UPDATE_APPLIGHTNINGPAYMENT\r\nAFTER UPDATE ON \"LightningPayments\"\r\nFOR EACH ROW\r\nBEGIN\r\n  UPDATE \"LightningPayments\"\r\n  SET \"Version\" = OLD.\"Version\" + 1\r\n  WHERE OLD.\"PaymentHash\" = \"LightningPayments\".\"PaymentHash\";\r\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_DELETE_CHANNEL\r\nAFTER DELETE ON \"LightningChannels\"\r\nFOR EACH ROW\r\nBEGIN\r\n  INSERT INTO \"OutboxItems\" (\"Entity\", \"Version\", \"Key\", \"ActionType\") SELECT 'Channel', \r\n  OLD.\"Version\", \r\n  OLD.\"Id\", \r\n  2;\r\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_INSERT_CHANNEL\r\nAFTER INSERT ON \"LightningChannels\"\r\nFOR EACH ROW\r\nBEGIN\r\n  INSERT INTO \"OutboxItems\" (\"Entity\", \"Version\", \"Key\", \"ActionType\") SELECT 'Channel', \r\n  NEW.\"Version\", \r\n  NEW.\"Id\", \r\n  0;\r\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_UPDATE_CHANNEL\r\nAFTER UPDATE ON \"LightningChannels\"\r\nFOR EACH ROW\r\nBEGIN\r\n  UPDATE \"LightningChannels\"\r\n  SET \"Version\" = OLD.\"Version\" + 1\r\n  WHERE OLD.\"Id\" = \"LightningChannels\".\"Id\";\r\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_DELETE_SETTING\r\nAFTER DELETE ON \"Settings\"\r\nFOR EACH ROW\r\nWHEN \r\n  \r\n  OLD.\"Backup\" IS TRUE\r\nBEGIN\r\n  INSERT INTO \"OutboxItems\" (\"Entity\", \"Version\", \"Key\", \"ActionType\") SELECT 'Setting', \r\n  OLD.\"Version\", \r\n  OLD.\"Key\", \r\n  2;\r\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_INSERT_SETTING\r\nAFTER INSERT ON \"Settings\"\r\nFOR EACH ROW\r\nWHEN \r\n  \r\n  NEW.\"Backup\" IS TRUE\r\nBEGIN\r\n  INSERT INTO \"OutboxItems\" (\"Entity\", \"Version\", \"Key\", \"ActionType\") SELECT 'Setting', \r\n  NEW.\"Version\", \r\n  NEW.\"Key\", \r\n  0;\r\nEND;");

            migrationBuilder.Sql("CREATE TRIGGER LC_TRIGGER_AFTER_UPDATE_SETTING\r\nAFTER UPDATE ON \"Settings\"\r\nFOR EACH ROW\r\nBEGIN\r\n  UPDATE \"Settings\"\r\n  SET \"Version\" = OLD.\"Version\" + 1\r\n  WHERE OLD.\"Key\" = \"Settings\".\"Key\";\r\nEND;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA writable_schema = 1; \r\nDELETE FROM sqlite_master WHERE type = 'trigger' AND name like 'LC_TRIGGER_AFTER_DELETE_APPLIGHTNINGPAYMENT%';\r\nPRAGMA writable_schema = 0;");

            migrationBuilder.Sql("PRAGMA writable_schema = 1; \r\nDELETE FROM sqlite_master WHERE type = 'trigger' AND name like 'LC_TRIGGER_AFTER_INSERT_APPLIGHTNINGPAYMENT%';\r\nPRAGMA writable_schema = 0;");

            migrationBuilder.Sql("PRAGMA writable_schema = 1; \r\nDELETE FROM sqlite_master WHERE type = 'trigger' AND name like 'LC_TRIGGER_AFTER_UPDATE_APPLIGHTNINGPAYMENT%';\r\nPRAGMA writable_schema = 0;");

            migrationBuilder.Sql("PRAGMA writable_schema = 1; \r\nDELETE FROM sqlite_master WHERE type = 'trigger' AND name like 'LC_TRIGGER_AFTER_DELETE_CHANNEL%';\r\nPRAGMA writable_schema = 0;");

            migrationBuilder.Sql("PRAGMA writable_schema = 1; \r\nDELETE FROM sqlite_master WHERE type = 'trigger' AND name like 'LC_TRIGGER_AFTER_INSERT_CHANNEL%';\r\nPRAGMA writable_schema = 0;");

            migrationBuilder.Sql("PRAGMA writable_schema = 1; \r\nDELETE FROM sqlite_master WHERE type = 'trigger' AND name like 'LC_TRIGGER_AFTER_UPDATE_CHANNEL%';\r\nPRAGMA writable_schema = 0;");

            migrationBuilder.Sql("PRAGMA writable_schema = 1; \r\nDELETE FROM sqlite_master WHERE type = 'trigger' AND name like 'LC_TRIGGER_AFTER_DELETE_SETTING%';\r\nPRAGMA writable_schema = 0;");

            migrationBuilder.Sql("PRAGMA writable_schema = 1; \r\nDELETE FROM sqlite_master WHERE type = 'trigger' AND name like 'LC_TRIGGER_AFTER_INSERT_SETTING%';\r\nPRAGMA writable_schema = 0;");

            migrationBuilder.Sql("PRAGMA writable_schema = 1; \r\nDELETE FROM sqlite_master WHERE type = 'trigger' AND name like 'LC_TRIGGER_AFTER_UPDATE_SETTING%';\r\nPRAGMA writable_schema = 0;");

            migrationBuilder.DropTable(
                name: "ChannelAliases");

            migrationBuilder.DropTable(
                name: "OutboxItems");

            migrationBuilder.DropColumn(
                name: "Backup",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "LightningPayments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "LightningChannels");

            migrationBuilder.AddColumn<string>(
                name: "Aliases",
                table: "LightningChannels",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
