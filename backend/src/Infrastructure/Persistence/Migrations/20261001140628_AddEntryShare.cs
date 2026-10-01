using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PassManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntryShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entry_shares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceOwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetOwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SharedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entry_shares", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_entry_shares_SourceOwnerId",
                table: "entry_shares",
                column: "SourceOwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_entry_shares_TargetOwnerId",
                table: "entry_shares",
                column: "TargetOwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entry_shares");
        }
    }
}
