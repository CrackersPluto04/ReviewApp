# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository layout

This is a two-project repo with no shared root tooling — each half is built/run independently:

- `ReviewApp.Api/` — ASP.NET Core 8 Web API (C#), EF Core + SQL Server backend.
- `review-app/` — Preact + TypeScript frontend (built with Vite), using MUI for components and react-router-dom for routing.

## Common commands

### Backend (`ReviewApp.Api/`)

Run all commands from `ReviewApp.Api/`.

```
dotnet run                          # run with default (http) launch profile
dotnet run --launch-profile https   # run with HTTPS (matches frontend CORS origin)
dotnet build
dotnet ef migrations add <Name>     # add a new EF Core migration
dotnet ef database update           # apply migrations to local DB
```

- Swagger UI is served at `/swagger` in Development.
- HTTPS profile listens on `https://localhost:7140` (also `http://localhost:5062`); the frontend's API clients are hardcoded to `https://localhost:7140`, so run the API with the `https` profile when working end-to-end with the frontend. The CORS origin and the links in emails come from `App:FrontendBaseUrl` (`https://localhost:5173` in `appsettings.json`); `Program.cs` refuses to start without it.
- Local connection strings and third-party API keys (TMDb, Spotify) live in `appsettings.Development.json`, which is gitignored (not committed) — each developer creates their own copy locally. The same goes for real SMTP credentials (`Email` section).
- **Emails in development**: the committed `Email` settings point to a local SMTP catcher on `localhost:1025` without auth (e.g. Mailpit, web UI on `http://localhost:8025`). Without one running, sending just fails in the background (logged), the API keeps working. To send real emails, override `Email` (Host, Port, Security = `StartTls`/`SslOnConnect`, Username, Password, FromAddress) in `appsettings.Development.json`.

### Frontend (`review-app/`)

Run all commands from `review-app/`.

```
npm start      # vite dev server, https on port 5173, opens browser
npm run build  # production build
npm run preview
```

- The dev server uses `@vitejs/plugin-basic-ssl`, so it always serves over HTTPS on `https://localhost:5173` — this must match the CORS origin allowed by the API.
- No lint or test scripts are currently configured in `package.json`.

## Architecture

### Backend

- **Layering**: `Controllers/` → `Services/` (business logic, interfaces in `Services/Interfaces/`) → `DAL/` (EF Core `AppDbContext` + `Entities/`). Controllers depend on service interfaces, which are registered as scoped in `Program.cs`.
- **Auth**: JWT bearer auth, but the token is normally delivered via an httpOnly cookie (`__Host-jwt_token`, `Secure`, `SameSite=Strict`, `Path=/`) rather than the `Authorization` header — see the `OnMessageReceived` handler in `Program.cs` that pulls the token from the cookie. `UserAuthHelper` (`IUserAuthHelper`) reads the authenticated user's ID from the `"id"` claim; use `GetSecureUserID()` in endpoints that require auth and `GetOptionalUserID()` where auth is optional (e.g. to vary visibility of results for anonymous vs. logged-in users).
  - **Issuing/clearing the cookie**: always go through `ITokenService` (`IssueAuthCookie(user)` / `DeleteAuthCookie()`); it owns the cookie name, cookie options and the 8h lifetime. Don't append/delete the cookie by hand. The token carries only the `id` and `ver` claims (HS256, validated with `ValidAlgorithms` pinned and a 30s `ClockSkew`). `Program.cs` refuses to start if `Jwt:Key` is missing or shorter than 32 bytes.
  - **Revocation (`TokenVersion`)**: `User.TokenVersion` is copied into the `ver` claim; `OnTokenValidated` in `Program.cs` compares it with the DB on every authenticated request and rejects mismatches (missing `ver` = 0). Incrementing `TokenVersion` logs the user out everywhere — this happens on password change (`PUT /api/User/me/password`, which then re-issues a cookie for the current browser), on a confirmed email change and on `POST /api/Auth/logout-all`. Any new "invalidate all sessions" feature should do the same.
  - **Two-factor authentication (email codes)**: when `User.IsTwoFactorEnabled`, `POST /api/Auth/login` (after a correct password only) issues no cookie; it emails a code and returns `{ twoFactorRequired, challengeToken }`. The challenge is a 10 min JWT from `ITokenService.CreateTwoFactorChallenge` with its own audience (`<Jwt:Audience>:2fa-challenge`), so the bearer handler never accepts it as a session, and it carries `ver`, so logout-all / password change kill pending logins. `POST /api/Auth/login/2fa` (challenge + code) issues the cookie, `login/2fa/resend` sends a new code. Enabling requires a verified email; enable/disable are two steps (password -> emailed code) in `SecurityController`. No recovery codes yet (TOTP and recovery codes are planned later).
  - **Security tokens**: `SecurityService` owns email verification, email change and 2FA codes, stored in `SecurityToken` as SHA-256 hashes only (`SecurityTokens` helper: generation, hashing, constant-time compare, lifetimes). Each `SecurityTokenType` is single purpose; issuing a token invalidates older unused ones of the same user + type; consuming is an atomic `ExecuteUpdate`; codes burn after 5 wrong tries; resends have a 60s cooldown. Link tokens (verify 24h, email change 1h) are confirmed by a POST from the frontend page, never by the GET of the link (mail scanners prefetch links).
  - **Email change** (`PUT /api/User/me/email`) needs the password and a verified email; it stores `User.PendingEmail`, mails a confirm link to the new address and a notice to the old one. Confirming sets the email, marks it verified and bumps `TokenVersion` (the browser that confirms keeps a fresh cookie if it's the same user). Existing users start unverified; unverified users aren't restricted except for 2FA and email change.
  - **Login** verifies unknown emails against a dummy BCrypt hash so response time doesn't reveal registered emails. Passwords are capped at 20 characters in DTOs and in `src/utils/validation.ts` — this keeps them under BCrypt's 72-byte limit even with multi-byte characters (C# and JS both count UTF-16 units, each at most 3 UTF-8 bytes), so no separate byte check is needed. Don't raise it above 24 without adding one.
  - **Credentials in transit**: email/password are sent as plain JSON over HTTPS by design (visible in the browser's own DevTools, encrypted on the wire) — don't add client-side hashing/encryption. HSTS (`UseHsts`) is enabled outside Development.
- **Rate limiting**: configured in `Program.cs` (`AddRateLimiter`, `app.UseRateLimiter()` after auth so logged-in users are partitioned by `id`, anonymous ones by IP). A global limit (300/min) applies to every request, plus named policies applied with `[EnableRateLimiting("...")]`: `auth-attempt` (login/register/2FA login step, 10 per 10 min per IP), `credential-change` (change email/password, 2FA enable/disable steps, 5 per 10 min), `email-link` (anonymous verify-email / confirm-email-change, 10 per 10 min per IP, separate so opening links never uses up login attempts), `email-send` (resend verification / login code, 5 per 10 min), `external-api` (`MediaController`, 60/min — protects the TMDb/Spotify quotas). Rejections are 429 with a JSON `{ error }` body. Counters are in-memory, so restarting the API resets them.
- **Media model (TPH inheritance)**: `Media` is an abstract EF Core entity with `Movie`, `Series`, and `Music` as concrete subtypes, mapped via table-per-hierarchy using the `MediaType` discriminator (configured in `AppDbContext.OnModelCreating`). Media rows are populated from external APIs — `TmdbService` (movies/series, via `ITmdbService`) and `SpotifyService` (music, via `ISpotifyService`) — and cached locally, keyed by `ExternalApiID` + `MediaType`.
- **Database triggers**: Some invariants are enforced by SQL Server triggers created in migrations rather than in C#: `SetUpdatedAt` on `Review` (auto-updates timestamp) and `AddDefaultCollection` on `User` (creates a default collection on user insert). Keep this in mind when reasoning about `Review`/`Collection` state — it isn't always set from application code.
- **Collections**: `Collection` has a per-user unique `Name` constraint. The default `Favourites` collection (`Collection.DefaultName`, created by the trigger) is permanent: `CollectionService` rejects renaming, editing or deleting it, so its name always identifies it (no other collection can take the name). `CollectionDto.IsDefault` marks it for the frontend. `CollectionMedia` is a join entity with a composite key (`CollectionID`, `MediaID`) and cascade delete from both sides.
- **Followers**: `UserFollower` is a self-referencing join entity on `User` with a composite key (`FollowerID`, `FollowingID`) and `Restrict` delete behavior (to avoid cascade cycles on `User` deletion).
- **Review replies**: `ReviewReply` (`ReviewReplyService` / `ReviewReplyController`, `api/ReviewReply`) threads are max 3 layers: review -> reply (`ParentReplyID` null) -> reply to reply. The layer is derived from the parent chain (no column); the service rejects replying to a reply that already has a parent. Replies have no visibility of their own — every read/write checks the parent review's visibility (hidden == 404), and private reviews can't be replied to. Deleting a reply is a soft delete (`IsDeleted`, content kept in the DB but never returned); a deleted reply is only listed as a placeholder while it has non-deleted children. Deleting a review cascades to all its replies in the DB (`ParentReply`/`User` FKs are `Restrict`). Listing uses keyset paging (`afterId`) for "load more"; review list responses include a non-deleted `ReplyCount`.
- **Achievements**: `Achievement` holds one row per tier (bronze/silver/gold); the tiers of one card share a `GroupCode` and a `Metric` (`AchievementMetric`, what is counted). `Categories` is a `[Flags]` enum (`AchievementCategories`), so one achievement can be in several categories. Definitions are seeded with `HasData` from `DAL/AchievementSeed.cs` — keep the IDs stable and add new tiers with new IDs plus a migration. `UserAchievement` (cascade deleted with the user) stores `CurrentProgress` and `IsUnlocked`; a missing row means zero progress.
  - **Evaluation**: `AchievementService.EvaluateAsync(userId, metrics...)` recounts the real totals and is called after `SaveChangesAsync` when content is created (and when a review is edited, as its score can change), not on deletes. Progress is the highest count ever reached (`Math.Max` with the stored value), so deleting content never lowers it, and deleting and recreating content doesn't raise it until the count beats the old peak. Unlocks are permanent. It never throws: failures are logged and swallowed, so the triggering action still succeeds. Counting rules live in `AchievementService.CountAsync` and are mirrored by the backfill SQL in the `AddAchievements` migration — reviews of every visibility count, replies exclude soft-deleted ones, low score is `Score <= 5.0`, and collections exclude the default `Favourites` (`Collection.DefaultName`). A new metric needs a case there and a hook where its data changes.
  - **Unlock delivery**: newly unlocked tiers go to every registered `IAchievementUnlockHandler`. `ResponseHeaderUnlockHandler` writes them (only for the requesting user) to the `X-Unlocked-Achievements` response header as JSON; the header is exposed in the CORS policy. `NotificationUnlockHandler` stores one notification per tier. Add a new handler instead of changing the service. `GET api/Achievement/{username}` is public.
- **Notifications**: `Notification` (recipient cascade, nullable actor `Restrict`) stores ready-to-display `Title`/`Message` plus a navigation target as plain ints/strings without FKs (`ReviewID`, `ReplyID`, `GroupCode`), so it outlives deleted content (the link then shows a not-found state). Types (`NotificationType`): `System`, `ReviewReply`, `ReplyReply`, `AchievementUnlocked`, `NewFollower`. Create them through `INotificationService` (`CreateAsync`, `CreateSystemAsync`, `NotifyNewFollowerAsync`) after the triggering `SaveChangesAsync`; like achievements, creation never throws. Hooks: `ReviewReplyService.CreateReplyAsync` (review author for layer 2, parent reply author for layer 3; never self, never a deleted parent, only if the recipient can still see the review), `FollowerService.FollowUserAsync` (deduplicated per actor for 7 days), the unlock handler, and `SecurityService`/`UserService` system messages. `NotificationController` (`api/Notification`, own notifications only — someone else's is a 404) lists with keyset paging (`status`, `sort`, `afterId`), has a cheap `unseen-count` for the badge polling, and mark seen/unseen, mark all seen, delete, delete all seen. Max 200 per user (oldest seen dropped first). A new type needs an enum value, a creation call where its data changes, and a route in `src/utils/notifications.ts`.
- **Single review / reply**: `GET api/Review/{id}` (with author, hidden == 404) and `GET api/ReviewReply/{id}` (the reply plus its layer-2 parent, deleted == 404) back the `/review/:reviewId?reply=` deep links.
- **Email**: `IAccountEmailService` builds the account emails (`EmailTemplates`: one inline-styled HTML layout + plain-text part, every value HTML-encoded) and only enqueues them on the singleton `EmailQueue` (bounded `Channel`); `EmailDispatcherService` (hosted) sends them with `SmtpEmailSender` (MailKit, `EmailOptions` from the `Email` section) with a couple of retries. Queued mail is lost on restart, which is fine because every email-dependent flow has a resend. Never log email bodies (they contain tokens/codes).
- **Background cleanup**: `DataCleanupService` (hosted, daily) deletes seen notifications older than 90 days and spent/expired `SecurityToken` rows.
- **DTOs**: Request/response shapes live in `DTOs/`, kept separate from EF entities; controllers should not return entities directly.

### Frontend

- **Preact, not React**: the project uses `preact/compat` aliased to `react`/`react-dom` (see `tsconfig.json` paths and `vite.config.ts`'s `@preact/preset-vite`). Write components using standard React/JSX patterns and hooks from `preact/hooks` — they compile through the compat layer.
- **Routing**: all routes are declared in `src/index.tsx` using `react-router-dom`. Most top-level pages are wrapped in `PageContainer` (shared header/footer/theme chrome). The `/profile/:username` route is a layout route (`ProfileLayout`) with nested tab routes (`overview`, `reviews`, `collections`, etc.) rendered inside it.
- **Auth state**: global auth state is provided by `AuthContext`/`AuthProvider` (`src/context/AuthContext.tsx`), backed by `AuthService` (`src/services/AuthService.ts`), which calls the API with `credentials: 'include'` so the httpOnly JWT cookie is sent automatically. Do not attempt to read or store the JWT in JS — the cookie is httpOnly by design. `AuthContext` registers a handler with `apiClient.ts` so that any 401 response clears the logged-in user (e.g. after the session was revoked from another device). `AuthUser.isEmailConfirmed` drives the non-blocking `EmailVerificationBanner` (in `PageContainer`); call `refreshUser()` after anything that changes it. `LoginPage` has a second step for 2FA logins (code, resend with cooldown, back).
- **Services layer**: each backend controller has a matching `src/services/*.ts` file (`AuthService`, `MediaService`, `ReviewService`, `CollectionService`, `FollowerService`, `UserService`, `ReviewReplyService`, `AchievementService`, `NotificationService`, `SecurityService`) that wraps the API calls. They use `apiFetch` from `src/services/apiClient.ts` (a thin `fetch` wrapper that reports 401s to `AuthContext`) — use `apiFetch`, not plain `fetch`. New backend endpoints should get a corresponding method here rather than calling the API directly from components/pages.
- **Review replies UI**: `ReviewCard` renders a collapsed `ReplyThread` (layer 2) whose `ReplyItem`s load their own layer-3 children; both lists use the `useReplyList` hook (`src/hooks/`). `ReplyForm` validates with `isValidReply`/`MAX_REPLY_LENGTH` from `src/utils/validation.ts`. Counts and placeholders are updated locally after create/delete instead of refetching.
- **Achievements UI**: the profile `achievements` tab (`AchievementsTab`) loads every achievement once and filters/sorts client-side; the filter state (`status`, `category` — both repeatable — and `sort`) lives in the URL. `AchievementFilterBar` is sticky from `md` up. `AchievementCard` is a CSS 3D flip card (keyboard operable, no animation under `prefers-reduced-motion`), greyed out until a tier is earned. Icons are `public/achievements/<IconKey>.svg` (placeholder art, falls back to a trophy icon). Status/sort helpers and tier colours are in `src/utils/achievements.ts`. Unlock toasts need no per-component code: `apiFetch` reads the `X-Unlocked-Achievements` header and passes it to `AchievementToastProvider` (`src/context/AchievementToastContext.tsx`), which queues the toasts.
- **Notifications UI**: `NotificationProvider` (`src/context/NotificationContext.tsx`, inside `AuthProvider`) holds the unseen count: loaded on login, polled every 45s while the tab is visible, and refreshed shortly after any successful non-GET `apiFetch` (the user's own actions can notify them). `NotificationBell` in the `Header` opens `NotificationsDialog` (Seen/Unseen toggles, sort, mark all seen, delete all seen with confirmation, "load more", full screen on small screens) with `NotificationCard`s; actions update the list and badge optimistically and roll back on failure. "Take me there" marks the notification seen and navigates to the route from `getNotificationRoute` (`src/utils/notifications.ts`). Reply notifications open `ReviewPage` (`/review/:reviewId?reply=`), which passes a `replyFocus` through `ReviewCard` -> `ReplyThread` -> `ReplyItem` to seed, expand, scroll to and highlight that reply.
- **Email links**: `EmailLinkPage` serves `/verify-email` and `/confirm-email-change`; it POSTs the `token` query param once on mount.
- **Account settings**: the owner-only `AccountSettingsMenu` on the profile header (rendered by `ProfileLayout`) opens the `ChangeUsernameDialog`, `ChangeEmailDialog`, `ChangePasswordDialog`, `SecurityDialog` (verification state + resend, pending email change + cancel, 2FA enable/disable) and `LogoutAllDialog`. Change email/password require the current password; client-side validation helpers (`isValidEmail`, password length limits) live in `src/utils/validation.ts`.
- **Theming**: light/dark mode is managed in `src/index.tsx` via MUI's `ThemeProvider`/`createTheme`, persisted to `localStorage`, and toggled through `PageContainer`.

## Claude Instructions

### Checking Documentation
Always check for up-to-date documentation when implementing features from libraries and frameworks, using the Context7 MCP server, to plan your work.

### Testing Guidelines
If finding it necessary, test the running application using the Playwright MCP server.

### Commit Rules:

- Do NOT add "Co-authored-by" lines to commits
- Do NOT add "Generated with Claude Code" or similar attributions
- Keep commits clean and professional

#### Commit Message Requirements (max 72 chars)

- Imperative mood: "Add" not "Added"
- Action verbs: Add, Update, Fix, Remove, Refactor, Implement
- No articles (a, an, the)
- No punctuation at end
- No prefixes like "feat:", "fix:"
- Single line only
- NO co-author attributions
