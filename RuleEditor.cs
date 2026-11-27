namespace WhatsBrowser;

internal static class Ui
{
    public static readonly Color Ink = Color.FromArgb(25, 43, 35);
    public static readonly Color Muted = Color.FromArgb(79, 101, 90);
    public static readonly Color Green = Color.FromArgb(19, 103, 70);
    public static readonly Color Alert = Color.FromArgb(133, 61, 37);
    public static readonly Color Border = Color.FromArgb(217, 228, 221);
    public static readonly Color Canvas = Color.FromArgb(243, 247, 244);

    public static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(0, 30), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Green : Color.White, ForeColor = primary ? Color.White : Ink,
            Padding = new Padding(7, 0, 7, 0), Margin = new Padding(0, 3, 5, 3), Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        button.FlatAppearance.BorderColor = primary ? Green : Border;
        return button;
    }
}

internal sealed class RuleEditor : UserControl
{
    internal static readonly string[] OperatorNames = ["Contém", "É igual a", "Começa com", "Termina com", "Qualquer mensagem", "Regex"];
    private readonly string directory;
    private readonly ComboBox flowPicker = Picker("Fluxo selecionado");
    private readonly ComboBox stagePicker = Picker("Entrada selecionada");
    private readonly ComboBox branchPicker = Picker("Condição selecionada");
    private readonly ComboBox nextPicker = Picker("Próxima entrada");
    private readonly ComboBox op = Picker("Tipo de condição");
    private readonly CheckBox normalize = new() { Text = "Normalizar entrada", AutoSize = true, Margin = new Padding(3, 9, 3, 6) };
    private readonly Label openingStatus = new() { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(4, 10, 12, 4) };
    private readonly FlowCanvas canvas = new() { Dock = DockStyle.Fill };
    private readonly TextBox name = Field("Nome da entrada", RuleEngine.MaxName);
    private readonly TextBox branchName = Field("Nome da condição", RuleEngine.MaxName);
    private readonly TextBox match = Field("Texto ou padrão da condição", RuleEngine.MaxMatch);
    private readonly TextBox reply = new()
    {
        Dock = DockStyle.Fill, MaxLength = RuleEngine.MaxReply, Multiline = true,
        ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, AccessibleName = "Resposta do caminho",
    };
    private readonly Label note = new() { Dock = DockStyle.Fill, ForeColor = Ui.Muted, AutoEllipsis = true };
    private readonly Button newTarget = Ui.Button("+ Próxima entrada");
    private readonly Button deleteBranch = Ui.Button("Excluir saída");
    private readonly Button up = Ui.Button("↑");
    private readonly Button down = Ui.Button("↓");
    private FlowLibrary library = FlowLibrary.Create();
    private ReplyFlow flow = ReplyFlow.Empty;
    private Guid selected;
    private int branchIndex = -1;
    private bool updating;
    private bool dirty;

    public event EventHandler? Changed;
    public ReplyFlow Snapshot => flow.Copy();
    private FlowStage Active => flow.Stages.First(stage => stage.Id == selected);

