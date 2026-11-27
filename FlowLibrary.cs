namespace WhatsBrowser;

internal sealed record NamedFlow(Guid Id, string Name, ReplyFlow Flow)
{
    public override string ToString() => Name;
}

internal sealed record FlowLibrary(int Version, Guid ActiveId, List<NamedFlow> Flows)
{
    public const int MaxFlows = 30;

    public NamedFlow GetActive() => Flows.First(item => item.Id == ActiveId);

    public static FlowLibrary Create(ReplyFlow? initial = null)
    {
        var item = new NamedFlow(Guid.NewGuid(), "Fluxo principal", initial ?? ReplyFlow.Empty);
        return new FlowLibrary(1, item.Id, [item]);
    }

    public static FlowLibrary Load(string directory)
    {
        var path = Filename(directory);
        if (!File.Exists(path))
        {
            var legacy = Path.Combine(directory, "fluxo.json");
            return Create(File.Exists(legacy) ? FlowFormat.Load(legacy) : null);
        }

        var library = LocalJson.Read<FlowLibrary>(path, 192);
        if (library?.Version != 1 || library.Flows is null || library.Flows.Count is < 1 or > MaxFlows)
            throw new InvalidDataException("A biblioteca de fluxos é inválida.");
        var ids = new HashSet<Guid>();
        for (var i = 0; i < library.Flows.Count; i++)
        {
            var item = library.Flows[i];
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) ||
                string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > RuleEngine.MaxName)
                throw new InvalidDataException("A biblioteca contém um fluxo inválido.");
            library.Flows[i] = item with { Flow = FlowFormat.Upgrade(item.Flow) };
        }
        if (!ids.Contains(library.ActiveId))
            throw new InvalidDataException("O fluxo selecionado não existe na biblioteca.");
        return library;
    }

    public void Save(string directory) => LocalJson.Write(Filename(directory), this);

    private static string Filename(string directory) => Path.Combine(directory, "fluxos.json");
}
