using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnsureDefaultCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Favourites used to be deletable and renamable. It is permanent now, so give it back
            // to every user who lost it (the same row the AddDefaultCollection trigger creates).
            migrationBuilder.Sql(@"
                INSERT INTO Collections(UserID, Name, VisibilityLevel, CreatedAt)
                SELECT u.ID, 'Favourites', 0, GETUTCDATE()
                FROM Users u
                WHERE NOT EXISTS (SELECT 1 FROM Collections c WHERE c.UserID = u.ID AND c.Name = 'Favourites')
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo, the recreated collections are indistinguishable from the original ones
        }
    }
}
