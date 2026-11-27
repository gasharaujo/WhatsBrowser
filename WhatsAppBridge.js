(function (request) {
    'use strict';
    if (location.origin !== 'https://web.whatsapp.com') return { state: 'outside' };
    const stateSlot = Symbol.for('WhatsBrowser.bridge');
    const state = window[stateSlot] ??= {};
    const visible = (node) => node?.getClientRects().length > 0;
    const first = (root, selectors) => {
        for (const selector of selectors) {
            const node = [...(root?.querySelectorAll(selector) ?? [])].find(visible);
            if (node) return node;
        }
        return null;
    };
    const main = first(document, ['#main', '[data-testid="conversation-panel-wrapper"]', '[data-testid="conversation-panel"]']);
    const editable = '[contenteditable="true"]';
    const editor = first(main || document, [
        `footer ${editable}[data-tab="10"]`,
        `[data-testid="conversation-compose-box-input"]${editable}`,
        `[data-testid="conversation-compose-box-input"] ${editable}`,
        `footer ${editable}[role="textbox"]`,
        `[data-testid="conversation-compose-box"] ${editable}`,
        `footer ${editable}`,
        ...(main ? [`${editable}[data-lexical-editor="true"][role="textbox"]`] : []),
    ]);
    const panel = main || editor?.closest('main, [role="main"]') || editor?.closest('footer')?.parentElement;
    if (!panel || !editor) return { state: 'no-chat' };
    const composer = editor.closest('[data-testid="conversation-compose-box"]');
    const footer = editor.closest('footer') || composer || panel;
    const header = first(panel, ['header', '[data-testid="conversation-panel-header"]']);
    const titleNode = first(header, ['[data-testid="conversation-info-header-chat-title"]', 'span[title]', 'span[dir="auto"]']);
    const title = (titleNode?.getAttribute('title') || titleNode?.textContent || '').trim().slice(0, 120);
    const selectedChat = document.querySelector('#pane-side [aria-selected="true"], #pane-side [aria-current="true"]');
    const selectedKey = selectedChat?.getAttribute('data-id') || '';
    const directionSelector = '.message-in, .message-out';
    const nativeSelector = '[data-id^="false_"], [data-id^="true_"], [data-message-id]';
    const clean = (text) => (text || '').replace(/\r\n?/g, '\n').replace(/[\u200B\u2060\uFEFF]/g, '').replace(/\u00a0/g, ' ').trim();

    function content(row) {
        const text = [...row.querySelectorAll(
            '[data-testid="msg-text"], [data-testid="selectable-text"], span.selectable-text, .copyable-text[dir="auto"]'
        )].findLast((node) => visible(node) && !node.closest(
            'blockquote, [data-testid*="quoted"], [data-testid*="quote-message"], [data-testid="msg-meta"]'));
        const rendered = clean(text?.innerText);
        const stored = clean(text?.textContent);
        return stored.includes('\n') && !rendered.includes('\n') ? stored : rendered || stored;
    }

    function identify(element) {
        const native = element.matches(nativeSelector) ? element : element.closest(nativeSelector) || element.querySelector(nativeSelector);
        const id = native?.getAttribute('data-id') || native?.getAttribute('data-message-id') || '';
        const parts = /^(true|false)_(.+?@[^_]+)_/.exec(id);
        const direction = element.closest(directionSelector) || element.querySelector(directionSelector);
        const tail = !direction && !parts ? element.querySelector('[data-icon="tail-in"], [data-icon="tail-out"], [data-testid="tail-in"], [data-testid="tail-out"]') : null;
        const incoming = direction ? direction.classList.contains('message-in') :
            parts?.[1] === 'false' ? true : parts?.[1] === 'true' ? false : tail ? (tail.getAttribute('data-icon') || tail.getAttribute('data-testid')) === 'tail-in' : null;
        return { id, key: parts?.[2] || '', row: direction || element, incoming };
    }

    function readRows() {
        const rows = new Map();
        const fallbackCounts = new Map();
        const elements = new Set();
        for (const element of panel.querySelectorAll(`${directionSelector}, ${nativeSelector}, [data-testid="msg-container"]`)) {
            if (!visible(element) || footer !== panel && footer.contains(element)) continue;
            const { id, key, row, incoming } = identify(element);
            if (incoming === null || elements.has(row) || id && rows.has(id)) continue;
            elements.add(row);
            const label = (row.matches('[data-pre-plain-text]') ? row : row.querySelector('[data-pre-plain-text]'))
                ?.getAttribute('data-pre-plain-text')?.trim() || '';
            const text = content(row);
            let rowId = id;
            if (!rowId) {
                const signature = `${incoming}:${label}:${text.length}:${text.slice(0, 90)}:${text.slice(-60)}`;
                const nth = (fallbackCounts.get(signature) || 0) + 1;
                fallbackCounts.set(signature, nth);
                rowId = `dom:${signature}:${nth}`;
            }
            rows.set(rowId, { id: rowId, key, incoming, element: row, label, text });
        }
        return [...rows.values()];
    }

    const rows = readRows();
    const key = rows.findLast((row) => row.key)?.key || selectedKey;
    const anchor = rows.findLast((row) => row.key)?.id;
    const target = { key, title, panel, header, selectedChat, anchor };
    const identified = Boolean(key || title && header);
    const sameChat = (before) => {
        if (!before) return false;
        if (before.key && key && before.key === key) return !before.title || !title || before.title === title;
        const sameSurface = Boolean(title && before.title === title &&
            (before.panel === panel && before.header === header || before.selectedChat && before.selectedChat === selectedChat) &&
            (!selectedChat || !before.selectedChat || selectedChat === before.selectedChat));
        if (before.key && key)
            return sameSurface && Boolean(before.anchor && rows.some((row) => row.id === before.anchor));
        return sameSurface;
    };
    function nearEnd() {
        for (let node = rows.at(-1)?.element.parentElement; node && node !== panel; node = node.parentElement) {
            const overflow = node.scrollHeight - node.clientHeight;
            const style = getComputedStyle(node);
            if (overflow > 45 && /auto|scroll/.test(style.overflowY)) {
                if (node.scrollTop < 0 || style.flexDirection === 'column-reverse') return Math.abs(node.scrollTop) < 135;
                return overflow - node.scrollTop < 135;
            }
        }
        return true;
    }

    const incoming = (row) => row ? { id: row.id, text: row.text.slice(0, 8192) } : null;
    const current = () => ({ state: 'ready', title, latest: incoming(rows.findLast((row) => row.incoming)) });
    const displayed = () => clean(editor.innerText || editor.textContent);
    const focusEditor = () => {
        editor.focus();
        return editor.contains(document.activeElement);
    };

    function sendButton() {
        const selectors = [
            'button[data-testid="compose-btn-send"]', '[data-testid="compose-btn-send"]',
            'button[aria-label="Enviar" i]', 'button[aria-label="Send" i]',
            '[role="button"][aria-label="Enviar" i]', '[role="button"][aria-label="Send" i]',
            'button[aria-label="Enviar mensagem" i]', 'button[aria-label="Send message" i]',
            '[role="button"][aria-label="Enviar mensagem" i]', '[role="button"][aria-label="Send message" i]',
            '[data-icon="send"]', '[data-icon="wds-ic-send-filled"]', '[data-icon="wds-ic-send"]',
        ];
        for (const scope of new Set([footer, composer])) {
            if (!scope) continue;
            for (const selector of selectors) {
                for (const marker of scope.querySelectorAll(selector)) {
                    const button = marker.closest('button, [role="button"]') || marker.closest('[aria-label]');
                    if (button && scope.contains(button) && visible(button) && !button.disabled &&
                        button.getAttribute('aria-disabled') !== 'true') return button;
                }
            }
        }
        return null;
    }

    if (request.action === 'read') return identified ? current() : { state: 'unidentified-chat' };

    if (request.action === 'watch-start') {
        if (!identified) return { state: 'unidentified-chat' };
        state.watch = { ...target, token: request.token, previous: rows.map((row) => row.id), followEnd: nearEnd() };
        return current();
    }
    if (request.action === 'watch-arm') {
        const watch = state.watch;
        if (watch?.token !== request.token || !sameChat(watch)) return { state: 'chat-changed' };
        watch.previous = rows.map((row) => row.id);
        watch.followEnd = nearEnd();
        if (key) Object.assign(watch, { key, anchor });
        return current();
    }
    if (request.action === 'watch-poll') {
        const watch = state.watch;
        if (watch?.token !== request.token || !sameChat(watch)) return { state: 'chat-changed' };
        if (!nearEnd()) {
            watch.followEnd = false;
            return { state: 'history' };
        }
        if (rows.length === 0 && watch.previous.length > 0) return { ...current(), fresh: [] };
        const previous = new Set(watch.previous);
        const overlap = rows.findLastIndex((row) => previous.has(row.id));
        const rebase = !watch.followEnd || previous.size > 0 && overlap === -1;
        const fresh = rebase ? [] : rows.slice(overlap + 1).filter((row) => row.incoming).map(incoming);
        watch.previous = rows.map((row) => row.id);
        watch.followEnd = true;
        if (key) Object.assign(watch, { key, anchor });
        return { ...current(), fresh, rebase };
    }

    if (request.action.startsWith('print-')) {
        const ordered = () => [...rows].sort((a, b) => a.element.getBoundingClientRect().top - b.element.getBoundingClientRect().top);
        if (request.action === 'print-messages') {
            const available = ordered().slice(-200);
            if (!available.length) return { state: 'no-messages' };
            state.printSelection = { ...target, token: request.token, ids: available.map((row) => row.id) };
            const messages = available.map((row) => ({
                id: row.id, incoming: row.incoming, text: row.text.slice(0, 140), label: row.label.slice(0, 240),
            }));
            return { state: 'ready', title, messages };
        }

        const selection = state.printSelection;
        if (selection?.token !== request.token || !sameChat(selection)) return { state: 'chat-changed' };
        const topOf = (row) => row.element.getBoundingClientRect().top;
        const bottomOf = (row) => row.element.getBoundingClientRect().bottom;
        const find = (id) => rows.find((row) => row.id === id);
        const viewport = (scroller) => {
            const bounds = panel.getBoundingClientRect();
            const top = Math.max(0, bounds.top, header?.getBoundingClientRect().bottom ?? 0);
            const bottom = Math.min(window.innerHeight, bounds.bottom, footer === panel ? bounds.bottom : footer.getBoundingClientRect().top);
            const scrollRect = scroller?.getBoundingClientRect();
            const x = Math.max(0, bounds.left, scrollRect ? scrollRect.left + scroller.clientLeft : 0);
            const y = Math.max(top, scrollRect ? scrollRect.top + scroller.clientTop : 0);
            const maxX = Math.min(window.innerWidth, bounds.right, scrollRect ? scrollRect.left + scroller.clientLeft + scroller.clientWidth : Infinity);
            const maxY = Math.min(bottom, scrollRect ? scrollRect.top + scroller.clientTop + scroller.clientHeight : Infinity);
            return { x, y, width: maxX - x, height: maxY - y, viewWidth: window.innerWidth, viewHeight: window.innerHeight, scroll: scroller?.scrollTop ?? 0 };
        };

        if (request.action === 'print-start') {
            const firstRow = selection.ids.includes(request.firstId) ? find(request.firstId) : null;
            const lastId = selection.ids.at(-1);
            const lastRow = find(lastId);
            if (!firstRow || !lastRow) return { state: 'print-range-changed' };
            let scroller = null;
            for (let node = firstRow.element.parentElement; node && node !== panel; node = node.parentElement) {
                if (node.contains(lastRow.element) && /auto|scroll|overlay/.test(getComputedStyle(node).overflowY)) {
                    scroller = node;
                    if (node.scrollHeight - node.clientHeight > 2) break;
                }
            }
            const frame = viewport(scroller);
            if (frame.width <= 0 || frame.height <= 0) return { state: 'print-viewport' };
            const start = topOf(firstRow) - frame.y + frame.scroll;
            const end = bottomOf(lastRow) - frame.y + frame.scroll;
            if (end <= start) return { state: 'print-range-changed' };
            selection.capture = { firstId: firstRow.id, lastId, scroller, originalScroll: frame.scroll, start, end };
            return { state: 'ready', frame: { ...frame, start, end } };
        }

        const capture = selection.capture;
        if (!capture) return { state: 'print-range-changed' };
        if (request.action === 'print-restore') {
            if (capture.scroller?.isConnected) capture.scroller.scrollTo({ top: capture.originalScroll, behavior: 'instant' });
            delete state.printSelection;
            return { state: 'ready' };
        }
        if (capture.scroller && (!capture.scroller.isConnected || !panel.contains(capture.scroller)))
            return { state: 'print-range-changed' };
        if (request.action === 'print-move') {
            if (!capture.scroller || !Number.isFinite(request.scroll)) return { state: 'print-viewport' };
            capture.scroller.scrollTo({ top: request.scroll, behavior: 'instant' });
            return { state: 'ready' };
        }
        if (request.action === 'print-frame') {
            const frame = viewport(capture.scroller);
            const referenceStart = find(capture.firstId);
            const referenceEnd = find(capture.lastId);
            if ((referenceStart && Math.abs(topOf(referenceStart) - frame.y + frame.scroll - capture.start) > 2) ||
                (referenceEnd && Math.abs(bottomOf(referenceEnd) - frame.y + frame.scroll - capture.end) > 2))
                return { state: 'print-range-changed' };
            return { state: 'ready', frame: { ...frame, start: capture.start, end: capture.end } };
        }
        return { state: 'failed' };
    }

    if (request.action === 'stage' || request.action === 'begin-send') {
        if (request.watch) {
            if (state.watch?.token !== request.watch || !sameChat(state.watch)) return { state: 'chat-changed' };
            if (!nearEnd()) return { state: 'history' };
        } else if (!identified) return { state: 'unidentified-chat' };
        if (request.id && !rows.some((row) => row.id === request.id && row.incoming))
            return { state: 'chat-changed' };
        if (typeof request.reply !== 'string' || !clean(request.reply) || request.reply.length > 4096) return { state: 'failed' };
        const reply = clean(request.reply);
        const draft = displayed();
        const reuse = request.action === 'begin-send' && request.allowMatchingDraft && draft === reply;
        if ((draft && !reuse) || editor.querySelector('img, [contenteditable="false"]')) return { state: 'draft' };
        if (!focusEditor()) return { state: 'focus-failed' };
        if (request.action === 'stage') {
            document.execCommand('insertText', false, request.reply.replace(/\r\n?/g, '\n'));
            return { state: displayed() === reply ? 'prepared' : 'insert-failed', title };
        }
        state.pending = {
            ...target, token: request.token, reply, insertionText: request.reply.replace(/\r\n?/g, '\n'),
            outgoing: new Set(rows.filter((row) => !row.incoming).map((row) => row.id)), dispatched: false,
        };
        if (reuse) {
            const selection = window.getSelection();
            if (!selection) return { state: 'prepared', title };
            const range = document.createRange();
            range.selectNodeContents(editor);
            selection.removeAllRanges();
            selection.addRange(range);
        }
        return { state: 'insert-target', title };
    }

    const pending = state.pending;
    if (!pending || pending.token !== request.token || !sameChat(pending)) return { state: 'chat-changed' };
    const input = displayed();
    if (request.action === 'input-status' || request.action === 'insert-fallback') {
        if (input === pending.reply) return { state: 'prepared' };
        if (input) return { state: 'draft-changed' };
        if (request.action === 'input-status') return { state: 'empty' };
        if (!focusEditor()) return { state: 'focus-failed' };
        document.execCommand('insertText', false, pending.insertionText);
        return { state: 'check-input' };
    }

    const readSendStatus = () => {
        const sent = rows.some((row) => !row.incoming && !pending.outgoing.has(row.id) && row.text === pending.reply);
        if (sent) {
            delete state.pending;
            return { state: 'sent', title };
        }
        if (!input && pending.dispatched) return { state: 'cleared' };
        return { state: input === pending.reply ? 'pending' : 'draft-changed' };
    };
    if (request.action === 'send-status') return readSendStatus();
    if (!['submit', 'send-target'].includes(request.action)) return { state: 'failed' };
    const status = readSendStatus();
    if (status.state !== 'pending') return status;
    if (request.action === 'submit') {
        const button = sendButton();
        if (!button) return { state: 'send-unavailable' };
        pending.dispatched = true;
        button.click();
        return { state: 'dispatched' };
    }
    if (!request.forceEnter) {
        const button = sendButton();
        if (button) {
            const rect = button.getBoundingClientRect();
            const x = rect.left + rect.width / 2;
            const y = rect.top + rect.height / 2;
            if (rect.width > 0 && rect.height > 0 && button.contains(document.elementFromPoint(x, y))) {
                pending.dispatched = true;
                return { state: 'click-target', x, y };
            }
        }
    }

    if (!focusEditor()) return { state: 'focus-failed' };
    pending.dispatched = true;
    return { state: 'enter-target' };
})
