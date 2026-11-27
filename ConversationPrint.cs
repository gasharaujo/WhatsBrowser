namespace WhatsBrowser;

internal sealed record PrintMessage(string Id, bool Incoming, string Text, string Label)
{
    public string Content => string.IsNullOrEmpty(Text) ? "Mídia ou mensagem sem texto" : Text;
    public string Caption => string.IsNullOrEmpty(Label) ? (Incoming ? "Recebida" : "Enviada") : Label;
}

internal sealed class ConversationPrint : Form
{
    private readonly IReadOnlyList<PrintMessage> messages;
    private readonly ListBox rows = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true, AccessibleName = "Mensagem inicial do print" };

    public string FirstId { get; private set; } = "";
    public string OutputPath { get; private set; } = "";

    public ConversationPrint(string? title, IReadOnlyList<PrintMessage> messages)
    {
        this.messages = messages;
        Text = string.IsNullOrWhiteSpace(title) ? "Print da conversa" : $"Print da conversa · {title}";
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(690, 485);
        MinimumSize = new Size(460, 340);
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label
        {
            Text = "Escolha a mensagem inicial. O app captura a conversa até a última mensagem carregada.",
            ForeColor = Ui.Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        for (var index = 0; index < messages.Count; index++)
        {
            var item = messages[index];
            var snippet = item.Content.ReplaceLineEndings(" ");
            if (snippet.Length > 110) snippet = snippet[..110] + "…";
            rows.Items.Add($"{index + 1}. {item.Caption}  {snippet}");
        }
        rows.SelectedIndex = 0;
        layout.Controls.Add(rows, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.RightToLeft };
        var save = Ui.Button("Capturar PNG", primary: true);
        var close = Ui.Button("Fechar");
        close.DialogResult = DialogResult.Cancel;
        save.Click += (_, _) =>
        {
            if (rows.SelectedIndex < 0) return;
            using var dialog = new SaveFileDialog { Filter = "Imagem PNG (*.png)|*.png", FileName = "conversa.png" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            FirstId = this.messages[rows.SelectedIndex].Id;
            OutputPath = dialog.FileName;
            DialogResult = DialogResult.OK;
        };
        actions.Controls.AddRange([save, close]);
        layout.Controls.Add(actions, 0, 2);
        Controls.Add(layout);
        CancelButton = close;
    }
}
