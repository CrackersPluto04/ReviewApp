using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAchievements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Categories = table.Column<int>(type: "int", nullable: false),
                    Metric = table.Column<int>(type: "int", nullable: false),
                    IconKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetValue = table.Column<int>(type: "int", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.ID);
                });

            migrationBuilder.CreateTable(
                name: "UserAchievements",
                columns: table => new
                {
                    UserID = table.Column<int>(type: "int", nullable: false),
                    AchievementID = table.Column<int>(type: "int", nullable: false),
                    CurrentProgress = table.Column<int>(type: "int", nullable: false),
                    IsUnlocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAchievements", x => new { x.UserID, x.AchievementID });
                    table.ForeignKey(
                        name: "FK_UserAchievements_Achievements_AchievementID",
                        column: x => x.AchievementID,
                        principalTable: "Achievements",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAchievements_Users_UserID",
                        column: x => x.UserID,
                        principalTable: "Users",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Achievements",
                columns: new[] { "ID", "Categories", "Code", "Description", "GroupCode", "IconKey", "Metric", "TargetValue", "Tier", "Title" },
                values: new object[,]
                {
                    { 1, 9, "MOVIE_MASTER_BRONZE", "Review 1 movie.", "MOVIE_MASTER", "movie-master", 0, 1, 0, "Movie Master" },
                    { 2, 9, "MOVIE_MASTER_SILVER", "Review 10 movies.", "MOVIE_MASTER", "movie-master", 0, 10, 1, "Movie Master" },
                    { 3, 9, "MOVIE_MASTER_GOLD", "Review 50 movies.", "MOVIE_MASTER", "movie-master", 0, 50, 2, "Movie Master" },
                    { 4, 10, "BINGE_WATCHER_BRONZE", "Review 1 series.", "BINGE_WATCHER", "binge-watcher", 1, 1, 0, "Binge Watcher" },
                    { 5, 10, "BINGE_WATCHER_SILVER", "Review 10 series.", "BINGE_WATCHER", "binge-watcher", 1, 10, 1, "Binge Watcher" },
                    { 6, 10, "BINGE_WATCHER_GOLD", "Review 50 series.", "BINGE_WATCHER", "binge-watcher", 1, 50, 2, "Binge Watcher" },
                    { 7, 12, "MUSIC_LOVER_BRONZE", "Review 1 piece of music.", "MUSIC_LOVER", "music-lover", 2, 1, 0, "Music Lover" },
                    { 8, 12, "MUSIC_LOVER_SILVER", "Review 10 pieces of music.", "MUSIC_LOVER", "music-lover", 2, 10, 1, "Music Lover" },
                    { 9, 12, "MUSIC_LOVER_GOLD", "Review 50 pieces of music.", "MUSIC_LOVER", "music-lover", 2, 50, 2, "Music Lover" },
                    { 10, 8, "CRITIC_BRONZE", "Write 10 reviews.", "CRITIC", "critic", 3, 10, 0, "Critic" },
                    { 11, 8, "CRITIC_SILVER", "Write 50 reviews.", "CRITIC", "critic", 3, 50, 1, "Critic" },
                    { 12, 8, "CRITIC_GOLD", "Write 100 reviews.", "CRITIC", "critic", 3, 100, 2, "Critic" },
                    { 13, 16, "CONVERSATION_STARTER_BRONZE", "Write 10 replies.", "CONVERSATION_STARTER", "conversation-starter", 4, 10, 0, "Conversation Starter" },
                    { 14, 16, "CONVERSATION_STARTER_SILVER", "Write 50 replies.", "CONVERSATION_STARTER", "conversation-starter", 4, 50, 1, "Conversation Starter" },
                    { 15, 16, "CONVERSATION_STARTER_GOLD", "Write 100 replies.", "CONVERSATION_STARTER", "conversation-starter", 4, 100, 2, "Conversation Starter" },
                    { 16, 8, "PROFESSIONAL_HATER_BRONZE", "Have 1 review scored 5.0 or less.", "PROFESSIONAL_HATER", "professional-hater", 5, 1, 0, "Professional Hater" },
                    { 17, 8, "PROFESSIONAL_HATER_SILVER", "Have 10 reviews scored 5.0 or less.", "PROFESSIONAL_HATER", "professional-hater", 5, 10, 1, "Professional Hater" },
                    { 18, 8, "PROFESSIONAL_HATER_GOLD", "Have 50 reviews scored 5.0 or less.", "PROFESSIONAL_HATER", "professional-hater", 5, 50, 2, "Professional Hater" },
                    { 19, 32, "CURATOR_BRONZE", "Create 1 collection of your own.", "CURATOR", "curator", 6, 1, 0, "Curator" },
                    { 20, 32, "CURATOR_SILVER", "Create 5 collections of your own.", "CURATOR", "curator", 6, 5, 1, "Curator" },
                    { 21, 32, "CURATOR_GOLD", "Create 10 collections of your own.", "CURATOR", "curator", 6, 10, 2, "Curator" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_Code",
                table: "Achievements",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_GroupCode",
                table: "Achievements",
                column: "GroupCode");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_AchievementID",
                table: "UserAchievements",
                column: "AchievementID");

            // One-time backfill of existing users' progress. Mirrors AchievementService.CountAsync,
            // Metric values: 0 movie, 1 series, 2 music reviews, 3 all reviews, 4 replies, 5 low score reviews, 6 collections.
            // Only rows with progress are inserted, a missing row means zero progress.
            var backfillSql = @"
                WITH Progress AS (
                    SELECT
                        u.ID AS UserID,
                        a.ID AS AchievementID,
                        a.TargetValue,
                        CASE a.Metric
                            WHEN 0 THEN (SELECT COUNT(*) FROM Reviews r JOIN Media m ON m.ID = r.MediaID WHERE r.UserID = u.ID AND m.MediaType = 0)
                            WHEN 1 THEN (SELECT COUNT(*) FROM Reviews r JOIN Media m ON m.ID = r.MediaID WHERE r.UserID = u.ID AND m.MediaType = 1)
                            WHEN 2 THEN (SELECT COUNT(*) FROM Reviews r JOIN Media m ON m.ID = r.MediaID WHERE r.UserID = u.ID AND m.MediaType = 2)
                            WHEN 3 THEN (SELECT COUNT(*) FROM Reviews r WHERE r.UserID = u.ID)
                            WHEN 4 THEN (SELECT COUNT(*) FROM ReviewReplies rr WHERE rr.UserID = u.ID AND rr.IsDeleted = 0)
                            WHEN 5 THEN (SELECT COUNT(*) FROM Reviews r WHERE r.UserID = u.ID AND r.Score <= 5.0)
                            WHEN 6 THEN (SELECT COUNT(*) FROM Collections c WHERE c.UserID = u.ID AND c.Name <> 'Favourites')
                            ELSE 0
                        END AS CurrentProgress
                    FROM Users u
                    CROSS JOIN Achievements a
                )
                INSERT INTO UserAchievements (UserID, AchievementID, CurrentProgress, IsUnlocked)
                SELECT UserID, AchievementID, CurrentProgress, CASE WHEN CurrentProgress >= TargetValue THEN 1 ELSE 0 END
                FROM Progress
                WHERE CurrentProgress > 0
            ";

            migrationBuilder.Sql(backfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAchievements");

            migrationBuilder.DropTable(
                name: "Achievements");
        }
    }
}
