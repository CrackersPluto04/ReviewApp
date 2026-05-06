using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AlterTriggerSetUpdatedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var alterTriggerSql = @"
                CREATE OR ALTER TRIGGER SetUpdatedAt
                ON Reviews
                FOR INSERT, UPDATE
                AS

                BEGIN
                    SET NOCOUNT ON;

                    UPDATE Reviews
                    SET UpdatedAt = GETUTCDATE()
                    FROM inserted i
                    WHERE Reviews.ID = i.ID;
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
