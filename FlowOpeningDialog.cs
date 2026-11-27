namespace WhatsBrowser;

internal static class FlowOpeningDialog
{
    public static FlowOpening? Edit(IWin32Window owner, FlowOpening? opening)
    {
        using var dialog = CreateWindow("Mensagem inicial do fluxo");
        var enabled = new CheckBox
        {
            Dock = DockStyle.Fill, Text = "Oferecer o envio da primeira mensagem ao ativar",
            Checked = opening?.Enabled ?? true,
        };
        var text = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical,
            MaxLength = RuleEngine.MaxReply, AccessibleName = "Texto da mensagem inicial",
            PlaceholderText = "Ex. Olá! Como posso ajudar?", Text = opening?.Text ?? "",
        };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = Ui.Button("Aplicar", primary: true);
        save.Click += (_, _) =>
        {
            if (enabled.Checked && string.IsNullOrWhiteSpace(text.Text))
            {
                text.Focus();
                return;
            }
            dialog.DialogResult = DialogResult.OK;
        };
        save.Enabled = !enabled.Checked || !string.IsNullOrWhiteSpace(text.Text);
        enabled.CheckedChanged += (_, _) => save.Enabled = !enabled.Checked || !string.IsNullOrWhiteSpace(text.Text);
        text.TextChanged += (_, _) => save.Enabled = !enabled.Checked || !string.IsNullOrWhiteSpace(text.Text);
        var cancel = Ui.Button("Cancelar");
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.AddRange([save, cancel]);
        var layout = CreateLayout(36);
        layout.Controls.Add(enabled, 0, 0);
        layout.Controls.Add(text, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        dialog.Controls.Add(layout);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(owner) == DialogResult.OK ? new FlowOpening(enabled.Checked, text.Text.Trim()) : null;
    }

    public static DialogResult Confirm(IWin32Window owner, string chatTitle, string message)
    {
        using var dialog = CreateWindow("Ativar fluxo");
        var description = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Ink,
            Text = $"Ativar o fluxo em {chatTitle}. Você pode começar enviando a mensagem abaixo.",
        };
        var preview = new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = message, AccessibleName = "Prévia da mensagem inicial",
        };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var send = Ui.Button("Enviar e ativar", primary: true);
        send.DialogResult = DialogResult.Yes;
        var activate = Ui.Button("Só ativar");
        activate.DialogResult = DialogResult.No;
        var cancel = Ui.Button("Cancelar");
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.AddRange([send, activate, cancel]);
        var layout = CreateLayout(52);
        layout.Controls.Add(description, 0, 0);
        layout.Controls.Add(preview, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        dialog.Controls.Add(layout);
        dialog.CancelButton = cancel;
        dialog.ActiveControl = cancel;
        return dialog.ShowDialog(owner);
    }

    private static Form CreateWindow(string title) => new()
    {
        Text = title, Font = new Font("Segoe UI", 10), StartPosition = FormStartPosition.CenterParent,
        ClientSize = new Size(520, 232), MinimumSize = new Size(440, 245), ShowInTaskbar = false,
        MaximizeBox = false, MinimizeBox = false, BackColor = Color.White,
    };

    private static TableLayoutPanel CreateLayout(int headerHeight)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, headerHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        return layout;
    }
}
