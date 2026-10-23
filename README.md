# ReviewApp

A full-stack platform for rating and reviewing movies, series and music. Users write scored reviews, discuss them in reply threads, organize titles into collections, follow each other, earn achievements and get in-app notifications. It ships with a web client and a native Android client, both backed by the same ASP.NET Core API.

Movie and series data comes from [TMDb](https://www.themoviedb.org/), music data from [Spotify](https://developer.spotify.com/).

## Contents

- [Features](#features)
- [Tech stack](#tech-stack)
- [Project structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Getting started](#getting-started)
  - [1. Backend](#1-backend-reviewappapi)
  - [2. Web frontend](#2-web-frontend-review-app)
  - [3. Android app](#3-android-app-review-app-android)
- [Configuration reference](#configuration-reference)
- [Testing](#testing)
- [Building for production](#building-for-production)
- [Security overview](#security-overview)
- [Known limitations and roadmap](#known-limitations-and-roadmap)
- [License](#license)

## Features

### Media and reviews

- **Discovery**: search and browse movies, series and music, with a discover page for each media type.
- **Reviews**: a 1–10 score (one decimal place) plus optional text, pros and cons. You can edit or delete your reviews, each title shows its aggregate score, and every review has its own visibility (`Private` / `FollowersOnly` / `Public`).
- **Review replies**: threads up to three levels deep (review → reply → reply to reply), with "load more" paging. Replies follow the visibility of their review, and private reviews can't be replied to. A deleted reply stays as a placeholder while other replies still answer it.
- **Review pages**: every review has its own page (`/review/:id`). With `?reply=` the page opens the thread, scrolls to that reply and highlights it.

### Collections

- Create named collections, add or remove titles, and reorder them by drag and drop. Collections have the same visibility levels as reviews.
- Every account has a permanent **Favourites** collection, which can't be renamed or deleted.

### Social

- Public profiles with bio, avatar and tabs for the user's reviews, collections and achievements.
- Follow and unfollow users, see follower and following lists, remove followers, and search for users.

### Achievements

- Tiered achievements (bronze / silver / gold) for reviews per media type, total reviews, replies, low-score reviews and collections created.
- Filter achievements by status and category and sort them on the profile's Achievements tab. Each card flips to show its details.
- Unlocking a tier shows a toast (web) or banner (Android) right away and also creates a notification. Progress keeps the highest count reached, so deleting content never takes an achievement away.

### Notifications

- In-app notifications for replies to your reviews and replies, new followers, achievement unlocks and account/security events.
- An unseen-count badge in the header, refreshed every 45 seconds and right after your own actions.
- Seen/unseen filters, sorting, mark seen or unseen, mark all as seen, delete, and delete all seen. "Take me there" opens the related review, reply, profile or achievement.

### Account and security

- Registration and login with JWT sessions in an httpOnly cookie.
- **Email verification**: a verification link is sent at sign-up, with a banner and a resend option until the address is verified.
- **Two-factor authentication** with emailed one-time codes. Turning it on or off takes your password and then a code.
- **Account settings**: change your username, email (confirmed through a link sent to the new address, with a notice to the old one) and password, and log out of all devices.
- Changing your password, confirming a new email or logging out everywhere ends every other active session.
- Transactional emails (HTML + plain text) are sent over SMTP in the background.

### General

- Light and dark theme, saved across sessions (the Android app also offers "follow system").
- Responsive web UI. The Android app has bottom navigation, and each tab keeps its own back stack.

### Android app

The native Android client covers the web app's features: auth with 2FA, home feed, search and discover, media details, reviews and reply threads, profiles and follow lists, collections with drag-to-reorder, Favourites and add-to-collection shortcuts on every media card, achievements, notifications and account settings. Sessions persist across restarts in an encrypted cookie store. Review pages also open from `reviewapp://review/{reviewId}?replyId={replyId}` deep links.

## Tech stack

| Layer | Technology |
|-------|------------|
| Backend | ASP.NET Core 8 Web API (C#), Entity Framework Core 8, Swagger (Swashbuckle) |
| Database | SQL Server (a few invariants are enforced by triggers created in migrations) |
| Web frontend | [Preact](https://preactjs.com/) (via `preact/compat`) + TypeScript, [Vite](https://vitejs.dev/), [MUI 7](https://mui.com/), React Router 7, [dnd-kit](https://dndkit.com/) |
| Android | Kotlin, Jetpack Compose + Material 3, Navigation Compose (type-safe routes), Retrofit + OkHttp + kotlinx.serialization, Coil, DataStore, [Reorderable](https://github.com/Calvin-LL/Reorderable). Min SDK 26, target SDK 37 |
| Auth | JWT (HS256) in an httpOnly `__Host-` cookie, server-side revocation, BCrypt password hashing, email-based 2FA |
| Email | MailKit over SMTP, sent by a background queue |
| External APIs | TMDb (movies/series), Spotify Web API (music) |

## Project structure

```
ReviewApp/
├── ReviewApp.Api/              # ASP.NET Core Web API
│   ├── Controllers/            # Auth, Security, User, Media, Review, ReviewReply,
│   │                           # Collection, Follower, Achievement, Notification
│   ├── Services/               # Business logic, TMDb/Spotify clients, email queue
│   │                           # and sender, achievement and notification logic,
│   │                           # background cleanup
│   ├── DAL/                    # AppDbContext, entities, achievement seed data
│   ├── DTOs/                   # Request/response contracts
│   ├── Enums/                  # Shared enums (visibility, media type, tiers, ...)
│   └── Migrations/             # EF Core migrations (including SQL triggers)
├── review-app/                 # Preact + TypeScript web frontend
│   ├── public/achievements/    # Achievement icons (SVG)
│   └── src/
│       ├── components/         # Reusable UI components and dialogs
│       ├── pages/              # Route-level pages
│       ├── services/           # API clients, one per backend controller
│       ├── context/            # Auth, notification and achievement-toast providers
│       ├── hooks/              # Shared hooks (e.g. reply list paging)
│       ├── utils/              # Validation, formatting, achievement and notification helpers
│       └── types/              # Shared TypeScript types
└── review-app-android/         # Native Android client (Kotlin + Compose)
    └── app/src/
        ├── main/java/com/reviewapp/android/
        │   ├── di/             # Manual dependency container
        │   ├── data/           # Retrofit APIs, DTOs, repositories, encrypted session store
        │   ├── domain/         # Domain models
        │   ├── ui/             # Screens + ViewModels per feature, navigation, theme
        │   └── util/           # Validation, formatting, filters (mirroring the web app)
        ├── debug/              # Debug-only network config that trusts the local dev certificate
        └── test/               # JVM unit tests
```

For a deeper look at the architecture and the conventions to follow, see [CLAUDE.md](CLAUDE.md).

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and the EF Core CLI: `dotnet tool install --global dotnet-ef --version 8.*`
- [Node.js](https://nodejs.org/) 18+ and npm
- SQL Server (e.g. LocalDB, which ships with Visual Studio)
- API credentials for [TMDb](https://www.themoviedb.org/settings/api) and [Spotify](https://developer.spotify.com/dashboard) (client ID and secret)
- A local SMTP catcher for development emails, e.g. [Mailpit](https://mailpit.axllent.org/) (optional, but you need it to complete verification, email-change and 2FA flows)
- For the Android app: [Android Studio](https://developer.android.com/studio) with Android SDK 37, JDK 17+ (Android Studio's bundled JBR works) and an emulator or device running Android 8.0 (API 26) or newer

## Getting started

### 1. Backend (`ReviewApp.Api/`)

Create `ReviewApp.Api/appsettings.Development.json`. It is gitignored, so each developer keeps their own copy:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=ReviewAppDb;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Key": "<a long random secret, at least 32 bytes>",
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
dotnet ef database update            # create/update the local database
dotnet run --launch-profile https
```

The API listens on `https://localhost:7140` (and `http://localhost:5062`). Swagger UI is at `https://localhost:7140/swagger` in Development. If this is the first time you use the ASP.NET Core dev certificate on this machine, trust it with `dotnet dev-certs https --trust`.

**Emails in development.** The committed settings send mail to an SMTP server on `localhost:1025` with no authentication. The easiest option is Mailpit:

```bash
docker run -d --name mailpit -p 1025:1025 -p 8025:8025 axllent/mailpit
```

Open `http://localhost:8025` to read the verification links and 2FA codes. If no SMTP server is running, sending fails in the background (and is logged), and the rest of the API keeps working. To send real emails, override the `Email` section in `appsettings.Development.json` (see [Configuration reference](#configuration-reference)).

### 2. Web frontend (`review-app/`)

From `review-app/`:

```bash
npm install
npm start
```

The Vite dev server always serves HTTPS on `https://localhost:5173` (via `@vitejs/plugin-basic-ssl`) and opens the browser. It expects the API at `https://localhost:7140`, so run the backend with the `https` profile. The CORS policy allows the origin configured in `App:FrontendBaseUrl`.

### 3. Android app (`review-app-android/`)

The app calls `https://localhost:7140/api/` and reaches the API on your machine through `adb reverse`, so the hostname matches the ASP.NET dev certificate. This works on both emulators and USB-connected devices.

1. **Export the dev certificate once.** Debug builds trust `app/src/debug/res/raw/aspnet_dev_cert.pem`, which is per developer and gitignored. The export also writes the certificate's **private key**, so export it outside the project and copy only the `.pem` file (everything in `res/raw` gets packaged into the APK):

   ```bat
   dotnet dev-certs https --export-path %TEMP%\aspnet_dev_cert.pem --format Pem --no-password
   copy %TEMP%\aspnet_dev_cert.pem app\src\debug\res\raw\
   del %TEMP%\aspnet_dev_cert.pem %TEMP%\aspnet_dev_cert.key
   ```

2. **Start the API** with the `https` profile (see step 1).

3. **Forward the port.** Repeat this after every emulator or device boot:

   ```bash
   adb reverse tcp:7140 tcp:7140
   ```

4. **Build and install** from `review-app-android/` (on Windows, use `gradlew.bat`):

   ```bash
   ./gradlew installDebug
   ```

   You can also open `review-app-android/` in Android Studio and run the `app` configuration. Release builds trust only the system CAs.

## Configuration reference

All backend settings live in `ReviewApp.Api/appsettings*.json` or environment variables (e.g. `Jwt__Key`).

| Key | Required | Description |
|-----|----------|-------------|
| `ConnectionStrings:DefaultConnection` | Yes | SQL Server connection string |
| `Jwt:Key` | Yes | HMAC signing key. The API won't start if it's missing or shorter than 32 bytes. |
| `Jwt:Issuer`, `Jwt:Audience` | Yes | Token issuer and audience |
| `App:FrontendBaseUrl` | Yes | Web frontend origin, used for CORS and for links in emails. Defaults to `https://localhost:5173`; the API won't start without it. |
| `Tmdb:BaseUrl`, `Tmdb:ApiKey` | Yes | TMDb API access |
| `Spotify:ClientId`, `Spotify:ClientSecret` | Yes | Spotify client-credentials access |
| `Email:Host`, `Email:Port` | Yes | SMTP server (default `localhost:1025`) |
| `Email:Security` | No | `None`, `StartTls` or `SslOnConnect` |
| `Email:Username`, `Email:Password` | No | SMTP credentials (leave empty for no auth) |
| `Email:FromAddress`, `Email:FromName` | Yes | Sender of the account emails |

For a real deployment, generate the JWT key randomly (e.g. `openssl rand -base64 64`) and pass it and the other secrets through environment variables or a secret store, not a committed file.

## Testing

- **Android**: JVM unit tests cover validation, formatting, API JSON parsing, achievement filtering/sorting, reply-thread and notification-list state, collection membership caching and the account-settings forms:

  ```bash
  cd review-app-android
  ./gradlew testDebugUnitTest
  ```

- **Backend and web frontend**: there are no automated test suites yet. Test the API manually through Swagger UI.

## Building for production

```bash
# Backend
cd ReviewApp.Api
dotnet publish -c Release

# Web frontend
cd review-app
npm run build          # outputs to review-app/dist

# Android
cd review-app-android
./gradlew assembleRelease
```

The frontend and the Android app currently have the API address hardcoded to `https://localhost:7140` (`src/services/*` and `BuildConfig.API_BASE_URL` in `app/build.gradle.kts`). Update them before deploying anywhere else. You also need to set up release signing for the Android APK.

## Security overview

- **Sessions**: the JWT is stored only in an httpOnly, `Secure`, `SameSite=Strict`, `__Host-` prefixed cookie (8 h lifetime), never in JavaScript-readable storage. On Android it is encrypted at rest with an Android Keystore AES-GCM key.
- **Revocation**: each token carries a per-user version that is checked on every request. Bumping the version (password change, confirmed email change, "log out of all devices") ends every session, including pending 2FA logins.
- **Security tokens**: email-verification links, email-change links and 2FA codes are stored only as SHA-256 hashes. Each one is single-use and expires. Codes burn after 5 wrong attempts, and resends have a 60 s cooldown. Links are confirmed by a POST from the frontend, so mail scanners that prefetch links can't use them up.
- **Rate limiting**: a global limit of 300 requests per minute, plus stricter policies for login/registration/2FA (10 per 10 min per IP), credential changes (5 per 10 min), email links, email sending and the TMDb/Spotify-backed media endpoints (60 per minute). Rejected requests get `429` with a JSON error body.
- **Other measures**: logins for unknown emails take as long as real ones (checked against a dummy BCrypt hash), so response times don't reveal which emails are registered. Passwords are capped so they stay within BCrypt's 72-byte limit, HSTS is on outside Development, and email bodies are never logged.

## Known limitations and roadmap

- The profile **Overview** tab is a placeholder on the web and doesn't exist on Android yet.
- 2FA uses emailed codes only. **TOTP** (authenticator apps) and **recovery codes** are planned.
- Notifications are in-app only. There are no browser push or Android system notifications yet.
- Email links (verification, email change) open the web frontend, and the Android app doesn't handle them.
- Rate-limit counters and the outgoing email queue are kept in memory, so restarting the API resets them. Every email flow has a resend option.
- No automated tests for the backend or the web frontend yet.

## License

This project is licensed under the [MIT License](LICENSE).
