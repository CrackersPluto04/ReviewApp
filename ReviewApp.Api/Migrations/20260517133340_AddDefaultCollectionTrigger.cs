using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultCollectionTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var triggerSql = @"
                CREATE OR ALTER TRIGGER AddDefaultCollection
                ON Users
                FOR INSERT
                AS

                BEGIN
                    SET NOCOUNT ON;

                    INSERT INTO Collections(UserID, Name)
                    SELECT ID, 'Favourites'
                    FROM inserted
                END
            ";

            migrationBuilder.Sql(triggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS AddDefaultCollection");
        }
    }
}
