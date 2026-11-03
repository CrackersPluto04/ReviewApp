using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueMediaAndReviewIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F5 (FINDINGS.md): remove the duplicates parallel requests could store before the unique indexes existed.
            // One batch, so the temp table lives for all of it. Counts are PRINTed (visible with `dotnet ef migrations script` + sqlcmd).
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM Media WHERE LEN(ExternalApiID) > 64)
    THROW 50001, 'Media.ExternalApiID values longer than 64 characters exist; fix them before applying AddUniqueMediaAndReviewIndexes.', 1;

-- Media: keep the lowest ID per (MediaType, ExternalApiID), point everything at it
SELECT ID, KeepID INTO #MediaDup
FROM (SELECT ID, MIN(ID) OVER (PARTITION BY MediaType, ExternalApiID) AS KeepID FROM Media) ranked
WHERE ID <> KeepID;
DECLARE @duplicateMedia INT = (SELECT COUNT(*) FROM #MediaDup);
PRINT CONCAT('Duplicate media rows: ', @duplicateMedia);

-- A collection holding several copies keeps only the one with the lowest media ID (the primary key would collide)
DELETE cm FROM CollectionMedias cm
JOIN #MediaDup d ON cm.MediaID = d.ID
WHERE EXISTS (
    SELECT 1 FROM CollectionMedias k
    LEFT JOIN #MediaDup kd ON kd.ID = k.MediaID
    WHERE k.CollectionID = cm.CollectionID AND COALESCE(kd.KeepID, k.MediaID) = d.KeepID AND k.MediaID < cm.MediaID);

UPDATE cm SET MediaID = d.KeepID FROM CollectionMedias cm JOIN #MediaDup d ON cm.MediaID = d.ID;

-- Repointing a review is not an edit: keep UpdatedAt as it is
DISABLE TRIGGER SetUpdatedAt ON Reviews;
UPDATE r SET MediaID = d.KeepID FROM Reviews r JOIN #MediaDup d ON r.MediaID = d.ID;
ENABLE TRIGGER SetUpdatedAt ON Reviews;

DELETE m FROM Media m JOIN #MediaDup d ON m.ID = d.ID;
DROP TABLE #MediaDup;

-- Reviews: keep the newest per (UserID, MediaID); the older ones and their replies (cascade) are deleted
SELECT ID INTO #ReviewDup
FROM (SELECT ID, ROW_NUMBER() OVER (PARTITION BY UserID, MediaID ORDER BY UpdatedAt DESC, ID DESC) AS RowNumber FROM Reviews) ranked
WHERE RowNumber > 1;
DECLARE @duplicateReviews INT = (SELECT COUNT(*) FROM #ReviewDup);
PRINT CONCAT('Duplicate reviews: ', @duplicateReviews);

DELETE r FROM Reviews r JOIN #ReviewDup d ON r.ID = d.ID;
DROP TABLE #ReviewDup;
");

            migrationBuilder.DropIndex(
                name: "IX_Reviews_UserID",
                table: "Reviews");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalApiID",
                table: "Media",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_UserID_MediaID",
                table: "Reviews",
                columns: new[] { "UserID", "MediaID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Media_MediaType_ExternalApiID",
                table: "Media",
                columns: new[] { "MediaType", "ExternalApiID" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reviews_UserID_MediaID",
                table: "Reviews");

            migrationBuilder.DropIndex(
                name: "IX_Media_MediaType_ExternalApiID",
                table: "Media");

            migrationBuilder.AlterColumn<string>(
                name: "ExternalApiID",
                table: "Media",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_UserID",
                table: "Reviews",
                column: "UserID");
        }
    }
}
