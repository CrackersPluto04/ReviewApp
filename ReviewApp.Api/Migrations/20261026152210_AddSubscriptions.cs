using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubscriptionEvents",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProviderEventID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionEvents", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserID = table.Column<int>(type: "int", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<int>(type: "int", nullable: false),
                    ProviderCustomerID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderSubscriptionID = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CancelAtPeriodEnd = table.Column<bool>(type: "bit", nullable: false),
                    CurrentPeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PastDueSince = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.ID);
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionEvents_ProviderEventID",
                table: "SubscriptionEvents",
                column: "ProviderEventID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_ProviderCustomerID",
                table: "UserSubscriptions",
                column: "ProviderCustomerID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_ProviderSubscriptionID",
                table: "UserSubscriptions",
                column: "ProviderSubscriptionID",
                unique: true,
                filter: "[ProviderSubscriptionID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_Status_CurrentPeriodEnd",
                table: "UserSubscriptions",
                columns: new[] { "Status", "CurrentPeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserID",
                table: "UserSubscriptions",
                column: "UserID",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionEvents");

            migrationBuilder.DropTable(
                name: "UserSubscriptions");
        }
    }
}
