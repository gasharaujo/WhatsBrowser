using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WhatsBrowser;

internal sealed class MainWindow : Form
{
    private const string WhatsAppUrl = "https://web.whatsapp.com/";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White };
    private readonly Button prepare = Ui.Button("Preparar resposta");
    private readonly Button activate = Ui.Button("Ativar automático", primary: true);
    private readonly Button reload = Ui.Button("Recarregar");
    private readonly Button print = Ui.Button("Print da conversa");
    private readonly Label activity = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(14, 0, 10, 0), ForeColor = Ui.Muted,
    };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 700 };
    private readonly string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhatsBrowser");
    private readonly RuleEditor editor;
    private readonly QuickReplyPanel quickReplies;
    private readonly WhatsAppSender sender;
    private readonly string script;
    private MatchSession? session;
    private bool ready;
    private bool busy;
    private bool closing;

    public MainWindow()
    {
        using (var resource = typeof(MainWindow).Assembly.GetManifestResourceStream("WhatsAppBridge.js")!)
        using (var reader = new StreamReader(resource))
            script = reader.ReadToEnd();

        Text = "WhatsBrowser";
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1050, 670);
        Size = new Size(1440, 900);
        AutoScaleMode = AutoScaleMode.Dpi;
        editor = new RuleEditor(dataDir);
        quickReplies = new QuickReplyPanel(dataDir);
        sender = new WhatsAppSender(CallAsync, DispatchInputAsync);
        quickReplies.SendRequested += SendQuickReplyAsync;
        editor.Changed += (_, _) =>
        {
            if (session is not null) Stop("Automático pausado porque o fluxo mudou.");
        };

        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        shell.Controls.Add(CreateHeader(), 0, 0);
        var workspace = new SplitContainer
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Ui.Border, Size = new Size(1300, 700),
            Panel1MinSize = 430, Panel2MinSize = 420, SplitterDistance = 565, SplitterWidth = 6,
        };
        var tabs = new TabControl { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 1, 1) };
        var rulesTab = new TabPage("Fluxos");
        rulesTab.Controls.Add(editor);
        var repliesTab = new TabPage("Respostas prontas");
        repliesTab.Controls.Add(quickReplies);
        tabs.TabPages.AddRange([rulesTab, repliesTab]);
        web.Margin = new Padding(0, 1, 0, 1);
        workspace.Panel1.BackColor = Color.White;
        workspace.Panel1.Controls.Add(tabs);
        workspace.Panel2.Controls.Add(web);
        shell.Controls.Add(workspace, 0, 1);
        shell.Controls.Add(activity, 0, 2);
        Controls.Add(shell);

        prepare.Click += async (_, _) => await PrepareLatestAsync();
        print.Click += async (_, _) => await PrintConversationAsync();
        activate.Click += async (_, _) =>
        {
            if (session is not null) Stop("Envio automático desativado.");
            else await StartAutomaticAsync();
        };
        reload.Click += (_, _) =>
        {
            Stop("Recarregando WhatsApp Web…");
            web.CoreWebView2?.Reload();
        };
        timer.Tick += async (_, _) => await TickAsync();
        FormClosing += (_, _) =>
        {
            closing = true;
            Stop("");
        };
        FormClosed += (_, _) => timer.Dispose();
        Shown += async (_, _) => await InitializeWhatsAppAsync();
        SetStatus("Inicializando o WhatsApp Web…");
        UpdateButtons();
    }

    private Control CreateHeader()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(14, 8, 14, 6), Margin = Padding.Empty,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var brand = new Label
        {
            Dock = DockStyle.Fill, Text = "WhatsBrowser", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ui.Ink,
            Font = new Font(Font.FontFamily, 17, FontStyle.Bold), Margin = Padding.Empty,
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight, Anchor = AnchorStyles.Right, Margin = Padding.Empty,
        };
        actions.Controls.AddRange([reload, print, prepare, activate]);
        layout.Controls.Add(brand, 0, 0);
        layout.Controls.Add(actions, 1, 0);
        return layout;
    }

    private async Task InitializeWhatsAppAsync()
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(dataDir, "WebView2"));
            if (closing) return;
            var controller = environment.CreateCoreWebView2ControllerOptions();
            controller.ProfileName = "Default";
            controller.IsInPrivateModeEnabled = false;
            await web.EnsureCoreWebView2Async(environment, controller);
            if (closing) return;

            var core = web.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                if (!IsWhatsApp(e.Uri))
                {
                    e.Cancel = true;
                    return;
                }
                ready = false;
                Stop("Carregando WhatsApp Web…");
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationCompleted += (_, e) =>
            {
                ready = e.IsSuccess && IsWhatsApp(core.Source);
                UpdateButtons();
                SetStatus(ready
                    ? "Vincule sua conta se aparecer o QR Code. Depois, abra uma conversa e configure as regras."
                    : $"O WhatsApp Web não carregou ({e.WebErrorStatus}). Clique em Recarregar.");
            };
            core.ProcessFailed += (_, _) =>
            {
                ready = false;
                Stop("A WebView foi interrompida. Clique em Recarregar.");
            };
            core.Navigate(WhatsAppUrl);
        }
        catch (Exception ex) when (!closing)
        {
            SetStatus($"Falha ao iniciar a WebView2. Verifique o runtime Microsoft Edge WebView2. {ex.Message}");
        }
    }

    private async Task SendQuickReplyAsync(QuickReply item)
    {
        if (!Begin()) return;
        try
        {
            var sent = await SendMessageAsync(item.Text);
            if (!closing)
                SetStatus(sent.State == "sent"
                    ? $"“{item.Title}” enviada na conversa aberta."
                    : Describe(sent.State));
        }
        catch (Exception ex)
        {
            if (!closing) SetStatus($"Falha ao enviar a resposta pronta. {ex.Message}");
        }
        finally { End(); }
    }

    private Task<PageResult> SendMessageAsync(string reply, MatchSession? active = null, IncomingMessage? message = null)
    {
        if (ActiveForm == this) web.Focus();
        return sender.SendAsync(reply, active?.Token, message?.Id, () => !closing && ready && (active is null || session == active));
    }

    private Task DispatchInputAsync(string method, object parameters)
    {
        var core = web.CoreWebView2;
        if (closing || !ready || core is null || !IsWhatsApp(core.Source))
            throw new InvalidOperationException("O WhatsApp Web mudou de página.");
        return core.CallDevToolsProtocolMethodAsync(method, JsonSerializer.Serialize(parameters, WebJson));
    }

    private async Task PrintConversationAsync()
    {
        if (!Begin()) return;
        try
        {
            var token = Guid.NewGuid();
            var chat = await CallAsync(new { action = "print-messages", token });
            if (closing) return;
            if (chat.State != "ready" || chat.Messages is not { Count: > 0 })
            {
                SetStatus(Describe(chat.State));
                return;
            }
            using var dialog = new ConversationPrint(chat.Title, chat.Messages);
            if (dialog.ShowDialog(this) != DialogResult.OK || web.CoreWebView2 is not { } core) return;
            SetStatus("Capturando a conversa no WhatsApp Web…");
            var capture = new ConversationCapture(core, CallAsync);
            await capture.SaveAsync(token, dialog.FirstId, dialog.OutputPath, percent =>
            {
                if (!closing) SetStatus($"Capturando conversa · {percent}%");
            });
            if (!closing) SetStatus("Print da conversa salvo em PNG.");
        }
        catch (Exception ex)
        {
            if (!closing) SetStatus($"Falha ao capturar a conversa. {ex.Message}");
        }
        finally { End(); }
    }

    private async Task PrepareLatestAsync()
    {
        if (!Begin()) return;
        try
        {
            var run = CreateRun();
            if (run is null) return;
            var chat = await CallAsync(new { action = "read" });
            if (!CanUse(chat)) return;
            if (chat.Latest is not { } message || string.IsNullOrWhiteSpace(message.Text))
            {
                SetStatus("A última mensagem recebida não tem texto que possa ser lido.");
                return;
            }
            var step = await Task.Run(() => run.Evaluate(message.Text));
            if (step.Branch is null || string.IsNullOrWhiteSpace(step.Branch.Reply))
            {
                SetStatus("Nenhuma regra correspondeu à última mensagem recebida.");
                return;
            }
            var result = await CallAsync(new { action = "stage", token = Guid.NewGuid(), message.Id, reply = step.Branch.Reply.Trim() });
            SetStatus(result.State == "prepared"
                ? $"Saída “{RuleEditor.BranchLabel(step.Branch)}” aplicada. A resposta está no campo do WhatsApp."
                : Describe(result.State));
        }
        catch (Exception ex)
        {
            if (!closing) SetStatus($"Falha ao preparar a resposta. {ex.Message}");
        }
        finally { End(); }
    }

    private async Task StartAutomaticAsync()
    {
        if (!Begin()) return;
        try
        {
            var run = CreateRun(allowOpening: true);
            if (run is null) return;
            var token = Guid.NewGuid();
            var chat = await CallAsync(new { action = "watch-start", token });
            if (!CanUse(chat)) return;
            var title = string.IsNullOrWhiteSpace(chat.Title) ? "esta conversa" : $"“{chat.Title}”";
            var opening = run.OpeningMessage;
            var decision = opening is not null
                ? FlowOpeningDialog.Confirm(this, title, opening)
                : MessageBox.Show(this, $"Enviar automaticamente respostas às novas mensagens de {title} enquanto a conversa estiver aberta?",
                    "Ativar envio automático", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if ((decision != DialogResult.Yes && !(opening is not null && decision == DialogResult.No)) || !ready || closing) return;

            var current = await CallAsync(new { action = "watch-arm", token });
            if (!CanUse(current)) return;
            var active = new MatchSession(token, title, run);
            session = active;
            string? openingStatus = null;
            if (opening is not null && decision == DialogResult.Yes)
            {
                UpdateButtons();
                SetStatus($"Enviando a mensagem inicial em {title}…");
                var sent = await SendMessageAsync(opening, active);
                if (session != active) return;
                if (sent.State is not ("sent" or "submitted"))
                {
                    Stop($"Falha na mensagem inicial. {Describe(sent.State)}");
                    return;
                }
                openingStatus = sent.State == "sent" ? "Mensagem inicial enviada." : "Envio inicial acionado. Confirmação pendente.";
            }
            timer.Start();
            var status = $"Automático ativo em {title}. Próxima entrada: {run.Current.Name}.";
            SetStatus(openingStatus is null ? status : $"{openingStatus} {status}");
        }
        catch (Exception ex)
        {
            if (!closing) Stop($"Falha ao iniciar o automático. {ex.Message}");
        }
        finally { End(); }
    }

    private async Task TickAsync()
    {
        if (session is not { } active || !Begin()) return;
        try
        {
            var chat = await CallAsync(new { action = "watch-poll", token = active.Token });
            if (session != active) return;
            if (chat.State == "history")
            {
                SetStatus("Automático ativo. Vá ao fim da conversa para aguardar mensagens novas.");
                return;
            }
            if (chat.State != "ready")
            {
                Stop($"Automático pausado. {Describe(chat.State)}");
                return;
            }
            if (chat.Rebase) SetStatus($"Histórico atualizado. Aguardando uma mensagem para “{active.Run.Current.Name}”.");
            foreach (var message in chat.Fresh ?? [])
            {
                if (session != active) return;
                if (string.IsNullOrWhiteSpace(message.Text) || !active.Handled.Add(message.Id)) continue;
                var step = await Task.Run(() => active.Run.Evaluate(message.Text));
                if (session != active) return;
                if (step.Branch is null)
                {
                    SetStatus($"Mensagem recebida em “{step.Stage.Name}”. Nenhuma saída correspondeu.");
                    continue;
                }
                var submitted = false;
                if (!string.IsNullOrWhiteSpace(step.Branch.Reply))
                {
                    var sent = await SendMessageAsync(step.Branch.Reply.Trim(), active, message);
                    if (session != active) return;
                    if (sent.State is not ("sent" or "submitted"))
                    {
                        Stop($"Automático pausado. {Describe(sent.State)}");
                        return;
                    }
                    submitted = sent.State == "submitted";
                }
                active.Run.Advance(step);
                SetStatus(submitted
                    ? $"Envio acionado em {active.Title}. Confirmação pendente. Próxima entrada: {step.Next.Name}."
                    : $"“{RuleEditor.BranchLabel(step.Branch)}” em {active.Title}. Próxima entrada: {step.Next.Name}.");
            }
        }
        catch (Exception ex)
        {
            if (!closing) Stop($"Automático pausado. {ex.Message}");
        }
        finally { End(); }
    }

    private FlowRun? CreateRun(bool allowOpening = false)
    {
        var run = new FlowRun(editor.Snapshot);
        if ((allowOpening && run.OpeningMessage is not null) || run.Current.Branches.Any(RuleEngine.IsComplete)) return run;
        SetStatus("A entrada inicial precisa de uma saída com condição e resposta ou próxima entrada.");
        return null;
    }

    private bool CanUse(PageResult chat)
    {
        if (chat.State == "ready") return true;
        SetStatus(Describe(chat.State));
        return false;
    }

    private async Task<PageResult> CallAsync(object request)
    {
        var core = web.CoreWebView2;
        if (!ready || closing || core is null || !IsWhatsApp(core.Source))
            return new PageResult { State = "outside" };
        var json = await core.ExecuteScriptAsync($"{script}({JsonSerializer.Serialize(request, WebJson)})");
        return JsonSerializer.Deserialize<PageResult>(json, WebJson) ?? new PageResult { State = "failed" };
    }

    private static bool IsWhatsApp(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IdnHost == "web.whatsapp.com" && uri.IsDefaultPort;

    private bool Begin()
    {
        if (!ready || busy || closing) return false;
        busy = true;
        UpdateButtons();
        return true;
    }

    private void End()
    {
        busy = false;
        if (!closing) UpdateButtons();
    }

    private void Stop(string status)
    {
        session = null;
        timer.Stop();
        if (closing) return;
        SetStatus(status);
        UpdateButtons();
    }

    private void SetStatus(string value) => activity.Text = value;

    private void UpdateButtons()
    {
        reload.Enabled = web.CoreWebView2 is not null;
        print.Enabled = ready && !busy;
        quickReplies.SetSendAvailable(ready && !busy);
        prepare.Enabled = ready && !busy && session is null;
        activate.Enabled = session is not null || (ready && !busy);
        activate.Text = session is null ? "Ativar automático" : "Pausar automático";
        activate.BackColor = session is null ? Ui.Green : Ui.Alert;
    }

    private static string Describe(string state) => state switch
    {
        "no-chat" => "Vincule a conta e abra uma conversa do WhatsApp Web.",
        "no-messages" => "Abra uma conversa com mensagens visíveis no WhatsApp Web.",
        "history" => "Vá ao fim da conversa para acompanhar as mensagens novas.",
        "chat-changed" => "A conversa ou a mensagem alvo mudou.",
        "unidentified-chat" => "Abra uma conversa para escolher onde enviar a resposta.",
        "draft" => "A conversa já tem um rascunho. Envie ou limpe o rascunho para aplicar a regra.",
        "draft-changed" => "O texto do campo foi alterado antes do envio.",
        "focus-failed" => "Não foi possível dar foco ao campo da conversa.",
        "insert-failed" => "O WhatsApp não confirmou o texto no campo de mensagem.",
        "send-unavailable" => "O botão Enviar não foi identificado. Confira o campo da conversa.",
        "submitted" or "cleared" => "Envio acionado. A confirmação ainda não apareceu na conversa.",
        "unconfirmed" => "O campo mudou durante o envio. Confira a conversa antes de tentar novamente.",
        "pending" => "O WhatsApp manteve a resposta no campo. O envio não foi confirmado.",
        "canceled" => "A operação foi interrompida antes de confirmar o envio.",
        "outside" => "Aguarde o WhatsApp Web carregar.",
        _ => "Não foi possível acessar o campo da conversa. Confira se o WhatsApp Web está aberto.",
    };

    private sealed class MatchSession(Guid token, string title, FlowRun run)
    {
        public Guid Token { get; } = token;
        public string Title { get; } = title;
        public FlowRun Run { get; } = run;
        public HashSet<string> Handled { get; } = new(StringComparer.Ordinal);
    }

}
