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

## Open

| ID | Finding | Why not fixed here | Skipped test |
| --- | --- | --- | --- |
| F1 | A failed TMDb / Spotify lookup (unknown ID, outage) returns an empty DTO instead of null, so `MediaService` stores a junk media row ("Unknown Title" / empty) and reviews or collections attach to it. `GET api/Media/media/{type}/{id}` answers 200 with it too | Needs a decision on the API contract (404 vs 400) for reviews, collections and the media details endpoint, and a cleanup of existing junk rows | `MediaServiceTests.GetOrCreate_UnknownExternalId_FailsWithoutStoringAnything` |
| F2 | `GET api/User/{username}/followers` and `/following` answer 200 `[]` for an unknown user, the other profile lists answer 404 | Changes the API contract; the clients may rely on the empty list | `ControllerSmokeTests.User_FollowerListsOfAnUnknownUser_Are404` |
| F3 | `search` is a valid username, but `GET api/User/search` is the user search route, so that profile can't be opened (usernames have no character rules either, so `/`, `?` or `.` break routing) | Needs reserved names / allowed characters at registration and a plan for existing accounts | `ControllerSmokeTests.User_NamedSearch_HasAReachableProfile` |
| F4 | 429 responses never carry `Retry-After`: the sliding window leases have no `RetryAfter` metadata, so that part of `OnRejected` never runs | Low impact; the header would need to be computed from the policy's window | `RateLimitTests.Rejection_SaysWhenToRetry` |
| F5 | One review per user and media, and one media row per external ID and type, are only checked in code: parallel requests can store duplicates (no unique index). The review race reproduces locally; the media one is timing-dependent and didn't in the runs so far | Needs a migration with unique indexes and a cleanup of possible existing duplicates | `ReviewServiceTests.Create_ConcurrentRequestsForTheSameMedia_StoreOneReview`, `MediaServiceTests.GetOrCreate_ConcurrentFirstRequests_StoreOneRow` |
| F6 | A parallel duplicate follow hits the primary key: one request succeeds, the other throws (a 500) instead of answering "You already follow this user." | Small, but needs a decision on catching `DbUpdateException` in the service layer generally (collections have the same pattern) | `FollowerServiceTests.Follow_ConcurrentDuplicates_OneSucceedsNoneThrows` |

## Observations (no test)

- `AchievementService.EvaluateAsync` calls `ChangeTracker.Clear()` on the request's shared `DbContext` when an evaluation fails. Today every caller saves before evaluating, so nothing is lost, but a future caller with pending changes would silently lose them.
- `ReviewService.CreateReviewAsync` and `CollectionService.AddMediaToCollectionAsync` create the media row before their duplicate checks, so a rejected request can leave an unused media row behind (harmless, it is reused later).
- `ICollectionService.SearchCollectionMediaAsync` is declared but throws `NotImplementedException`; no endpoint calls it.
- A blank `query` on the `api/Media/search/*` endpoints never reaches the controller's own "Query parameter is required." check: model binding treats a whitespace-only value as missing and answers with a validation problem.

## Kept as is (checked, intended)

- Replying inside the thread of a deleted (placeholder) reply is allowed: the web shows the Reply button there, and the reply notification is skipped for a deleted parent on purpose. Covered by `ReviewReplyServiceTests.Create_InsideADeletedRepliesThread_IsAllowedWithoutNotifyingAnyone`.
- The email change notification can contain two long addresses: `NotificationService` truncates messages to the column limit, so it is stored (cut with an ellipsis) rather than dropped.
