# Backend test findings

Bugs and oddities found while writing the backend test suite (`feature/backend-tests`).
Small, clearly wrong ones were fixed on the branch; the test that found them stays as a regression test.
The others are open: each has a skipped test (`Skip = "Known issue Fx (FINDINGS.md) ..."`) that describes the correct behavior.
Un-skipping that test is the first step of the fix.

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

## Open

| ID | Finding | Why not fixed here | Skipped test |
| --- | --- | --- | --- |
| F1 | A failed TMDb / Spotify lookup (unknown ID, outage) returns an empty DTO instead of null, so `MediaService` stores a junk media row ("Unknown Title" / empty) and reviews or collections attach to it | Needs a decision on the API contract (404 vs 400) for reviews, collections and the media details endpoint, and a cleanup of existing junk rows | `MediaServiceTests.GetOrCreate_UnknownExternalId_FailsWithoutStoringAnything` |

## Kept as is (checked, intended)

- Replying inside the thread of a deleted (placeholder) reply is allowed: the web shows the Reply button there, and the reply notification is skipped for a deleted parent on purpose. Covered by `ReviewReplyServiceTests.Create_InsideADeletedRepliesThread_IsAllowedWithoutNotifyingAnyone`.
