using System.Runtime.InteropServices;

namespace WhatsBrowser;

internal sealed class WhatsAppSender(Func<object, Task<PageResult>> evaluate, Func<string, object, Task> dispatch)
{
    public async Task<PageResult> SendAsync(string reply, Guid? watch, string? id, Func<bool> isCurrent)
    {
        var token = Guid.NewGuid();
        var start = await evaluate(new { action = "begin-send", token, reply, watch, id, allowMatchingDraft = watch is null });
        if (!isCurrent()) return Canceled();
        if (start.State == "insert-target")
        {
            try { await dispatch("Input.insertText", new { text = reply.ReplaceLineEndings("\n") }); }
            catch (COMException) { /* The verified empty field can use the page's insertion fallback. */ }
            start = await WaitForInputAsync(token, isCurrent);
            if (start.State == "empty" && isCurrent())
            {
                var fallback = await evaluate(new { action = "insert-fallback", token });
                start = fallback.State == "check-input" ? await WaitForInputAsync(token, isCurrent) : fallback;
            }
            if (start.State == "empty") return new PageResult { State = "insert-failed" };
        }
        if (!isCurrent()) return Canceled();
        if (start.State != "prepared") return start;

        for (var i = 0; i < 3; i++)
        {
            if (!isCurrent()) return Canceled();
            var attempt = await evaluate(new { action = "submit", token });
            if (attempt.State == "dispatched")
            {
                var checkedSend = await WaitForOutgoingAsync(token, isCurrent);
                if (checkedSend.State != "pending") return checkedSend;
                break;
            }
            if (attempt.State is not ("send-unavailable" or "no-chat")) return attempt;
            await Task.Delay(70);
        }

        if (!isCurrent()) return Canceled();
        var target = await evaluate(new { action = "send-target", token });
        if (!isCurrent()) return Canceled();
        if (!await DispatchSendAsync(target, isCurrent)) return target;
        var result = await WaitForOutgoingAsync(token, isCurrent);
        if (result.State != "pending" || target.State != "click-target" || !isCurrent()) return result;

        target = await evaluate(new { action = "send-target", token, forceEnter = true });
        if (!isCurrent()) return Canceled();
        if (!await DispatchSendAsync(target, isCurrent)) return target;
        return await WaitForOutgoingAsync(token, isCurrent);
    }

    private async Task<PageResult> WaitForInputAsync(Guid token, Func<bool> isCurrent)
    {
        var result = new PageResult { State = "empty" };
        var consecutiveMatches = 0;
        for (var i = 0; i < 7; i++)
        {
            await Task.Delay(45);
            if (!isCurrent()) return Canceled();
            result = await evaluate(new { action = "input-status", token });
            if (result.State == "prepared")
            {
                if (++consecutiveMatches >= 2) return result;
                continue;
            }
            consecutiveMatches = 0;
            if (result.State is not ("empty" or "no-chat")) return result;
        }
        return result;
    }

    private async Task<PageResult> WaitForOutgoingAsync(Guid token, Func<bool> isCurrent)
    {
        var result = new PageResult { State = "pending" };
        var cleared = false;
        var consecutiveClears = 0;
        for (var i = 0; i < (cleared ? 17 : 8); i++)
        {
            await Task.Delay(90);
            if (!isCurrent()) return Canceled();
            result = await evaluate(new { action = "send-status", token });
            if (result.State == "cleared")
            {
                cleared = true;
                consecutiveClears++;
                continue;
            }
            consecutiveClears = 0;
            if (result.State == "pending" && cleared) return new PageResult { State = "unconfirmed" };
            if (result.State is not ("pending" or "no-chat")) return result;
        }
        return consecutiveClears >= 2 ? new PageResult { State = "submitted" } : result;
    }

    private async Task<bool> DispatchSendAsync(PageResult target, Func<bool> isCurrent)
    {
        if (target.State == "click-target" && target.X is { } x && target.Y is { } y)
        {
            await dispatch("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y });
            if (!isCurrent()) return false;
            await dispatch("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", buttons = 1, clickCount = 1 });
            await dispatch("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", buttons = 0, clickCount = 1 });
            return true;
        }
        if (target.State != "enter-target") return false;
        await dispatch("Input.dispatchKeyEvent", new { type = "rawKeyDown", key = "Enter", code = "Enter", windowsVirtualKeyCode = 13, nativeVirtualKeyCode = 13 });
        await dispatch("Input.dispatchKeyEvent", new { type = "keyUp", key = "Enter", code = "Enter", windowsVirtualKeyCode = 13, nativeVirtualKeyCode = 13 });
        return true;
    }

    private static PageResult Canceled() => new() { State = "canceled" };
}

internal sealed class PageResult
{
    public PageResult() { }

    public string State { get; init; } = "failed";
    public string? Title { get; init; }
    public bool Rebase { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public IncomingMessage? Latest { get; init; }
    public List<IncomingMessage>? Fresh { get; init; }
    public List<PrintMessage>? Messages { get; init; }
    public PrintFrame? Frame { get; init; }
}

internal sealed record IncomingMessage(string Id, string Text);
