import { RecognitionOutcome, RecognitionTier, RecognizedMediaDto } from "../types/types";

/** User-facing names of the relatedness tiers, closest first. */
export const TIER_LABELS: Record<RecognitionTier, string> = {
    direct: 'Direct match',
    universe: 'Same universe',
    people: 'Related people',
    similar: 'You might also like'
};

/**
 * The label of a result's tier.
 * @param tier The tier string from the API.
 * @returns The label; an unknown tier falls back to the loosest one, so a tier added by a newer backend still renders.
 */
export function tierLabel(tier: string): string {
    return TIER_LABELS[tier as RecognitionTier] ?? TIER_LABELS.similar;
}

/** Advice shown before taking a photo. */
export const RECOGNITION_TIPS = [
    'Use good light and avoid reflections',
    'Get close, so the object fills most of the photo',
    'Photograph one object at a time',
    'Make any text (titles, names) readable'
];

/**
 * What to say when a recognition has no list to show. The person case is deliberate: people are never identified
 * from their faces, so the user is pointed at objects instead.
 * @param outcome How the recognition ended, from the API.
 * @returns The message.
 */
export function emptyResultMessage(outcome: RecognitionOutcome): string {
    switch (outcome) {
        case 'not_media_related':
            return "This doesn't look related to movies, series or music.";
        case 'person_not_identified':
            return 'People are not identified from their faces. Try a figure, poster, cover or other object instead.';
        case 'recognised':
            return "We recognised it, but couldn't find matching movies, series or music.";
        default:
            return "We couldn't recognise anything in this photo.";
    }
}

/**
 * Appends a "Show more" page to the list on screen. An item that is already shown is never added twice, so
 * overlapping pages (e.g. a retried request) cannot duplicate cards.
 * @param current The items shown now.
 * @param page The newly loaded items.
 * @returns A new array: `current` followed by the items of `page` that are not in it.
 */
export function mergeItems(current: RecognizedMediaDto[], page: RecognizedMediaDto[]): RecognizedMediaDto[] {
    const seen = new Set(current.map(itemKey));
    return [...current, ...page.filter(item => !seen.has(itemKey(item)))];
}

/**
 * The identity of a result: a title is the media type plus its external API ID.
 * @param item The recognized item.
 */
export function itemKey(item: RecognizedMediaDto): string {
    return `${item.media.mediaType}:${item.media.externalApiID}`;
}

/**
 * The allowance chip text, e.g. "14 of 20 left today".
 * @param remaining Recognitions left today.
 * @param limit The daily limit.
 * @returns The text, or an empty string when either value is unknown (Free users have no counters).
 */
export function allowanceText(remaining?: number | null, limit?: number | null): string {
    if (remaining == null || limit == null) return '';
    return `${remaining} of ${limit} left today`;
}

/**
 * The local time the allowance resets, e.g. "01:00". The API gives a UTC instant (midnight UTC).
 * @param isoDate The ISO 8601 timestamp from the API.
 * @returns The time in the viewer's locale, or an empty string when there is none.
 */
export function formatResetTime(isoDate?: string | null): string {
    if (!isoDate) return '';
    return new Date(isoDate).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}
