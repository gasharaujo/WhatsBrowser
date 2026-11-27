using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jint;
using Jint.Runtime;

namespace WhatsBrowser;

public enum RuleOperator { Contains, Equals, StartsWith, EndsWith, Any, Regex }

public sealed record ReplyRule(string Name, RuleOperator Operator, string Match, string Reply, Guid? NextStageId = null);

public sealed record FlowStage(Guid Id, string Name, int X, int Y, List<ReplyRule> Branches)
{
    public override string ToString() => Name;
}

public sealed record FlowOpening(bool Enabled, string Text);

public sealed record ReplyFlow(int Version, Guid StartStageId, bool NormalizeInput, List<FlowStage> Stages,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<ReplyRule>? Rules = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FlowOpening? Opening = null)
{
    public static ReplyFlow Empty
    {
        get
        {
            var start = new FlowStage(Guid.NewGuid(), "Entrada inicial", 70, 75, []);
            return new ReplyFlow(2, start.Id, false, [start]);
        }
    }

    public ReplyFlow Copy() => this with { Stages = Stages.Select(stage => stage with { Branches = [..stage.Branches] }).ToList() };
}

public sealed record FlowStep(FlowStage Stage, ReplyRule? Branch, FlowStage Next, string Input);

public sealed class FlowRun
{
    private readonly ReplyFlow flow;
    private readonly Dictionary<Guid, FlowStage> stages;
    public FlowStage Current { get; private set; }
    public string? OpeningMessage => flow.Opening is { Enabled: true } opening ? opening.Text.Trim() : null;

    public FlowRun(ReplyFlow flow)
    {
        this.flow = flow;
        stages = flow.Stages.ToDictionary(stage => stage.Id);
        Current = stages[flow.StartStageId];
    }

    public FlowStep Evaluate(string message)
    {
        var input = flow.NormalizeInput ? RuleEngine.Preprocess(message) : message;
        var branch = RuleEngine.FindMatch(Current.Branches, input, flow.NormalizeInput);
        var next = branch is null ? Current : stages[branch.NextStageId ?? flow.StartStageId];
        return new FlowStep(Current, branch, next, input);
    }

    public void Advance(FlowStep step) => Current = step.Next;
}

public static class RuleEngine
{
    public const int MaxStages = 1500;
    public const int MaxBranches = 6000;
    public const int MaxBranchesPerStage = 100;
    public const int MaxCoordinate = 100_000;
    public const int MaxReply = 4096;
    public const int MaxMatch = 8192;
    public const int MaxName = 60;
    private static readonly ThreadLocal<Engine> RegexEngine = new(() => new Engine(options =>
        options.Constraints.RegexTimeout = TimeSpan.FromMilliseconds(50)));

    public static ReplyRule? FindMatch(IEnumerable<ReplyRule> rules, string message, bool preprocessTerms = false)
    {
        var text = Fold(message);
        foreach (var rule in rules)
        {
            if (rule.NextStageId is null && string.IsNullOrWhiteSpace(rule.Reply))
                continue;
            if (rule.Operator == RuleOperator.Any)
                return rule;
            if (rule.Operator == RuleOperator.Regex)
            {
                if (MatchesRegex(message, rule.Match)) return rule;
                continue;
            }

            var term = Fold(preprocessTerms ? Preprocess(rule.Match) : rule.Match);
            if (term.Length == 0)
                continue;

            var matches = rule.Operator switch
            {
                RuleOperator.Contains => text.Contains(term, StringComparison.Ordinal),
                RuleOperator.Equals => text == term,
                RuleOperator.StartsWith => text.StartsWith(term, StringComparison.Ordinal),
                RuleOperator.EndsWith => text.EndsWith(term, StringComparison.Ordinal),
                _ => false,
            };
            if (matches)
                return rule;
        }
        return null;
    }

    public static bool IsComplete(ReplyRule rule) =>
        (rule.NextStageId is not null || !string.IsNullOrWhiteSpace(rule.Reply)) &&
        (rule.Operator == RuleOperator.Any ||
         (!string.IsNullOrWhiteSpace(rule.Match) && (rule.Operator != RuleOperator.Regex || IsValidRegex(rule.Match))));

    public static bool IsValidRegex(string pattern) => TryRegex("", pattern, out _);

    private static bool MatchesRegex(string text, string expression) => TryRegex(text, expression, out var matched) && matched;

