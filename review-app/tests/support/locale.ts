/**
 * The app formats dates, prices and sorts titles with the browser's default locale (no explicit locale argument).
 * Under jsdom that is Node's ICU default, which comes from the OS (on Windows it ignores LANG / LC_ALL),
 * so the tests pin it here: every locale-sensitive built-in gets TEST_LOCALE when the caller passes none.
 * The time zone is pinned with process.env.TZ, which Node applies at runtime.
 */
export const TEST_LOCALE = 'en-US';
export const TEST_TIME_ZONE = 'UTC';

type Locales = string | string[] | undefined;

const withLocale = (locales: Locales) => locales ?? TEST_LOCALE;

export function pinLocaleAndTimeZone() {
    process.env.TZ = TEST_TIME_ZONE;

    for (const name of ['DateTimeFormat', 'NumberFormat', 'Collator', 'PluralRules', 'RelativeTimeFormat', 'ListFormat'] as const) {
        const Original = Intl[name] as any;
        const Pinned = function (this: unknown, locales?: Locales, options?: object) {
            return new Original(withLocale(locales), options);
        } as any;
        Pinned.prototype = Original.prototype;
        Pinned.supportedLocalesOf = Original.supportedLocalesOf;
        (Intl as any)[name] = Pinned;
    }

    const datePrototype = Date.prototype as any;
    for (const method of ['toLocaleString', 'toLocaleDateString', 'toLocaleTimeString']) {
        const original = datePrototype[method];
        datePrototype[method] = function (this: Date, locales?: Locales, options?: object) {
            return original.call(this, withLocale(locales), options);
        };
    }

    const originalNumberToLocale = Number.prototype.toLocaleString;
    Number.prototype.toLocaleString = function (this: number, locales?: Locales, options?: object) {
        return originalNumberToLocale.call(this, withLocale(locales), options);
    };

    const originalLocaleCompare = String.prototype.localeCompare;
    String.prototype.localeCompare = function (this: string, that: string, locales?: Locales, options?: object) {
        return originalLocaleCompare.call(this, that, withLocale(locales), options);
    };
}
