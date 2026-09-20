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
- **Auth**: JWT bearer auth, but the token is normally delivered via an httpOnly cookie (`jwt_token`) rather than the `Authorization` header — see the `OnMessageReceived` handler in `Program.cs` that pulls the token from the cookie. `UserAuthHelper` (`IUserAuthHelper`) reads the authenticated user's ID from the `"id"` claim; use `GetSecureUserID()` in endpoints that require auth and `GetOptionalUserID()` where auth is optional (e.g. to vary visibility of results for anonymous vs. logged-in users).
- **Media model (TPH inheritance)**: `Media` is an abstract EF Core entity with `Movie`, `Series`, and `Music` as concrete subtypes, mapped via table-per-hierarchy using the `MediaType` discriminator (configured in `AppDbContext.OnModelCreating`). Media rows are populated from external APIs — `TmdbService` (movies/series, via `ITmdbService`) and `SpotifyService` (music, via `ISpotifyService`) — and cached locally, keyed by `ExternalApiID` + `MediaType`.
- **Database triggers**: Some invariants are enforced by SQL Server triggers created in migrations rather than in C#: `SetUpdatedAt` on `Review` (auto-updates timestamp) and `AddDefaultCollection` on `User` (creates a default collection on user insert). Keep this in mind when reasoning about `Review`/`Collection` state — it isn't always set from application code.
- **Collections**: `Collection` has a per-user unique `Name` constraint. `CollectionMedia` is a join entity with a composite key (`CollectionID`, `MediaID`) and cascade delete from both sides.
- **Followers**: `UserFollower` is a self-referencing join entity on `User` with a composite key (`FollowerID`, `FollowingID`) and `Restrict` delete behavior (to avoid cascade cycles on `User` deletion).
- **DTOs**: Request/response shapes live in `DTOs/`, kept separate from EF entities; controllers should not return entities directly.

### Frontend

- **Preact, not React**: the project uses `preact/compat` aliased to `react`/`react-dom` (see `tsconfig.json` paths and `vite.config.ts`'s `@preact/preset-vite`). Write components using standard React/JSX patterns and hooks from `preact/hooks` — they compile through the compat layer.
- **Routing**: all routes are declared in `src/index.tsx` using `react-router-dom`. Most top-level pages are wrapped in `PageContainer` (shared header/footer/theme chrome). The `/profile/:username` route is a layout route (`ProfileLayout`) with nested tab routes (`overview`, `reviews`, `collections`, etc.) rendered inside it.
- **Auth state**: global auth state is provided by `AuthContext`/`AuthProvider` (`src/context/AuthContext.tsx`), backed by `AuthService` (`src/services/AuthService.ts`), which calls the API with `credentials: 'include'` so the httpOnly JWT cookie is sent automatically. Do not attempt to read or store the JWT in JS — the cookie is httpOnly by design.
- **Services layer**: each backend controller has a matching `src/services/*.ts` file (`AuthService`, `MediaService`, `ReviewService`, `CollectionService`, `FollowerService`, `UserService`) that wraps `fetch` calls to the API. New backend endpoints should get a corresponding method here rather than calling `fetch` directly from components/pages.
- **Theming**: light/dark mode is managed in `src/index.tsx` via MUI's `ThemeProvider`/`createTheme`, persisted to `localStorage`, and toggled through `PageContainer`.
