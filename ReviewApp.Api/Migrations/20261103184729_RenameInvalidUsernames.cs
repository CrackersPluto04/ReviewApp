using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReviewApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameInvalidUsernames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // F3 (FINDINGS.md): usernames that break the rule of UsernameRules (3-20 characters of letters, digits, '_' and '-',
            // not reserved) can't be opened by URL. They are renamed to user_<ID> (user_<ID>_<n> if that is taken),
            // and the user gets a system notification to pick a new name. The binary collation makes [A-Za-z] ASCII only;
            // the '-' must come first in the brackets ('[^A-Za-z0-9_-]' would also reject '_' and '-').
            // Keep the reserved list in sync with UsernameRules.Reserved.
            migrationBuilder.Sql(@"
SELECT ID INTO #Invalid FROM Users
WHERE DATALENGTH(Username) / 2 NOT BETWEEN 3 AND 20
   OR Username COLLATE Latin1_General_BIN LIKE '%[^-A-Za-z0-9_]%'
   OR LOWER(Username) IN ('search', 'me', 'admin', 'administrator', 'api', 'login', 'logout', 'register', 'settings',
                          'premium', 'support', 'system', 'root', 'null', 'undefined', 'moderator', 'reviewapp');
DECLARE @renamed INT = (SELECT COUNT(*) FROM #Invalid);
PRINT CONCAT('Usernames renamed: ', @renamed);

DECLARE @id INT, @name NVARCHAR(50), @n INT;
DECLARE invalid CURSOR LOCAL FAST_FORWARD FOR SELECT ID FROM #Invalid;
OPEN invalid;
FETCH NEXT FROM invalid INTO @id;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @name = CONCAT('user_', @id);
    SET @n = 1;
    WHILE EXISTS (SELECT 1 FROM Users WHERE Username = @name AND ID <> @id)
    BEGIN
        SET @name = CONCAT('user_', @id, '_', @n);
        SET @n = @n + 1;
    END

    UPDATE Users SET Username = @name WHERE ID = @id;

    INSERT INTO Notifications (RecipientUserID, ActorUserID, Type, ReviewID, ReplyID, GroupCode, Title, Message, IsSeen, CreatedAt)
    VALUES (@id, NULL, 0, NULL, NULL, NULL, 'Please choose a new username',
            CONCAT('Your username could not be used in links, so it was changed to ', @name,
                   '. You can pick a new one in your account settings (letters, numbers, ''_'' and ''-'').'),
            0, GETUTCDATE());

    FETCH NEXT FROM invalid INTO @id;
END
CLOSE invalid;
DEALLOCATE invalid;
DROP TABLE #Invalid;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old names are not kept, so the rename can't be undone
        }
    }
}