    private static bool TryRegex(string text, string expression, out bool matched)
    {
        matched = false;
        if (string.IsNullOrWhiteSpace(expression)) return false;
        var literal = expression.Trim();
        var end = literal.StartsWith('/') ? literal.LastIndexOf('/') : -1;
        var pattern = end > 0 ? literal[1..end] : expression;
        var flags = end > 0 ? literal[(end + 1)..] : "";
        if (end < 1)
        {
            var legacyFlags = Regex.Match(pattern, @"^\(\?([ims]+)\)");
            if (legacyFlags.Success)
            {
                flags = new string(legacyFlags.Groups[1].Value.Distinct().ToArray());
                pattern = pattern[legacyFlags.Length..];
            }
        }
        try
        {
            var engine = RegexEngine.Value!;
            engine.SetValue("pattern", pattern).SetValue("flags", flags).SetValue("subject", text);
            matched = engine.Evaluate("new RegExp(pattern, flags).test(subject)").AsBoolean();
            return true;
        }
        catch (JavaScriptException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static string Preprocess(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var text = Regex.Replace(decomposed, "[\u0300-\u036f]", "");
        text = Regex.Replace(text, "[^a-zA-Z0-9 ]", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string Fold(string value)
    {
        var normalized = new StringBuilder(value.Length);
        foreach (var letter in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(letter) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
                normalized.Append(Rune.ToLowerInvariant(letter));
        }
        return normalized.ToString().Trim();
    }
}

public static class FlowFormat
{
    public static ReplyFlow Load(string path)
    {
        return Upgrade(LocalJson.Read<ReplyFlow>(path, 64));
    }

    internal static ReplyFlow Upgrade(ReplyFlow? flow)
    {
        if (flow is { Version: 1, Rules: not null })
        {
            var upgraded = ReplyFlow.Empty;
            upgraded.Stages[0].Branches.AddRange(flow.Rules);
            flow = upgraded;
        }
        if (flow?.Version != 2 || flow.Stages is null || flow.Stages.Count is < 1 or > RuleEngine.MaxStages)
            throw new InvalidDataException("Selecione um fluxo WhatsBrowser válido.");
        if (flow.Opening is { } opening &&
            (opening.Text is null || opening.Text.Length > RuleEngine.MaxReply ||
             opening.Enabled && string.IsNullOrWhiteSpace(opening.Text)))
            throw new InvalidDataException("A mensagem inicial do fluxo é inválida.");
        var ids = new HashSet<Guid>();
        var branchCount = 0;
        foreach (var stage in flow.Stages)
        {
            if (stage is null || stage.Id == Guid.Empty || !ids.Add(stage.Id) || string.IsNullOrWhiteSpace(stage.Name) ||
                stage.Name.Length > RuleEngine.MaxName || Math.Abs((long)stage.X) > RuleEngine.MaxCoordinate ||
                Math.Abs((long)stage.Y) > RuleEngine.MaxCoordinate || stage.Branches is null || stage.Branches.Count > RuleEngine.MaxBranchesPerStage)
                throw new InvalidDataException("O fluxo contém uma entrada inválida.");
            branchCount += stage.Branches.Count;
        }
        if (!ids.Contains(flow.StartStageId) || branchCount > RuleEngine.MaxBranches)
            throw new InvalidDataException("O fluxo excede os limites ou falta a entrada inicial.");
        foreach (var rule in flow.Stages.SelectMany(stage => stage.Branches))
        {
            if (rule is null || rule.Name is null || rule.Match is null || rule.Reply is null ||
                !Enum.IsDefined(rule.Operator) || rule.Name.Length > RuleEngine.MaxName ||
                rule.Match.Length > RuleEngine.MaxMatch || rule.Reply.Length > RuleEngine.MaxReply ||
                (rule.NextStageId is { } target && !ids.Contains(target)))
                throw new InvalidDataException("O fluxo contém uma condição ou ligação inválida.");
        }
        return flow;
    }

    public static void Save(string path, ReplyFlow flow) => LocalJson.Write(path, flow);
}

internal static class LocalJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static T? Read<T>(string path, int megabytes)
    {
        using var file = File.OpenRead(path);
        if (file.Length > megabytes * 1_048_576L)
            throw new InvalidDataException($"O arquivo excede {megabytes} MB.");
        return JsonSerializer.Deserialize<T>(file, Options);
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(staging))
                JsonSerializer.Serialize(file, value, Options);
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging))
                File.Delete(staging);
        }
    }
}
