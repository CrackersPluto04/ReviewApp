import { RecognitionOutcome, RecognitionTier, RecognizedMediaDto } from "../types/types";

// User-facing names of the relatedness tiers, closest first
export const TIER_LABELS: Record<RecognitionTier, string> = {
    direct: 'Direct match',
    universe: 'Same universe',
    people: 'Related people',
    similar: 'You might also like'
};

export function tierLabel(tier: string): string {
    return TIER_LABELS[tier as RecognitionTier] ?? TIER_LABELS.similar;
}

export const RECOGNITION_TIPS = [
    'Use good light and avoid reflections',
    'Get close, so the object fills most of the photo',
    'Photograph one object at a time',
    'Make any text (titles, names) readable'
];

// What to say when there is no list to show
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

// "Show more" pages are appended; an item that is already on screen is never added twice
export function mergeItems(current: RecognizedMediaDto[], page: RecognizedMediaDto[]): RecognizedMediaDto[] {
    const seen = new Set(current.map(itemKey));
    return [...current, ...page.filter(item => !seen.has(itemKey(item)))];
}

export function itemKey(item: RecognizedMediaDto): string {
    return `${item.media.mediaType}:${item.media.externalApiID}`;
}

// e.g. "14 of 20 left today"
export function allowanceText(remaining?: number | null, limit?: number | null): string {
    if (remaining == null || limit == null) return '';
    return `${remaining} of ${limit} left today`;
}

// The local time the allowance resets, e.g. "01:00"
export function formatResetTime(isoDate?: string | null): string {
    if (!isoDate) return '';
    return new Date(isoDate).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}
