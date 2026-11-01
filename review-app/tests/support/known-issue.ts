import { it } from 'vitest';

/**
 * A test of the correct behaviour for an open bug listed in tests/FINDINGS.md: skipped in normal runs.
 * `KNOWN_ISSUES=1 npm test` runs them as `it.fails`, which checks that each one still fails against the current code
 * (a known issue that started passing was fixed: un-skip it, i.e. turn it into a plain `it`, and move the finding to "Fixed").
 */
export function knownIssue(id: string, name: string, fn: () => unknown, timeout?: number) {
    const title = `Known issue ${id} (FINDINGS.md): ${name}`;

    if (process.env.KNOWN_ISSUES)
        it.fails(title, fn, timeout);
    else
        it.skip(title, fn, timeout);
}
