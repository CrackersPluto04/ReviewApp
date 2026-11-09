# Backend test findings

Bugs and oddities found while writing the backend test suite (`feature/backend-tests`).
Small, clearly wrong ones were fixed on the branch; the test that found them stays as a regression test.
The others are open: each has a skipped test (`Skip = "Known issue Fx (FINDINGS.md) ..."`) that describes the correct behavior.
Un-skipping that test is the first step of the fix. Each skipped test was checked to fail against the current code (except the timing-dependent media race in F5).

## Fixed

| Finding | Fix | Test |
| --- | --- | --- |
| `PagedResponse.TotalPages` was `(int)NaN` (int.MinValue) or negative without a valid page size, e.g. an empty `search/all` | No valid page size means 0 pages | `PagedResponseTests` |
| Spotify discover put `market` into the URL unescaped, so a crafted value could add query parameters | Escaped like the other values | `ExternalApiClientTests.Spotify_DiscoverEscapesEveryUserValue` |
| A wrong 2FA code racing the right one could mark the just used code unused again (second use possible) | The failed-attempt update only touches unused codes | `SecurityServiceTests.TwoFactorCode_WrongTriesRacingTheRightOne_NeverReviveIt` |
| Review lists failed with a 500 for `page <= 0` or `pageSize <= 0` (negative `Skip`) and had no page size limit | Page is at least 1, page size is clamped to 1-50 (like replies and notifications) | `ReviewServiceTests.Paging_*` |
| Updating a collection always answered `MediaCount = 0` (the items were never loaded) | Counted in the database | `CollectionServiceTests.Update_RenamesAndKeepsTheMediaCount` |
| A case-only collection rename (`sci-fi` -> `Sci-Fi`) was rejected, the name check matched the collection itself (case-insensitive collation) | The check excludes the collection being renamed, like the username check does | `CollectionServiceTests.Update_CaseOnlyRename_IsAllowed` |
| `GetUserCollectionsAsync` returned `null!` for an unknown user behind a non-nullable return type | The return type is nullable, so callers see the not found case | `CollectionServiceTests.List_UnknownUser_IsNull` |
| The "email queue is full" error was never logged: with `DropWrite` the channel drops silently and `TryWrite` still returns true | `Wait` mode, so `TryWrite` reports the full queue (nothing awaits a write, so dropping is unchanged) | `EmailPipelineTests.Queue_WhenFull_DropsAndLogsTheEmail` |
| `PATCH api/Notification/{id}/seen` with `{}` silently marked the notification unseen (`[Required]` on a plain `bool` does nothing) | `IsSeen` is `bool?`, a missing value is a 400 (both clients always send it) | `ControllerSmokeTests.Notification_SetSeenWithoutAValue_IsRejected` |
| F1: a failed TMDb / Spotify lookup (unknown ID, outage) stored a junk media row ("Unknown Title" / empty) that reviews and collections attached to; `GET api/Media/media/{type}/{id}` answered 200 with it | Strict lookups by ID (`FindMovieByIdAsync` / `FindSeriesByIdAsync` / `FindTrackByIdAsync`) and `MediaService.ResolveAsync` / `LookupAsync`: nothing is stored unless the source knows the ID. Unknown = 404 `{ error, code: "media_not_found" }`, outage = 503 `{ error, code: "source_unavailable" }` (`MediaErrorFilter` for reviews and collection adds). The daily cleanup deletes unused junk rows and fetches the used ones again | `MediaServiceTests.GetOrCreate_UnknownExternalId_FailsWithoutStoringAnything`, `Resolve_*`, `ControllerSmokeTests.Media_UnknownOrUnavailableTitles_AreReportedNotStored`, `BackgroundJobTests.Cleanup_DeletesUnusedIncompleteMediaAndRepairsUsedOnes` |
| F2: `GET api/User/{username}/followers` and `/following` answered 200 `[]` for an unknown user, the other profile lists 404 | 404 "User profile not found." like the other lists; the web and Android follower lists show their error state | `ControllerSmokeTests.User_FollowerListsOfAnUnknownUser_Are404`, `UserServiceTests.FollowerLists_*` |
| F3: `search` was a valid username although `GET api/User/search` is the user search route, and usernames had no character rules (`/`, `?`, `.` broke routing) | `UsernameRules`: 3-20 characters of letters, digits, `_` and `-`, plus a reserved list, at registration and username change (mirrored by both clients). The `RenameInvalidUsernames` migration renames existing offending accounts to `user_<id>` and notifies them | `UsernameRulesTests`, `ControllerSmokeTests.User_UsernamesThatBreakUrls_AreRejected` |
| F4: 429 responses never carried `Retry-After` (sliding window leases have no `RetryAfter` metadata) | `RateLimitRetry`: one segment of the longer window that applies (the endpoint's policy or the global one) | `RateLimitTests.Rejection_SaysWhenToRetry`, `RateLimitRetryTests` |
| F5: one review per user and media, and one media row per external ID and type, were only checked in code, so parallel requests could store duplicates | Unique indexes (`AddUniqueMediaAndReviewIndexes`, which first merges duplicate media and keeps the newest duplicate review); the services catch the violation and answer like the sequential case | `ReviewServiceTests.Create_ConcurrentRequestsForTheSameMedia_StoreOneReview`, `MediaServiceTests.GetOrCreate_ConcurrentFirstRequests_StoreOneRow` |
| F6: a parallel duplicate follow hit the primary key and threw (a 500) instead of "You already follow this user." | `DbErrors.IsUniqueViolation` is caught in `FollowerService` (and in collection create / add, same pattern) | `FollowerServiceTests.Follow_ConcurrentDuplicates_OneSucceedsNoneThrows` |

## Open

None. F1-F6 were fixed on `feature/offline-mode-mobile-sync` (the sync needs F5 and F6); they are in the table above.

## Observations (no test)

- `AchievementService.EvaluateAsync` calls `ChangeTracker.Clear()` on the request's shared `DbContext` when an evaluation fails. Today every caller saves before evaluating, so nothing is lost, but a future caller with pending changes would silently lose them.
- `ReviewService.CreateReviewAsync` and `CollectionService.AddMediaToCollectionAsync` create the media row before their duplicate checks, so a rejected request can leave an unused media row behind (harmless, it is reused later).
- `ICollectionService.SearchCollectionMediaAsync` is declared but throws `NotImplementedException`; no endpoint calls it.
- A blank `query` on the `api/Media/search/*` endpoints never reaches the controller's own "Query parameter is required." check: model binding treats a whitespace-only value as missing and answers with a validation problem.

## Kept as is (checked, intended)

- Replying inside the thread of a deleted (placeholder) reply is allowed: the web shows the Reply button there, and the reply notification is skipped for a deleted parent on purpose. Covered by `ReviewReplyServiceTests.Create_InsideADeletedRepliesThread_IsAllowedWithoutNotifyingAnyone`.
- The email change notification can contain two long addresses: `NotificationService` truncates messages to the column limit, so it is stored (cut with an ellipsis) rather than dropped.
