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
- HTTPS profile listens on `https://localhost:7140` (also `http://localhost:5062`); the frontend's API clients and CORS policy (`Program.cs`) are hardcoded to `https://localhost:7140`, so run the API with the `https` profile when working end-to-end with the frontend.
- Local connection strings and third-party API keys (TMDb, Spotify) live in `appsettings.Development.json`, which is gitignored (not committed) — each developer creates their own copy locally.

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
  - **Revocation (`TokenVersion`)**: `User.TokenVersion` is copied into the `ver` claim; `OnTokenValidated` in `Program.cs` compares it with the DB on every authenticated request and rejects mismatches (missing `ver` = 0). Incrementing `TokenVersion` logs the user out everywhere — this happens on password change (`PUT /api/User/me/password`, which then re-issues a cookie for the current browser) and on `POST /api/Auth/logout-all`. Any new "invalidate all sessions" feature should do the same.
  - **Login** verifies unknown emails against a dummy BCrypt hash so response time doesn't reveal registered emails. Passwords are capped at 20 characters in DTOs and in `src/utils/validation.ts` — this keeps them under BCrypt's 72-byte limit even with multi-byte characters (C# and JS both count UTF-16 units, each at most 3 UTF-8 bytes), so no separate byte check is needed. Don't raise it above 24 without adding one.
  - **Credentials in transit**: email/password are sent as plain JSON over HTTPS by design (visible in the browser's own DevTools, encrypted on the wire) — don't add client-side hashing/encryption. HSTS (`UseHsts`) is enabled outside Development.
- **Rate limiting**: configured in `Program.cs` (`AddRateLimiter`, `app.UseRateLimiter()` after auth so logged-in users are partitioned by `id`, anonymous ones by IP). A global limit (300/min) applies to every request, plus named policies applied with `[EnableRateLimiting("...")]`: `auth-attempt` (login/register, 10 per 10 min per IP), `credential-change` (change email/password, 5 per 10 min), `external-api` (`MediaController`, 60/min — protects the TMDb/Spotify quotas). Rejections are 429 with a JSON `{ error }` body. Counters are in-memory, so restarting the API resets them.
- **Media model (TPH inheritance)**: `Media` is an abstract EF Core entity with `Movie`, `Series`, and `Music` as concrete subtypes, mapped via table-per-hierarchy using the `MediaType` discriminator (configured in `AppDbContext.OnModelCreating`). Media rows are populated from external APIs — `TmdbService` (movies/series, via `ITmdbService`) and `SpotifyService` (music, via `ISpotifyService`) — and cached locally, keyed by `ExternalApiID` + `MediaType`.
- **Database triggers**: Some invariants are enforced by SQL Server triggers created in migrations rather than in C#: `SetUpdatedAt` on `Review` (auto-updates timestamp) and `AddDefaultCollection` on `User` (creates a default collection on user insert). Keep this in mind when reasoning about `Review`/`Collection` state — it isn't always set from application code.
- **Collections**: `Collection` has a per-user unique `Name` constraint. `CollectionMedia` is a join entity with a composite key (`CollectionID`, `MediaID`) and cascade delete from both sides.
- **Followers**: `UserFollower` is a self-referencing join entity on `User` with a composite key (`FollowerID`, `FollowingID`) and `Restrict` delete behavior (to avoid cascade cycles on `User` deletion).
- **Review replies**: `ReviewReply` (`ReviewReplyService` / `ReviewReplyController`, `api/ReviewReply`) threads are max 3 layers: review -> reply (`ParentReplyID` null) -> reply to reply. The layer is derived from the parent chain (no column); the service rejects replying to a reply that already has a parent. Replies have no visibility of their own — every read/write checks the parent review's visibility (hidden == 404), and private reviews can't be replied to. Deleting a reply is a soft delete (`IsDeleted`, content kept in the DB but never returned); a deleted reply is only listed as a placeholder while it has non-deleted children. Deleting a review cascades to all its replies in the DB (`ParentReply`/`User` FKs are `Restrict`). Listing uses keyset paging (`afterId`) for "load more"; review list responses include a non-deleted `ReplyCount`.
- **Achievements**: `Achievement` holds one row per tier (bronze/silver/gold); the tiers of one card share a `GroupCode` and a `Metric` (`AchievementMetric`, what is counted). `Categories` is a `[Flags]` enum (`AchievementCategories`), so one achievement can be in several categories. Definitions are seeded with `HasData` from `DAL/AchievementSeed.cs` — keep the IDs stable and add new tiers with new IDs plus a migration. `UserAchievement` (cascade deleted with the user) stores `CurrentProgress` and `IsUnlocked`; a missing row means zero progress.
  - **Evaluation**: `AchievementService.EvaluateAsync(userId, metrics...)` recounts the real totals and is called after `SaveChangesAsync` in the review, reply and collection services (deleting a review also re-evaluates `Replies` for everyone who replied, because their replies are cascade deleted). Progress can go down, but unlocks are permanent. It never throws: failures are logged and swallowed, so the triggering action still succeeds. Counting rules live in `AchievementService.CountAsync` and are mirrored by the backfill SQL in the `AddAchievements` migration — reviews of every visibility count, replies exclude soft-deleted ones, low score is `Score <= 5.0`, and collections exclude the default `Favourites`. A new metric needs a case there and a hook where its data changes.
  - **Unlock delivery**: newly unlocked tiers go to every registered `IAchievementUnlockHandler`. `ResponseHeaderUnlockHandler` writes them (only for the requesting user) to the `X-Unlocked-Achievements` response header as JSON; the header is exposed in the CORS policy. Add a new handler (e.g. for notifications) instead of changing the service. `GET api/Achievement/{username}` is public.
- **DTOs**: Request/response shapes live in `DTOs/`, kept separate from EF entities; controllers should not return entities directly.

### Frontend

- **Preact, not React**: the project uses `preact/compat` aliased to `react`/`react-dom` (see `tsconfig.json` paths and `vite.config.ts`'s `@preact/preset-vite`). Write components using standard React/JSX patterns and hooks from `preact/hooks` — they compile through the compat layer.
- **Routing**: all routes are declared in `src/index.tsx` using `react-router-dom`. Most top-level pages are wrapped in `PageContainer` (shared header/footer/theme chrome). The `/profile/:username` route is a layout route (`ProfileLayout`) with nested tab routes (`overview`, `reviews`, `collections`, etc.) rendered inside it.
- **Auth state**: global auth state is provided by `AuthContext`/`AuthProvider` (`src/context/AuthContext.tsx`), backed by `AuthService` (`src/services/AuthService.ts`), which calls the API with `credentials: 'include'` so the httpOnly JWT cookie is sent automatically. Do not attempt to read or store the JWT in JS — the cookie is httpOnly by design. `AuthContext` registers a handler with `apiClient.ts` so that any 401 response clears the logged-in user (e.g. after the session was revoked from another device).
- **Services layer**: each backend controller has a matching `src/services/*.ts` file (`AuthService`, `MediaService`, `ReviewService`, `CollectionService`, `FollowerService`, `UserService`, `ReviewReplyService`, `AchievementService`) that wraps the API calls. They use `apiFetch` from `src/services/apiClient.ts` (a thin `fetch` wrapper that reports 401s to `AuthContext`) — use `apiFetch`, not plain `fetch`. New backend endpoints should get a corresponding method here rather than calling the API directly from components/pages.
- **Review replies UI**: `ReviewCard` renders a collapsed `ReplyThread` (layer 2) whose `ReplyItem`s load their own layer-3 children; both lists use the `useReplyList` hook (`src/hooks/`). `ReplyForm` validates with `isValidReply`/`MAX_REPLY_LENGTH` from `src/utils/validation.ts`. Counts and placeholders are updated locally after create/delete instead of refetching.
- **Achievements UI**: the profile `achievements` tab (`AchievementsTab`) loads every achievement once and filters/sorts client-side; the filter state (`status`, `category` — both repeatable — and `sort`) lives in the URL. `AchievementFilterBar` is sticky from `md` up. `AchievementCard` is a CSS 3D flip card (keyboard operable, no animation under `prefers-reduced-motion`), greyed out until a tier is earned. Icons are `public/achievements/<IconKey>.svg` (placeholder art, falls back to a trophy icon). Status/sort helpers and tier colours are in `src/utils/achievements.ts`. Unlock toasts need no per-component code: `apiFetch` reads the `X-Unlocked-Achievements` header and passes it to `AchievementToastProvider` (`src/context/AchievementToastContext.tsx`), which queues the toasts.
- **Account settings**: the owner-only `AccountSettingsMenu` on the profile header (rendered by `ProfileLayout`) opens the `ChangeUsernameDialog`, `ChangeEmailDialog`, `ChangePasswordDialog` and `LogoutAllDialog`. Change email/password require the current password; client-side validation helpers (`isValidEmail`, password length limits) live in `src/utils/validation.ts`.
- **Theming**: light/dark mode is managed in `src/index.tsx` via MUI's `ThemeProvider`/`createTheme`, persisted to `localStorage`, and toggled through `PageContainer`.

## Claude Instructions

## Checking Documentation
Always check for up-to-date documentation when implementing features from libraries and frameworks, using the Context7 MCP server, to plan your work.

## Testing Guidelines
If finding it necessary, test the running application using the Playwright MCP server.

## Commit Rules:

- Do NOT add "Co-authored-by" lines to commits
- Do NOT add "Generated with Claude Code" or similar attributions
- Keep commits clean and professional

### Commit Message Requirements (max 72 chars)

- Imperative mood: "Add" not "Added"
- Action verbs: Add, Update, Fix, Remove, Refactor, Implement
- No articles (a, an, the)
- No punctuation at end
- No prefixes like "feat:", "fix:"
- Single line only
- NO co-author attributions
