import { describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/preact';
import { ReplyForm } from '../../src/components/ReplyForm';
import { server } from '../support/server';
import { apiError, capture, deferred, ok } from '../support/api';
import { reply } from '../support/builders';
import { setupUser } from '../support/render';
import { MAX_REPLY_LENGTH } from '../../src/utils/validation';

function renderForm(props: Partial<Parameters<typeof ReplyForm>[0]> = {}) {
    const onCreated = vi.fn();
    const user = setupUser();
    render(<ReplyForm reviewId={7} onCreated={onCreated} {...props} />);
    return { user, onCreated, box: screen.getByRole('textbox') };
}

describe('ReplyForm', () => {
    it('posts the trimmed reply and clears the field', async () => {
        const created = reply({ id: 50, content: 'Agreed!' });
        const create = capture('post', 'ReviewReply', ok(created));
        server.use(create.handler);
        const { user, onCreated, box } = renderForm();

        await user.type(box, '  Agreed!  ');
        await user.click(screen.getByRole('button', { name: 'Reply' }));

        await waitFor(() => expect(onCreated).toHaveBeenCalledWith(created));
        expect(create.last.body).toEqual({ reviewID: 7, parentReplyID: null, content: 'Agreed!' });
        expect(box).toHaveValue('');
    });

    it('replies to a reply', async () => {
        const create = capture('post', 'ReviewReply', ok(reply()));
        server.use(create.handler);
        const { user, box } = renderForm({ parentReplyId: 30, replyingTo: 'carol' });

        expect(box).toHaveAttribute('placeholder', 'Reply to carol...');
        await user.type(box, 'Hi carol');
        await user.click(screen.getByRole('button', { name: 'Reply' }));

        await waitFor(() => expect(create.last.body).toEqual({ reviewID: 7, parentReplyID: 30, content: 'Hi carol' }));
    });

    it('counts the trimmed characters', async () => {
        const { user, box } = renderForm();

        expect(screen.getByText(`0/${MAX_REPLY_LENGTH}`)).toBeInTheDocument();
        await user.type(box, '  abc ');

        expect(screen.getByText(`3/${MAX_REPLY_LENGTH}`)).toBeInTheDocument();
    });

    it('cannot send an empty or whitespace reply', async () => {
        const { user, box } = renderForm();

        expect(screen.getByRole('button', { name: 'Reply' })).toBeDisabled();
        await user.type(box, '   ');
        expect(screen.getByRole('button', { name: 'Reply' })).toBeDisabled();
    });

    it('marks a too long reply and blocks it', async () => {
        const { user, box } = renderForm();

        await user.click(box);
        await user.paste('x'.repeat(MAX_REPLY_LENGTH + 1));

        expect(box).toHaveAttribute('aria-invalid', 'true');
        expect(screen.getByText(`${MAX_REPLY_LENGTH + 1}/${MAX_REPLY_LENGTH}`)).toBeInTheDocument();
        expect(screen.getByRole('button', { name: 'Reply' })).toBeDisabled();
    });

    it('locks the form while posting and sends only once', async () => {
        const response = deferred(() => ok(reply()));
        const create = capture('post', 'ReviewReply', () => response.promise);
        server.use(create.handler);
        const { user, box } = renderForm({ onCancel: vi.fn() });

        await user.type(box, 'Hello');
        await user.click(screen.getByRole('button', { name: 'Reply' }));

        expect(await screen.findByRole('button', { name: 'Posting...' })).toBeDisabled();
        expect(box).toBeDisabled();
        expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled();
        response.release();
        await waitFor(() => expect(box).toBeEnabled());
        expect(create.calls).toHaveLength(1);
    });

    it('keeps the draft and shows the error when posting fails', async () => {
        server.use(capture('post', 'ReviewReply', apiError(403, 'Private reviews can\'t be replied to.')).handler);
        const { user, onCreated, box } = renderForm();

        await user.type(box, 'My draft');
        await user.type(box, '{Enter}');
        await user.click(screen.getByRole('button', { name: 'Reply' }));

        expect(await screen.findByText('Private reviews can\'t be replied to.')).toBeInTheDocument();
        expect(box).toHaveValue('My draft\n');
        expect(onCreated).not.toHaveBeenCalled();
    });

    it('cancels', async () => {
        const onCancel = vi.fn();
        const { user } = renderForm({ onCancel });

        await user.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(onCancel).toHaveBeenCalledTimes(1);
    });

    it('has no cancel button without a handler', () => {
        renderForm();
        expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    });
});
