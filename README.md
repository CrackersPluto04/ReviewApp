# ReviewApp

A full-stack web app for rating and reviewing movies, series, and music, organizing them into collections, and following other users. Movie/series data is sourced from [TMDb](https://www.themoviedb.org/) and music data from [Spotify](https://developer.spotify.com/).

## Features

- **Auth** — registration and login with JWT-based sessions delivered via an httpOnly cookie.
- **Media discovery** — search and browse movies, series, and music (via TMDb and Spotify), with dedicated discover pages per media type.
- **Reviews** — write, edit, and delete reviews with ratings; view aggregate scores per title; per-review visibility (`Private` / `Public` / `FollowersOnly`).
- **Collections** — create named collections of media, add/remove items, and reorder them by drag-and-drop; same visibility controls as reviews.
- **Profiles** — public user profiles with bio, avatar, and tabs for a user's reviews and collections.
- **Social** — follow/unfollow users, view followers and following lists, remove unwanted followers, and search for users.
- **Light/dark theme**, persisted across sessions.

## Tech stack

| Layer    | Technology |
|----------|------------|
| Frontend | [Preact](https://preactjs.com/) + TypeScript, [Vite](https://vitejs.dev/), [MUI](https://mui.com/), React Router, [dnd-kit](https://dndkit.com/) |
| Backend  | ASP.NET Core 8 Web API (C#), Entity Framework Core |
| Database | SQL Server |
| External APIs | TMDb (movies/series), Spotify (music) |
| Auth | JWT bearer tokens in an httpOnly cookie, BCrypt password hashing |

## Project structure

```
ReviewApp/
├── ReviewApp.Api/     # ASP.NET Core Web API (backend)
│   ├── Controllers/   # Auth, User, Media, Review, Collection, Follower
│   ├── Services/      # Business logic + TMDb/Spotify integrations
│   ├── DAL/            # EF Core DbContext and entities
│   ├── DTOs/           # Request/response contracts
│   └── Migrations/     # EF Core migrations
└── review-app/         # Preact + TypeScript frontend
    └── src/
        ├── components/  # Reusable UI components
        ├── pages/        # Route-level pages
        ├── services/     # API clients (one per backend controller)
        └── context/      # Auth context/provider
```

See [CLAUDE.md](CLAUDE.md) for a more detailed architecture overview.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) (18+) and npm
- SQL Server (e.g. LocalDB, which ships with Visual Studio)
- API credentials for [TMDb](https://www.themoviedb.org/settings/api) and [Spotify](https://developer.spotify.com/dashboard) (client ID/secret)

## Getting started

### 1. Backend (`ReviewApp.Api/`)

Add your local configuration to `ReviewApp.Api/appsettings.Development.json` (gitignored, not committed):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=ReviewAppDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Key": "<a long random secret>",
    "Issuer": "ReviewApp.Api",
    "Audience": "review-app"
  },
  "Tmdb": {
    "BaseUrl": "https://api.themoviedb.org/3/",
    "ApiKey": "<your TMDb API key>"
  },
  "Spotify": {
    "ClientId": "<your Spotify client ID>",
    "ClientSecret": "<your Spotify client secret>"
  }
}
```

Then, from `ReviewApp.Api/`:

```bash
dotnet restore
dotnet ef database update   # creates/updates the local database
dotnet run --launch-profile https
```

The API is served at `https://localhost:7140` (Swagger UI at `/swagger`).

### 2. Frontend (`review-app/`)

From `review-app/`:

```bash
npm install
npm start
```

The dev server runs at `https://localhost:5173` and expects the API to be running at `https://localhost:7140` (see CORS policy in `ReviewApp.Api/Program.cs`).

## Building for production

```bash
# Backend
cd ReviewApp.Api
dotnet build -c Release

# Frontend
cd review-app
npm run build   # outputs to review-app/dist
```

## License

This project is licensed under the [MIT License](LICENSE).