    public RuleEditor(string directory)
    {
        this.directory = directory;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        ForeColor = Ui.Ink;
        op.Items.AddRange(OperatorNames);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 235));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.Controls.Add(BuildFlowBar(), 0, 0);
        var settings = Row(ActionButton("Mensagem inicial…", EditOpeningMessage), openingStatus, normalize);
        settings.Padding = new Padding(7, 0, 4, 0);
        layout.Controls.Add(settings, 0, 1);
        layout.Controls.Add(BuildCanvasBar(), 0, 2);
        layout.Controls.Add(canvas, 0, 3);
        layout.Controls.Add(BuildInspector(), 0, 4);
        note.Padding = new Padding(9, 2, 0, 0);
        layout.Controls.Add(note, 0, 5);
        Controls.Add(layout);

        string? loadError = null;
        try { library = FlowLibrary.Load(directory); }
        catch (Exception ex) { loadError = $"Falha ao ler os fluxos. {ex.Message}"; }
        OpenActive();
        if (loadError is not null) note.Text = loadError;
        flowPicker.SelectionChangeCommitted += (_, _) => SwitchFlow();
        stagePicker.SelectionChangeCommitted += (_, _) =>
        {
            if (stagePicker.SelectedItem is FlowStage stage) SelectStage(stage.Id, locate: true);
        };
        branchPicker.SelectionChangeCommitted += (_, _) => SelectBranch(branchPicker.SelectedIndex);
        nextPicker.SelectionChangeCommitted += (_, _) => UpdateBranch();
        op.SelectionChangeCommitted += (_, _) => UpdateBranch();
        branchName.TextChanged += (_, _) => UpdateBranch();
        match.TextChanged += (_, _) => UpdateBranch();
        reply.TextChanged += (_, _) => UpdateBranch();
        name.TextChanged += (_, _) =>
        {
            if (updating) return;
            var stage = Active;
            UpdateStage(stage with { Name = string.IsNullOrWhiteSpace(name.Text) ? "Entrada sem título" : name.Text });
            MarkDirty(affectsRules: false);
        };
        name.Leave += (_, _) => { if (!updating) RefreshStagePickers(); };
        normalize.CheckedChanged += (_, _) =>
        {
            if (updating) return;
            flow = flow with { NormalizeInput = normalize.Checked };
            MarkDirty();
        };
        canvas.StageSelected += (id, index) => SelectStage(id, index);
        canvas.StageMoved += (id, point) =>
        {
            var stage = flow.Stages.First(item => item.Id == id);
            UpdateStage(stage with { X = point.X, Y = point.Y });
            MarkDirty(affectsRules: false);
        };
        canvas.ConnectRequested += (source, index, target) =>
        {
            var stage = flow.Stages.First(item => item.Id == source);
            stage.Branches[index] = stage.Branches[index] with { NextStageId = target };
            SelectStage(source, index);
            MarkDirty();
        };
    }

    private static ComboBox Picker(string name) => new()
    {
        Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name, IntegralHeight = false, DropDownHeight = 260,
    };

    private static TextBox Field(string label, int limit) => new() { Dock = DockStyle.Fill, MaxLength = limit, AccessibleName = label };

    private static Button ActionButton(string text, Action click)
    {
        var button = Ui.Button(text);
        button.Click += (_, _) => click();
        return button;
    }

    private static FlowLayoutPanel Row(params Control[] items)
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, AutoSize = true };
        bar.Controls.AddRange(items);
        return bar;
    }

    private static TableLayoutPanel PickerRow(Control picker, Control actions)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(7, 2, 4, 0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        picker.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        row.Controls.Add(picker, 0, 0);
        row.Controls.Add(actions, 1, 0);
        return row;
    }

    private Control BuildFlowBar()
    {
        var options = Ui.Button("⋯");
        options.AccessibleName = "Opções do fluxo";
        var menu = new ContextMenuStrip();
        menu.Items.Add("Renomear fluxo", null, (_, _) => RenameFlow());
        menu.Items.Add("Importar JSON", null, (_, _) => Import());
        menu.Items.Add("Exportar JSON", null, (_, _) => Export());
        menu.Items.Add("Excluir fluxo", null, (_, _) => DeleteFlow());
        options.Click += (_, _) => menu.Show(options, 0, options.Height);
        options.Disposed += (_, _) => menu.Dispose();
        return PickerRow(flowPicker, Row(ActionButton("+ Fluxo", CreateFlow), ActionButton("Salvar", () => SaveActive()),
            ActionButton("Testar", TestFlow), options));
    }

    private void EditOpeningMessage()
    {
        var opening = FlowOpeningDialog.Edit(this, flow.Opening);
        if (opening is null || opening == flow.Opening) return;
        flow = flow with { Opening = opening };
        RefreshOpeningStatus();
        MarkDirty();
    }

    private void RefreshOpeningStatus() =>
        openingStatus.Text = flow.Opening is { Enabled: true } ? "Configurada" : "Desativada";

    private Control BuildCanvasBar()
    {
        var fit = ActionButton("Enquadrar", canvas.Fit);
        var more = Ui.Button("⋯");
        more.AccessibleName = "Opções da entrada";
        var actions = new ContextMenuStrip();
        actions.Items.Add("Definir como entrada inicial", null, (_, _) =>
        {
            flow = flow with { StartStageId = selected };
            MarkDirty();
            Present();
        });
        actions.Items.Add("Excluir entrada", null, (_, _) => DeleteStage());
        more.Click += (_, _) =>
        {
            actions.Items[0].Enabled = flow.StartStageId != selected;
            actions.Items[1].Enabled = flow.Stages.Count > 1;
            actions.Show(more, 0, more.Height);
        };
        more.Disposed += (_, _) => actions.Dispose();
        return PickerRow(stagePicker, Row(ActionButton("+ Entrada", () => AddStage(false)), fit, more));
    }

    private Control BuildInspector()
    {
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, Padding = new Padding(8, 4, 8, 1), Margin = Padding.Empty };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 77));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var height in new[] { 30, 40, 29, 31 }) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        void RowField(int row, string caption, Control value, Control? side = null)
        {
            fields.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            fields.Controls.Add(value, 1, row);
            if (side is null) fields.SetColumnSpan(value, 2);
            else fields.Controls.Add(side, 2, row);
        }
        up.AccessibleName = "Subir a saída";
        down.AccessibleName = "Descer a saída";
        up.Click += (_, _) => MoveBranch(-1);
        down.Click += (_, _) => MoveBranch(1);
        deleteBranch.Click += (_, _) => RemoveBranch();
        newTarget.Click += (_, _) => AddStage(true);
        RowField(0, "Entrada", name);
        RowField(1, "Saídas", branchPicker, Row(ActionButton("+ Saída", AddBranch), up, down, deleteBranch));
        RowField(2, "Título", branchName, op);
        RowField(3, "Condição", match);
        RowField(4, "Resposta", reply);
        RowField(5, "Próxima", nextPicker, Row(newTarget));
        return fields;
    }

    private void RefreshStagePickers()
    {
        var branch = branchIndex >= 0 ? Active.Branches[branchIndex] : null;
        stagePicker.Items.Clear();
        stagePicker.Items.AddRange(flow.Stages.Cast<object>().ToArray());
        stagePicker.SelectedItem = Active;
        nextPicker.Items.Clear();
        nextPicker.Items.Add(new Target(null, "Finalizar e voltar ao início"));
        foreach (var stage in flow.Stages) nextPicker.Items.Add(new Target(stage.Id, stage.Name));
        nextPicker.SelectedIndex = branch?.NextStageId is { } next ? flow.Stages.FindIndex(stage => stage.Id == next) + 1 : 0;
    }

    private void Present() => canvas.Present(flow, selected, branchIndex);

    private void SelectStage(Guid id, int index = 0, bool locate = false)
    {
        selected = id;
        canvas.CancelConnection();
        updating = true;
        name.Text = Active.Name;
        normalize.Checked = flow.NormalizeInput;
        RefreshOpeningStatus();
        updating = false;
        SelectBranch(index);
        if (locate) canvas.CenterOn(Active);
    }

    private void SelectBranch(int index)
    {
        var branches = Active.Branches;
        branchIndex = branches.Count == 0 ? -1 : Math.Clamp(index, 0, branches.Count - 1);
        var rule = branchIndex >= 0 ? branches[branchIndex] : null;
        updating = true;
        branchPicker.Items.Clear();
        branchPicker.Items.AddRange(branches.Select(BranchLabel).Cast<object>().ToArray());
        branchPicker.SelectedIndex = branchIndex;
        branchName.Text = rule?.Name ?? "";
        op.SelectedIndex = rule is null ? -1 : (int)rule.Operator;
        match.Text = rule?.Match ?? "";
        reply.Text = rule?.Reply ?? "";
        RefreshStagePickers();
        updating = false;
        branchName.Enabled = op.Enabled = reply.Enabled = nextPicker.Enabled = newTarget.Enabled = deleteBranch.Enabled = rule is not null;
        match.Enabled = rule is not null && rule.Operator != RuleOperator.Any;
        match.PlaceholderText = rule?.Operator == RuleOperator.Regex ? @"Ex. /^pedido\s+\d+$/i" : "Texto procurado";
        up.Enabled = branchIndex > 0;
        down.Enabled = branchIndex >= 0 && branchIndex < branches.Count - 1;
        Present();
    }

    internal static string BranchLabel(ReplyRule rule) => string.IsNullOrWhiteSpace(rule.Name) ? OperatorNames[(int)rule.Operator] : rule.Name;

    private void UpdateBranch()
    {
        if (updating || branchIndex < 0 || op.SelectedIndex < 0) return;
        var operation = (RuleOperator)op.SelectedIndex;
        var item = new ReplyRule(branchName.Text, operation, match.Text, reply.Text, (nextPicker.SelectedItem as Target)?.Id);
        Active.Branches[branchIndex] = item;
        branchPicker.Items[branchIndex] = BranchLabel(item);
        match.Enabled = operation != RuleOperator.Any;
        match.PlaceholderText = operation == RuleOperator.Regex ? @"Ex. /^pedido\s+\d+$/i" : "Texto procurado";
        MarkDirty();
        if (operation == RuleOperator.Regex && !string.IsNullOrWhiteSpace(item.Match) && !RuleEngine.IsValidRegex(item.Match))
            note.Text = "Regex inválida. Confira o padrão e as flags.";
        Present();
    }

    private void AddBranch()
    {
        var items = Active.Branches;
        if (items.Count >= RuleEngine.MaxBranchesPerStage || flow.Stages.Sum(stage => stage.Branches.Count) >= RuleEngine.MaxBranches)
        {
            note.Text = "O fluxo atingiu o limite de saídas.";
            return;
        }
        items.Add(new ReplyRule($"Saída {items.Count + 1}", RuleOperator.Contains, "", ""));
        MarkDirty();
        SelectBranch(items.Count - 1);
        match.Focus();
    }

    private void RemoveBranch()
    {
        if (branchIndex < 0) return;
        canvas.CancelConnection();
        Active.Branches.RemoveAt(branchIndex);
        MarkDirty();
        SelectBranch(branchIndex);
    }

    private void MoveBranch(int delta)
    {
        var items = Active.Branches;
        var target = branchIndex + delta;
        if (branchIndex < 0 || target < 0 || target >= items.Count) return;
        canvas.CancelConnection();
        (items[target], items[branchIndex]) = (items[branchIndex], items[target]);
        MarkDirty();
        SelectBranch(target);
    }

    private void AddStage(bool connect)
    {
        if (flow.Stages.Count >= RuleEngine.MaxStages)
        {
            note.Text = $"O limite é de {RuleEngine.MaxStages} entradas.";
            return;
        }
        var origin = Active;
        var point = connect ? new Point(origin.X + 360, origin.Y + branchIndex * 50) : canvas.NewStagePoint();
        var stage = new FlowStage(Guid.NewGuid(), $"Entrada {flow.Stages.Count + 1}",
            Math.Clamp(point.X, -RuleEngine.MaxCoordinate, RuleEngine.MaxCoordinate),
            Math.Clamp(point.Y, -RuleEngine.MaxCoordinate, RuleEngine.MaxCoordinate), []);
        if (connect && branchIndex >= 0)
            origin.Branches[branchIndex] = origin.Branches[branchIndex] with { NextStageId = stage.Id };
        flow.Stages.Add(stage);
        MarkDirty();
        SelectStage(stage.Id, locate: true);
        name.Focus();
        name.SelectAll();
    }

    private void UpdateStage(FlowStage stage)
    {
        flow.Stages[flow.Stages.FindIndex(item => item.Id == stage.Id)] = stage;
        Present();
    }

    private void DeleteStage()
    {
        if (flow.Stages.Count <= 1) return;
        if (MessageBox.Show(this, $"Excluir “{Active.Name}” e suas ligações?", "Excluir entrada", MessageBoxButtons.YesNo,
            MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        flow.Stages.Remove(Active);
        foreach (var stage in flow.Stages)
            for (var i = 0; i < stage.Branches.Count; i++)
                if (stage.Branches[i].NextStageId == selected)
                    stage.Branches[i] = stage.Branches[i] with { NextStageId = null };
        if (flow.StartStageId == selected) flow = flow with { StartStageId = flow.Stages[0].Id };
        MarkDirty();
        SelectStage(flow.StartStageId);
    }

    private void MarkDirty(bool affectsRules = true)
    {
        dirty = true;
        note.Text = "Alterações pendentes de salvar.";
        if (affectsRules) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshFlows()
    {
        flowPicker.Items.Clear();
        flowPicker.Items.AddRange(library.Flows.Cast<object>().ToArray());
        flowPicker.SelectedItem = library.GetActive();
    }

    private void OpenActive()
    {
        flow = library.GetActive().Flow.Copy();
        dirty = false;
        RefreshFlows();
        SelectStage(flow.StartStageId);
        canvas.Fit();
        note.Text = "Clique numa saída para ligá-la a outra entrada. Arraste o fundo e use a roda para ampliar.";
    }

    private bool Commit(FlowLibrary next)
    {
        try
        {
            next.Save(directory);
            library = next;
            return true;
        }
        catch (Exception ex) { note.Text = $"Falha ao salvar. {ex.Message}"; return false; }
    }

    private bool ValidatePatterns()
    {
        var problem = flow.Stages.FirstOrDefault(stage => stage.Branches.Any(rule => rule.Operator == RuleOperator.Regex &&
            !string.IsNullOrWhiteSpace(rule.Match) && !RuleEngine.IsValidRegex(rule.Match)));
        if (problem is null) return true;
        note.Text = $"Corrija a regex em “{problem.Name}”.";
        return false;
    }

    private bool SaveActive()
    {
        if (!ValidatePatterns()) return false;
        var entries = library.Flows.Select(item => item.Id == library.ActiveId ? item with { Flow = flow.Copy() } : item).ToList();
        if (!Commit(library with { Flows = entries })) return false;
        dirty = false;
        RefreshFlows();
        note.Text = "Fluxo salvo.";
        return true;
    }

    private bool CanLeave()
    {
        if (!dirty) return true;
        return MessageBox.Show(this, "Salvar as alterações deste fluxo antes de continuar?", "Alterações pendentes",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) switch
        {
            DialogResult.Yes => SaveActive(),
            DialogResult.No => true,
            _ => false,
        };
    }

    private void SwitchFlow()
    {
        if (flowPicker.SelectedItem is not NamedFlow target || target.Id == library.ActiveId) return;
        if (!CanLeave() || !Commit(library with { ActiveId = target.Id }))
        {
            flowPicker.SelectedItem = library.GetActive();
            return;
        }
        OpenActive();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string? AskName(string title, string initial, Guid? id = null)
    {
        var input = Microsoft.VisualBasic.Interaction.InputBox("Nome do fluxo", title, initial).Trim();
        if (input.Length == 0) return null;
        if (input.Length > RuleEngine.MaxName || library.Flows.Any(item => item.Id != id &&
            string.Equals(item.Name, input, StringComparison.CurrentCultureIgnoreCase)))
        {
            note.Text = "Use um nome de fluxo único, com até 60 caracteres.";
            return null;
        }
        return input;
    }

    private void CreateFlow()
    {
        if (library.Flows.Count >= FlowLibrary.MaxFlows) { note.Text = $"O limite é de {FlowLibrary.MaxFlows} fluxos."; return; }
        var title = AskName("Novo fluxo", "");
        if (title is null || !CanLeave()) return;
        var item = new NamedFlow(Guid.NewGuid(), title, ReplyFlow.Empty);
        if (!Commit(library with { ActiveId = item.Id, Flows = [..library.Flows, item] })) return;
        OpenActive();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RenameFlow()
    {
        var active = library.GetActive();
        var title = AskName("Renomear fluxo", active.Name, active.Id);
        if (title is null || title == active.Name) return;
        var entries = library.Flows.Select(item => item.Id == active.Id ? item with { Name = title } : item).ToList();
        if (!Commit(library with { Flows = entries })) return;
        RefreshFlows();
        note.Text = "Fluxo renomeado.";
    }

    private void DeleteFlow()
    {
        var active = library.GetActive();
        if (MessageBox.Show(this, $"Excluir “{active.Name}” e suas entradas?", "Excluir fluxo",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        var remaining = library.Flows.Where(item => item.Id != active.Id).ToList();
        var next = remaining.Count > 0 ? library with { ActiveId = remaining[0].Id, Flows = remaining } : FlowLibrary.Create();
        if (!Commit(next)) return;
        OpenActive();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog { Filter = "Fluxo JSON (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var imported = FlowFormat.Load(dialog.FileName);
            if (!CanLeave()) return;
            flow = imported;
            SelectStage(flow.StartStageId);
            canvas.Fit();
            MarkDirty();
            note.Text = "Fluxo importado. Salve para manter este fluxo.";
        }
        catch (Exception ex) { note.Text = $"Falha ao importar. {ex.Message}"; }
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "Fluxo JSON (*.json)|*.json", FileName = "fluxo-whatsbrowser.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { FlowFormat.Save(dialog.FileName, flow); note.Text = "Fluxo exportado."; }
        catch (Exception ex) { note.Text = $"Falha ao exportar. {ex.Message}"; }
    }

    private void TestFlow()
    {
        if (!ValidatePatterns()) return;
        using var test = new FlowTester(Snapshot);
        test.ShowDialog(this);
    }

    private sealed record Target(Guid? Id, string Name)
    {
        public override string ToString() => Name;
    }
}
