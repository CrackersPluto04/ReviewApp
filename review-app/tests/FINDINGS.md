# Frontend test findings

Bugs and oddities found while writing the frontend test suite (`feature/frontend-tests`).
By decision none were fixed on the branch: the only production changes are the ones that make the app testable
(`src/App.tsx` taken out of `src/index.tsx`, the `tsconfig.json` exclude, the redundant `https: true` in `vite.config.ts`).

Each open finding has one or more skipped tests named `Known issue Wx (FINDINGS.md): ...` (the `knownIssue` helper in
`tests/support/known-issue.ts`) that describe the correct behaviour. `KNOWN_ISSUES=1 npm test` runs them as expected
failures, which checks that every one of them still fails against the current code; it was run when they were written. In that mode the run ends with unhandled errors from W10, whose page crashes outside the test (that crash is the bug).
Fixing a finding = turn its `knownIssue(...)` into a plain `it(...)`, make it pass, and move the finding to "Fixed".

IDs start with W (web) so they never clash with the backend's F1-F6 (`ReviewApp.Api.Tests/FINDINGS.md`).

## Fixed

None yet.

## Open

| ID | Finding | Impact | Skipped tests |
| --- | --- | --- | --- |
| W12 | `ReviewPage` without `?reply=` reloads the review on every render: `replyId` is `NaN`, it is an effect dependency, and Preact compares dependencies with `!==` (`NaN !== NaN`), unlike React's `Object.is`. A hover alone sent 30+ requests in the test | **High**: an endless request loop against `GET api/Review/{id}` while the page is open (reached from a notification that has a review but no reply, or a typed URL); the thread loses its state on each reload | `ReviewPage.test.tsx` "opened without ?reply= it loads the review once" |
| W19 | The 2FA fields of `SecurityDialog` (mounted later, per step) get the same id from Preact's `useId` as the dialog title, so `<label for>` points at the `<h2>` | Medium (accessibility): the code / password field has no accessible name for screen readers, clicking the label doesn't focus it | `SecurityDialog.test.tsx` "the two-factor code field is labelled by its own label" |
| W5 | `EmailVerificationBanner` passes both `action` and `onClose` to MUI's `Alert`, which renders no close button when there is an action | Medium: the "dismiss for this session" feature is unreachable until the user resends the email | `EmailVerificationBanner.test.tsx` "the reminder can be dismissed" |
| W10 | `MediaReviewPage`: a media whose details request fails renders `MediaDetails` with `undefined` and crashes the page; when the URL changes to another media while the first one came in the router state, the old media stays (the fetch is skipped while `media` is set) | Medium: blank page on a bad link or an API outage | `MediaReviewPage.test.tsx` (2) |
| W11 | Review filters: `MediaDetails` disables the filter whenever the list is empty, so a filter that matches nothing can't be changed or cleared (only by editing the URL); `ReviewFilter` copies its props into state once, so it doesn't follow the URL (browser Back) | Medium: the user gets stuck with an empty list | `MediaDetails.test.tsx` (2) |
| W2 | Many service methods call `response.json()` on error responses without a guard: an error page that isn't JSON (a proxy's 502, an HTML 500) throws, and the user sees the *network* message ("Network error while ...") instead of the method's error fallback. Affected: every `CollectionService`, `FollowerService`, `AchievementService`, `ReviewService`, `MediaService` method, `ReviewReplyService` (except `getReplyContext`), most `UserService` reads and `updateMyProfile`, `AuthService.register`. The services with a `getErrorMessage` helper (`Security`, `Subscription`, `Notification`, account changes) are fine | Low-medium: a misleading message during outages | `Known issue W2` in `tests/services/*` (one per affected method call, 40 in total) |
| W7 | `WriteEditReview`: a failed create still switches to edit mode (the next save is a `PUT` of a review that doesn't exist), a failed update shows nothing, and the `error` flag is never reset, so a later success is shown as an error toast | Medium: silent failures when saving a review | `WriteEditReview.test.tsx` (3) |
| W8 | `SearchPage`: the error message is never cleared (a later successful search with no results still shows it), a failed search keeps showing the previous results, and the "all" tab shows pagination but always asks the API for page 1 | Low-medium | `SearchPage.test.tsx` (3) |
| W15 | `ProfileLayout`: any failed action (follow, bio, avatar) sets the page-level error, which replaces the whole profile with the message (the follow is rolled back, but invisibly) | Medium: one failed click and the profile is gone until reload | `ProfileLayout.test.tsx` (2) |
| W16 | `ProfileLayout` refetches the profile whenever the logged-in user object changes (the effect depends on `user`): saving the avatar (which updates the user) reloads the whole profile and remounts the tab below it | Low: a spinner flash and lost tab state after an avatar change; also on every login | `ProfileLayout.test.tsx` "saving the avatar does not reload the whole profile" |
| W13 | `CollectionsTab` keeps the selected collection when the profile changes (`!selectedCollectionId` guard), so another user's profile shows the previous user's collection | Low-medium: wrong content after navigating between profiles | `CollectionsTab.test.tsx` "another user's profile opens their own first collection" |
| W14 | `CollectionDetails`: the reorder after a drag is fire-and-forget inside a state updater, with no error handling or rollback | Low: the order shown can differ from the stored one after a failure | `CollectionDetails.reorder.test.tsx` "a failed reorder puts the media back and says so" |
| W1 | `useReplyList`: the double-call guard reads `loading` from the render closure (two calls in one tick both pass), a malformed response body throws inside `loadMore` and leaves `loading` true forever, and the list isn't reset when the review / parent id changes | Low: duplicate requests on fast double clicks, a stuck spinner on a bad response | `useReplyList.test.ts` (3) |
| W4 | `MediaService.searchMusic`: a failed backend page ends the loop and the method still returns `success: true` with the tracks collected so far (and a page count that suggests more) | Low: a partial result instead of an error during Spotify outages | `MediaService.test.ts` "a failed backend page is reported as a failure, not as a shorter result" |
| W6 | `LoginPage`: registering doesn't set `submitting`, so the Register button stays enabled and a double click sends two registrations | Low (the backend rejects the duplicate) | `LoginPage.test.tsx` "the register button is disabled while registering" |
| W17 | `EditAvatarDialog` initialises its draft from the prop once: a cancelled edit is still there when the dialog is opened again | Low | `EditAvatarDialog.test.tsx` "a cancelled draft is gone when the dialog is opened again" |
| W18 | `FollowerListDialog` never clears its error: an old error stays above a list that loaded fine on the next open | Low | `FollowerListDialog.test.tsx` "an earlier error is gone when the list is opened again" |
| W3 | `FollowerService`: the network messages read "Network error while reaching trying to follow." and every method logs "Follow error:" | Cosmetic | `FollowerService.test.ts` (2) |
| W9 | `MediaCard` leaves a `console.log` of the collection id and media in the Favourites handler | Cosmetic | `MediaCard.test.tsx` "adding to Favourites leaves no debug output in the console" |

## Observations (no test)

- Several selects have an `InputLabel` without `labelId` (discover filters, review filter, write review, collection dialog, collections sort, achievements sort), so they have no accessible name; the tests find them by the value they show. The theme toggle in the header and the collection add / edit / delete icon buttons have no `aria-label` either.
- `ReviewsTab` passes `disabled={reviews.length === 0 && loading && !!errorMessage}` to its filter; `loading` and an error message are never set together, so it is always `false` (harmless: the filter there is always usable, unlike W11).
- `MediaService`, `ReviewService.getMediaReviews` and `getAverageScore` send no `credentials`. Harmless today: those endpoints are anonymous and answer the same for everyone (the media review list is public reviews only). `ReviewService.getAverageScore` / `checkIfUserReviewedMedia` and `MediaService.getMediaDetails` put ids into the URL without encoding (external ids are alphanumeric today).
- `ReplyItem`'s reply field uses `autoFocus`; React focuses such an element on mount, Preact only sets the attribute (and jsdom implements neither), so whether the field gets focus in the browser wasn't verified here.
- The profile tab of a review list shows the dates as the backend sends them (`yyyy-MM-dd`), the media review list formats them with the viewer's locale: two styles for the same data.
- `npm audit` reports 11 advisories in the existing dev tooling (Vite / esbuild / Babel / PostCSS dependency chain), none new with the test packages; `npm audit fix` was not run on this branch.
