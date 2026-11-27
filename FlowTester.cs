namespace WhatsBrowser;

internal sealed class FlowTester : Form
{
    private readonly TextBox input = new()
    {
        Dock = DockStyle.Fill, AcceptsReturn = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
        MaxLength = 1_000_000, AccessibleName = "Mensagens de entrada",
    };
    private readonly DataGridView results = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false,
        BackgroundColor = Color.White, AccessibleName = "Resultados do fluxo",
    };
    private readonly Label status = new() { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(6, 10, 0, 0) };
    private readonly Button runButton = Ui.Button("Executar mensagens", primary: true);
    private readonly ReplyFlow flow;
    private FlowRun run;

    public FlowTester(ReplyFlow flow)
    {
        this.flow = flow;
        run = new FlowRun(flow);
        Text = "Testar fluxo";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(920, 620);
        MinimumSize = new Size(620, 450);
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label { Text = "Cole ou digite uma mensagem por linha. Cada linha conta como a próxima interação.", Dock = DockStyle.Fill }, 0, 0);
        layout.Controls.Add(input, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
        var reset = Ui.Button("Reiniciar");
        reset.Click += (_, _) =>
        {
            run = new FlowRun(flow);
            results.Rows.Clear();
            ShowStart();
        };
        runButton.Click += async (_, _) =>
        {
            var messages = input.Lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (messages.Length == 0) return;
            if (messages.Length > 10_000)
            {
                status.Text = "Envie até 10.000 mensagens por lote.";
                return;
            }
            var current = run;
            runButton.Enabled = reset.Enabled = false;
            input.ReadOnly = true;
            try
            {
                var steps = await Task.Run(() => messages.Select(message =>
                {
                    var step = current.Evaluate(message);
                    current.Advance(step);
                    return step;
                }).ToArray());
                if (IsDisposed) return;
                foreach (var step in steps)
                    results.Rows.Add(step.Input, step.Stage.Name, step.Branch is null ? "Sem condição correspondente" : RuleEditor.BranchLabel(step.Branch),
                        step.Branch?.Reply, step.Next.Name);
                input.Clear();
                if (results.Rows.Count > 0) results.FirstDisplayedScrollingRowIndex = results.Rows.Count - 1;
                UpdateStatus();
            }
            catch (Exception ex)
            {
                if (!IsDisposed) status.Text = $"Falha no teste. {ex.Message}";
            }
            finally
            {
                if (!IsDisposed) { runButton.Enabled = reset.Enabled = true; input.ReadOnly = false; }
            }
        };
        actions.Controls.AddRange([runButton, reset, status]);
        layout.Controls.Add(actions, 0, 2);
        results.Columns.Add("input", "Entrada avaliada");
        results.Columns.Add("stage", "Etapa");
        results.Columns.Add("branch", "Saída escolhida");
        results.Columns.Add("reply", "Resposta");
        results.Columns.Add("next", "Próxima etapa");
        layout.Controls.Add(results, 0, 3);
        Controls.Add(layout);
        ShowStart();
    }

    private void ShowStart()
    {
        if (run.OpeningMessage is { } opening)
            results.Rows.Add("", "Início", "Mensagem inicial", opening, run.Current.Name);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var interactions = results.Rows.Count - (run.OpeningMessage is null ? 0 : 1);
        status.Text = $"{interactions} interações · próxima entrada: {run.Current.Name}";
    }
}
