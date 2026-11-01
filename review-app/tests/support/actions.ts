import { vi } from 'vitest';

/**
 * Stand-ins for the async callbacks the account dialogs get (onSave / onConfirm), which resolve to { success, message }.
 */
type ActionResult = { success: boolean; message?: string };

export const succeed = () => vi.fn(async (..._args: unknown[]): Promise<ActionResult> => ({ success: true }));

export const fail = (message?: string) => vi.fn(async (..._args: unknown[]): Promise<ActionResult> => ({ success: false, message }));

// Resolved by the test, to assert the in-progress state
export function pending() {
    let resolve!: (result: ActionResult) => void;
    const fn = vi.fn((..._args: unknown[]) => new Promise<ActionResult>(r => { resolve = r; }));
    return { fn, resolve: (result: ActionResult) => resolve(result) };
}
