using System.Drawing.Drawing2D;

namespace WhatsBrowser;

internal sealed class FlowCanvas : Control
{
    private const int WidthInWorld = 256;
    private const int HeaderHeight = 44;
    private const int BranchHeight = 26;
    private const float MinZoom = 0.07f;
    private const float MaxZoom = 1.75f;
    private ReplyFlow? flow;
    private Guid selected;
    private int branch;
    private (Guid Node, int Branch)? pending;
    private PointF offset = new(10, 10);
    private float zoom = 1;
    private Point mouseStart;
    private PointF originalOffset;
    private PointF pointer;
    private Guid? dragStage;
    private Point dragStart;
    private Point dragPosition;
    private bool panning;
    private bool resizeFit;

    public event Action<Guid, int>? StageSelected;
    public event Action<Guid, Point>? StageMoved;
    public event Action<Guid, int, Guid>? ConnectRequested;

    public FlowCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Ui.Canvas;
        TabStop = true;
        AccessibleName = "Canvas de entradas e saídas";
    }

    public void Present(ReplyFlow flow, Guid selected, int branch)
    {
        this.flow = flow;
        this.selected = selected;
        this.branch = branch;
        Invalidate();
    }

    public void CancelConnection()
    {
        pending = null;
        Invalidate();
    }

    public Point NewStagePoint()
    {
        var point = Point.Round(ToWorld(new PointF(ClientSize.Width * .48f, ClientSize.Height * .4f)));
        if (flow is null) return point;
        for (var i = 0; i < flow.Stages.Count && flow.Stages.Any(stage => BoundsOf(stage).IntersectsWith(new RectangleF(point.X, point.Y, WidthInWorld, 72))); i++)
            point.Y += 94;
        return point;
    }

    public void Fit()
    {
        if (flow is null) return;
        if (ClientSize.Width < 100 || ClientSize.Height < 85)
        {
            resizeFit = true;
            return;
        }
        var bounds = flow.Stages.Select(BoundsOf).Aggregate(RectangleF.Union);
        zoom = Math.Clamp(Math.Min((ClientSize.Width - 44) / bounds.Width, (ClientSize.Height - 46) / bounds.Height), MinZoom, 1.1f);
        offset = new PointF((ClientSize.Width - bounds.Width * zoom) / 2 - bounds.X * zoom,
            (ClientSize.Height - bounds.Height * zoom) / 2 - bounds.Y * zoom);
        Invalidate();
    }

    public void CenterOn(FlowStage stage)
    {
        var rect = BoundsOf(stage);
        if (zoom < .55f) zoom = .75f;
        offset = new PointF(ClientSize.Width / 2f - (rect.X + rect.Width / 2) * zoom,
            ClientSize.Height / 2f - (rect.Y + Math.Min(120, rect.Height) / 2) * zoom);
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (resizeFit && ClientSize.Width >= 100 && ClientSize.Height >= 85)
        {
            resizeFit = false;
            Fit();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (flow is null) return;
        var graphics = e.Graphics;
        var gap = Math.Max(12, 24 * zoom);
        using var dots = new SolidBrush(Color.FromArgb(210, 223, 214));
        for (var x = offset.X % gap; x < Width; x += gap)
            for (var y = offset.Y % gap; y < Height; y += gap)
                graphics.FillRectangle(dots, x, y, 1.4f, 1.4f);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var state = graphics.Save();
        graphics.TranslateTransform(offset.X, offset.Y);
        graphics.ScaleTransform(zoom, zoom);
        var nodes = flow.Stages.ToDictionary(stage => stage.Id);
        using var edge = new Pen(Color.FromArgb(101, 153, 123), 2 / Math.Max(zoom, .55f));
        using var picked = new Pen(Ui.Green, 2.6f / Math.Max(zoom, .55f));
        using var dash = new Pen(Ui.Green, 2 / Math.Max(zoom, .55f)) { DashStyle = DashStyle.Dash };
        foreach (var from in flow.Stages)
            for (var i = 0; i < from.Branches.Count; i++)
                if (from.Branches[i].NextStageId is { } to && nodes.TryGetValue(to, out var target))
                    DrawEdge(graphics, from.Id == selected && i == branch ? picked : edge,
                        OutPort(BoundsOf(from), i), new PointF(BoundsOf(target).Left, BoundsOf(target).Top + 21));
        if (pending is { } wire && nodes.TryGetValue(wire.Node, out var origin) && wire.Branch < origin.Branches.Count)
            DrawEdge(graphics, dash, OutPort(BoundsOf(origin), wire.Branch), pointer);

        var view = RectangleF.FromLTRB(ToWorld(PointF.Empty).X, ToWorld(PointF.Empty).Y,
            ToWorld(new PointF(Width, Height)).X, ToWorld(new PointF(Width, Height)).Y);
        using var nameFont = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var compactFont = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Ui.Ink);
        using var muted = new SolidBrush(Ui.Muted);
        using var green = new SolidBrush(Ui.Green);
        using var highlight = new SolidBrush(Color.FromArgb(228, 244, 234));
        using var border = new Pen(Ui.Border);
        using var focus = new Pen(Ui.Green, 2);
        using var text = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        foreach (var stage in flow.Stages)
        {
            var box = BoundsOf(stage);
            if (!view.IntersectsWith(box)) continue;
            var isSelected = stage.Id == selected;
            graphics.FillRectangle(Brushes.White, box);
            graphics.FillRectangle(isSelected ? highlight : Brushes.White, box.X, box.Y, box.Width, HeaderHeight);
            graphics.DrawRectangle(isSelected ? focus : border, box.X, box.Y, box.Width, box.Height);
            if (zoom < .23f) continue;
            var initial = stage.Id == flow.StartStageId;
            graphics.DrawString(stage.Name, nameFont, ink, new RectangleF(box.X + 12, box.Y + 10, box.Width - (initial ? 70 : 23), 25), text);
            if (initial) graphics.DrawString("INÍCIO", compactFont, green, box.Right - 57, box.Y + 14);
            if (stage.Branches.Count == 0)
                graphics.DrawString("Adicione uma saída no painel abaixo", bodyFont, muted, box.X + 12, box.Y + HeaderHeight + 3);
            for (var i = 0; i < stage.Branches.Count; i++)
            {
                var rule = stage.Branches[i];
                var port = OutPort(box, i);
                if (isSelected && i == branch)
                    graphics.FillRectangle(highlight, box.X + 2, port.Y - 12, box.Width - 4, 24);
                var caption = RuleEditor.BranchLabel(rule);
                graphics.DrawString(caption, bodyFont, ink, new RectangleF(box.X + 12, port.Y - 9, box.Width - 42, 20), text);
                graphics.FillEllipse(rule.NextStageId is not null ? green : Brushes.White, port.X - 5, port.Y - 5, 10, 10);
                graphics.DrawEllipse(focus, port.X - 5, port.Y - 5, 10, 10);
            }
        }
        graphics.Restore(state);
        using var status = new SolidBrush(Ui.Muted);
        graphics.DrawString(pending is null ? $"{zoom:P0}  ·  Roda para ampliar" : "Clique na próxima entrada · Esc para cancelar",
            Font, status, 8, Height - Font.Height - 6);
    }

    private static void DrawEdge(Graphics graphics, Pen pen, PointF from, PointF to)
    {
        var bend = Math.Clamp(Math.Abs(to.X - from.X) * .45f, 38, 210);
        graphics.DrawBezier(pen, from, new PointF(from.X + bend, from.Y), new PointF(to.X - bend, to.Y), to);
        graphics.DrawLine(pen, to, new PointF(to.X - 7, to.Y - 4));
        graphics.DrawLine(pen, to, new PointF(to.X - 7, to.Y + 4));
    }

    private RectangleF BoundsOf(FlowStage stage)
    {
        var at = dragStage == stage.Id ? dragPosition : new Point(stage.X, stage.Y);
        return new RectangleF(at.X, at.Y, WidthInWorld, HeaderHeight + Math.Max(1, stage.Branches.Count) * BranchHeight + 6);
    }

    private static PointF OutPort(RectangleF box, int index) => new(box.Right, box.Top + HeaderHeight + index * BranchHeight + BranchHeight / 2f);
    private PointF ToWorld(PointF screen) => new((screen.X - offset.X) / zoom, (screen.Y - offset.Y) / zoom);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (flow is null || e.Button is not (MouseButtons.Left or MouseButtons.Middle)) return;
        Focus();
        var point = ToWorld(e.Location);
        pointer = point;
        if (e.Button == MouseButtons.Left)
        {
            var target = flow.Stages.AsEnumerable().Reverse().FirstOrDefault(stage => BoundsOf(stage).Contains(point));
            if (pending is { } wire && target is not null)
            {
                CancelConnection();
                ConnectRequested?.Invoke(wire.Node, wire.Branch, target.Id);
                return;
            }
            foreach (var stage in flow.Stages.AsEnumerable().Reverse())
            {
                var box = BoundsOf(stage);
                for (var i = 0; i < stage.Branches.Count; i++)
                {
                    var port = OutPort(box, i);
                    var tolerance = Math.Min(15, 11 / zoom);
                    if (Math.Abs(port.X - point.X) > tolerance || Math.Abs(port.Y - point.Y) > tolerance) continue;
                    StageSelected?.Invoke(stage.Id, i);
                    pending = (stage.Id, i);
                    Invalidate();
                    return;
                }
            }
            if (target is not null)
            {
                var row = (int)Math.Floor((point.Y - BoundsOf(target).Top - HeaderHeight) / BranchHeight);
                StageSelected?.Invoke(target.Id, Math.Max(row, 0));
                if (row >= 0) return;
                dragStage = target.Id;
                dragStart = dragPosition = new Point(target.X, target.Y);
            }
            else panning = true;
        }
        else panning = true;
        mouseStart = e.Location;
        originalOffset = offset;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragStage is not null)
        {
            dragPosition = new Point(
                Math.Clamp(dragStart.X + (int)((e.X - mouseStart.X) / zoom), -RuleEngine.MaxCoordinate, RuleEngine.MaxCoordinate),
                Math.Clamp(dragStart.Y + (int)((e.Y - mouseStart.Y) / zoom), -RuleEngine.MaxCoordinate, RuleEngine.MaxCoordinate));
        }
        else if (panning)
            offset = new PointF(originalOffset.X + e.X - mouseStart.X, originalOffset.Y + e.Y - mouseStart.Y);
        pointer = ToWorld(e.Location);
        if (pending is not null || dragStage is not null || panning) Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var moved = dragStage;
        dragStage = null;
        panning = false;
        Capture = false;
        if (moved is { } id && dragPosition != dragStart) StageMoved?.Invoke(id, dragPosition);
        Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (Capture) return;
        dragStage = null;
        panning = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var world = ToWorld(e.Location);
        zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.18f : 1 / 1.18f), MinZoom, MaxZoom);
        offset = new PointF(e.X - world.X * zoom, e.Y - world.Y * zoom);
        Invalidate();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && pending is not null)
        {
            CancelConnection();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
