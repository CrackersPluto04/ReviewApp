using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Reviews",
                type: "rowversion",
                nullable: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Collections",
                type: "rowversion",
                nullable: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Collections",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.CreateTable(
                name: "SyncOperations",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    OperationID = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRef = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    EntityType = table.Column<int>(type: "int", nullable: false),
                    EntityID = table.Column<int>(type: "int", nullable: true),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncOperations", x => x.ID);
                    table.ForeignKey(
                        name: "FK_SyncOperations_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncTombstones",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    EntityType = table.Column<int>(type: "int", nullable: false),
                    EntityID = table.Column<int>(type: "int", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncTombstones", x => x.ID);
                    table.ForeignKey(
                        name: "FK_SyncTombstones_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_UserID_RowVersion",
                table: "Reviews",
                columns: new[] { "UserID", "RowVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_Collections_UserID_RowVersion",
                table: "Collections",
                columns: new[] { "UserID", "RowVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_CreatedAt",
                table: "SyncOperations",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_UserID_ClientRef",
                table: "SyncOperations",
                columns: new[] { "UserID", "ClientRef" },
                unique: true,
                filter: "[ClientRef] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SyncOperations_UserID_OperationID",
                table: "SyncOperations",
                columns: new[] { "UserID", "OperationID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncTombstones_DeletedAt",
                table: "SyncTombstones",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SyncTombstones_UserID_RowVersion",
                table: "SyncTombstones",
                columns: new[] { "UserID", "RowVersion" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncOperations");

            migrationBuilder.DropTable(
                name: "SyncTombstones");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_UserID_RowVersion",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Collections_UserID_RowVersion",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Reviews");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Collections");
        }
    }
}
