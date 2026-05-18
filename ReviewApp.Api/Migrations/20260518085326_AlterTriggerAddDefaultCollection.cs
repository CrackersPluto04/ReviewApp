using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlterTriggerAddDefaultCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var alterTriggerSql = @"
                CREATE OR ALTER TRIGGER AddDefaultCollection
                ON Users
                FOR INSERT
                AS

                BEGIN
                    SET NOCOUNT ON;

                    INSERT INTO Collections(UserID, Name, VisibilityLevel, CreatedAt)
                    SELECT ID, 'Favourites', 0, GETUTCDATE()
                    FROM inserted
                END
            ";

            migrationBuilder.Sql(alterTriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
