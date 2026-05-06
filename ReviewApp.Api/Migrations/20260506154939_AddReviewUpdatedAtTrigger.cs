using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewUpdatedAtTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var createTriggerSql = @"
                create or alter trigger SetUpdatedAt
                on Reviews
                for update
                as

                begin
                    set nocount on;

                    update Reviews
                    set UpdatedAt = getutcdate()
                    from inserted i
                    where Reviews.ID = i.ID;
                end
            ";

            migrationBuilder.Sql(createTriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop trigger if exists SetUpdatedAt;");
        }
    }
}
