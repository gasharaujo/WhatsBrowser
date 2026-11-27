namespace WhatsBrowser;

internal sealed record QuickReply(string Title, string Text);

internal sealed class QuickReplyPanel : UserControl
{
    private const int MaxEntries = 60;
    private readonly string path;
    private readonly FlowLayoutPanel list = new()
    {
        Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, BackColor = Ui.Canvas, Padding = new Padding(8), Margin = Padding.Empty,
    };
    private readonly TextBox title = new() { Dock = DockStyle.Fill, MaxLength = RuleEngine.MaxName, PlaceholderText = "Ex. Horário de atendimento" };
    private readonly TextBox message = new()
    {
        Dock = DockStyle.Fill, MaxLength = RuleEngine.MaxReply, Multiline = true, ScrollBars = ScrollBars.Vertical,
        AcceptsReturn = true, PlaceholderText = "Texto que será enviado",
    };
    private readonly Label note = new() { Dock = DockStyle.Fill, ForeColor = Ui.Muted, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button remove = Ui.Button("Excluir");
    private readonly Button update = Ui.Button("Atualizar");
    private readonly ToolTip titles = new();
    private readonly List<Button> sendButtons = [];
    private List<QuickReply> entries = [];
    private int selection = -1;
    private bool canSend;

    public event Func<QuickReply, Task>? SendRequested;

    public QuickReplyPanel(string directory)
    {
        path = Path.Combine(directory, "respostas-prontas.json");
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        ForeColor = Ui.Ink;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        var heading = new Label
        {
            Text = "Respostas prontas", Font = new Font("Segoe UI", 13, FontStyle.Bold),
            Dock = DockStyle.Fill, Padding = new Padding(12, 0, 8, 0), TextAlign = ContentAlignment.MiddleLeft,
        };
        var description = new Label
        {
            Text = "Envie uma mensagem salva para a conversa aberta.", ForeColor = Ui.Muted,
            Dock = DockStyle.Fill, Padding = new Padding(12, 0, 8, 0), TextAlign = ContentAlignment.MiddleLeft,
        };
        var head = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        head.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        head.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        head.Controls.Add(heading, 0, 0);
        head.Controls.Add(description, 0, 1);
        layout.Controls.Add(head, 0, 0);
        layout.Controls.Add(list, 0, 1);
        layout.Controls.Add(CreateFields(), 0, 2);
        Controls.Add(layout);
        list.ClientSizeChanged += (_, _) => SizeCards();
        Disposed += (_, _) => titles.Dispose();

        try
        {
            if (File.Exists(path))
            {
                var loaded = LocalJson.Read<List<QuickReply>>(path, 2);
                if (loaded is null || loaded.Count > MaxEntries || loaded.Any(IsInvalid))
                    throw new InvalidDataException("O arquivo de respostas prontas é inválido.");
                entries = loaded;
            }
        }
        catch (Exception ex) { note.Text = $"Falha ao carregar respostas. {ex.Message}"; }
        SetSelection(-1);
        DrawList();
    }

    private Control CreateFields()
    {
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(12, 8, 10, 4), Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 89));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        fields.Controls.Add(new Label { Text = "Título", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        fields.Controls.Add(new Label { Text = "Mensagem", Dock = DockStyle.Fill }, 0, 1);
        title.AccessibleName = "Título da resposta";
        message.AccessibleName = "Mensagem da resposta";
        fields.Controls.Add(title, 1, 0);
        fields.Controls.Add(message, 1, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        var save = Ui.Button("Salvar resposta", primary: true);
        save.Click += (_, _) => Save(overwrite: false);
        var add = Ui.Button("Nova");
        add.Click += (_, _) => { SetSelection(-1); title.Focus(); };
        remove.Click += (_, _) => Delete();
        update.Click += (_, _) => Save(overwrite: true);
        actions.Controls.AddRange([save, update, add, remove]);
        fields.Controls.Add(actions, 0, 2);
        fields.SetColumnSpan(actions, 2);
        fields.Controls.Add(note, 0, 3);
        fields.SetColumnSpan(note, 2);
        return fields;
    }

    public void SetSendAvailable(bool enabled)
    {
        if (canSend == enabled) return;
        canSend = enabled;
        foreach (var button in sendButtons) button.Enabled = enabled;
    }

    private void SetSelection(int index)
    {
        selection = index;
        title.Text = index < 0 ? "" : entries[index].Title;
        message.Text = index < 0 ? "" : entries[index].Text;
        remove.Enabled = update.Enabled = index >= 0;
    }

    private void DrawList()
    {
        list.SuspendLayout();
        titles.RemoveAll();
        while (list.Controls.Count > 0) list.Controls[0].Dispose();
        sendButtons.Clear();
        if (entries.Count == 0)
        {
            list.Controls.Add(new Label { Text = "Cadastre a primeira resposta abaixo.", AutoSize = true, ForeColor = Ui.Muted });
        }
        for (var i = 0; i < entries.Count; i++)
        {
            var index = i;
            var item = entries[index];
            var row = new TableLayoutPanel { Height = 88, ColumnCount = 2, RowCount = 2, Padding = new Padding(5, 2, 5, 3), BackColor = Color.White };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            var edit = Ui.Button(item.Title);
            edit.AutoSize = false;
            edit.UseMnemonic = false;
            edit.Dock = DockStyle.Fill;
            edit.Margin = Padding.Empty;
            edit.FlatAppearance.BorderSize = 0;
            edit.TextAlign = ContentAlignment.MiddleLeft;
            edit.AccessibleName = $"Editar resposta {item.Title}";
            edit.Click += (_, _) => { SetSelection(index); title.Focus(); };
            titles.SetToolTip(edit, item.Title);
            var preview = new Label
            {
                Text = item.Text.ReplaceLineEndings(" "), AutoEllipsis = true, Dock = DockStyle.Fill,
                ForeColor = Ui.Muted, Padding = new Padding(7, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft,
            };
            var send = Ui.Button("Enviar", primary: true);
            send.Enabled = canSend;
            send.Anchor = AnchorStyles.Right;
            send.AccessibleName = $"Enviar resposta {item.Title}";
            send.Click += async (_, _) =>
            {
                if (SendRequested is { } handler) await handler(item);
            };
            sendButtons.Add(send);
            row.Controls.Add(edit, 0, 0);
            row.SetColumnSpan(edit, 2);
            row.Controls.Add(preview, 0, 1);
            row.Controls.Add(send, 1, 1);
            list.Controls.Add(row);
        }
        SizeCards();
        list.ResumeLayout();
    }

    private void SizeCards()
    {
        var width = Math.Max(1, list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 6);
        foreach (var row in list.Controls.OfType<TableLayoutPanel>()) row.Width = width;
    }

    private static bool IsInvalid(QuickReply? item) =>
        item is null || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > RuleEngine.MaxName ||
        string.IsNullOrWhiteSpace(item.Text) || item.Text.Length > RuleEngine.MaxReply;

    private void Save(bool overwrite)
    {
        var item = new QuickReply(title.Text.Trim(), message.Text.Trim());
        if (IsInvalid(item))
        {
            note.Text = "Preencha o título e a mensagem.";
            return;
        }
        if (overwrite && selection < 0) return;
        if (!overwrite && entries.Count >= MaxEntries)
        {
            note.Text = $"O limite é de {MaxEntries} respostas prontas.";
            return;
        }
        List<QuickReply> next = [.. entries];
        if (overwrite) next[selection] = item;
        else next.Add(item);
        if (!Commit(next)) return;
        if (!overwrite) SetSelection(-1);
        note.Text = overwrite ? "Resposta atualizada." : "Nova resposta salva.";
    }

    private void Delete()
    {
        if (selection < 0 || MessageBox.Show(this, $"Excluir a resposta “{entries[selection].Title}”?", "Excluir resposta",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        List<QuickReply> next = [.. entries];
        next.RemoveAt(selection);
        if (!Commit(next)) return;
        SetSelection(-1);
        note.Text = "Resposta excluída.";
    }

    private bool Commit(List<QuickReply> next)
    {
        try
        {
            LocalJson.Write(path, next);
            entries = next;
            DrawList();
            return true;
        }
        catch (Exception ex)
        {
            note.Text = $"Falha ao salvar. {ex.Message}";
            return false;
        }
    }
}
