using System.Drawing.Imaging;
using Microsoft.Web.WebView2.Core;

namespace WhatsBrowser;

internal sealed record PrintFrame(double X, double Y, double Width, double Height, double ViewWidth, double ViewHeight,
    double Scroll, double Start, double End);

internal sealed class ConversationCapture(CoreWebView2 web, Func<object, Task<PageResult>> evaluate)
{
    public async Task SaveAsync(Guid token, string firstId, string path, Action<int> report)
    {
        try
        {
            var frame = RequireFrame(await evaluate(new { action = "print-start", token, firstId }));
            using var sample = await TakeScreenshotAsync();
            var scaleX = sample.Width / frame.ViewWidth;
            var scaleY = sample.Height / frame.ViewHeight;
            if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
                throw new InvalidOperationException("A WebView não retornou uma imagem válida.");

            var left = Math.Max(0, Ceiling(frame.X * scaleX));
            var width = Math.Min(sample.Width, Floor((frame.X + frame.Width) * scaleX)) - left;
            var start = Ceiling(frame.Start * scaleY);
            var end = Floor(frame.End * scaleY);
            var height = end - start;
            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("Não há mensagens dentro da área de captura.");
            if (height > 32_000 || (long)width * height > 48_000_000)
                throw new InvalidOperationException("O trecho é longo demais para um PNG. Escolha uma mensagem inicial mais recente.");

            using var image = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(image);
            var initialFrame = frame;
            var next = start;
            while (next < end)
            {
                var cursor = next / scaleY;
                if (next != start || cursor < frame.Scroll - .01 || cursor >= frame.Scroll + frame.Height - 1)
                {
                    var overlap = Math.Clamp(frame.Height * .18, 6, 64);
                    var move = await evaluate(new { action = "print-move", token, scroll = cursor - overlap });
                    if (move.State != "ready") throw CaptureError(move.State);
                    await Task.Delay(115);
                    frame = RequireFrame(await evaluate(new { action = "print-frame", token }));
                }
                if (!SameViewport(frame, initialFrame)) throw CaptureError("print-viewport-changed");

                var top = Ceiling(frame.Y * scaleY);
                var bottom = Math.Min(sample.Height, Floor((frame.Y + frame.Height) * scaleY));
                var y = next + (int)Math.Round((frame.Y - frame.Scroll) * scaleY);
                if (y < top - 1 || y >= bottom) throw CaptureError("print-gap");
                y = Math.Max(top, y);
                var count = Math.Min(end - next, bottom - y);
                var destination = next - start;

                using var screenshot = await TakeScreenshotAsync();
                var verified = RequireFrame(await evaluate(new { action = "print-frame", token }));
                if (!SameViewport(verified, frame) || Math.Abs(verified.Scroll - frame.Scroll) > .25 ||
                    screenshot.Width != sample.Width || screenshot.Height != sample.Height)
                    throw CaptureError("print-viewport-changed");

                graphics.SetClip(new Rectangle(0, destination, width, count));
                graphics.DrawImageUnscaled(screenshot, -left, destination - y);
                graphics.ResetClip();
                next += count;
                report((int)((next - start) * 100L / height));
            }

            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                image.Save(temporary, ImageFormat.Png);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        finally
        {
            await evaluate(new { action = "print-restore", token });
        }
    }

    private async Task<Bitmap> TakeScreenshotAsync()
    {
        using var png = new MemoryStream();
        await web.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
        png.Position = 0;
        using var frame = new Bitmap(png);
        return frame.Clone(new Rectangle(0, 0, frame.Width, frame.Height), PixelFormat.Format24bppRgb);
    }

    private static int Ceiling(double value) => (int)Math.Ceiling(value - .001);
    private static int Floor(double value) => (int)Math.Floor(value + .001);

    private static bool SameViewport(PrintFrame first, PrintFrame second) =>
        Math.Abs(first.X - second.X) < .25 && Math.Abs(first.Y - second.Y) < .25 &&
        Math.Abs(first.Width - second.Width) < .25 && Math.Abs(first.Height - second.Height) < .25 &&
        first.ViewWidth == second.ViewWidth && first.ViewHeight == second.ViewHeight;

    private static PrintFrame RequireFrame(PageResult result) =>
        result.State == "ready" && result.Frame is { Width: > 0, Height: > 0 } frame &&
        double.IsFinite(frame.Start) && double.IsFinite(frame.End) && frame.End > frame.Start
            ? frame : throw CaptureError(result.State);

    private static InvalidOperationException CaptureError(string state) => new(state switch
    {
        "chat-changed" => "A conversa mudou durante o print.",
        "print-range-changed" => "O WhatsApp rearranjou as mensagens. Abra o print novamente e selecione o trecho.",
        "print-viewport-changed" => "A área do WhatsApp mudou durante a captura. Mantenha a janela e a rolagem fixas até o fim.",
        "print-gap" => "Parte do trecho saiu da área carregada pelo WhatsApp. Selecione um trecho mais curto.",
        _ => "Não foi possível capturar a área da conversa aberta.",
    });
}
