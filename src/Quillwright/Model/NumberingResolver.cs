namespace Quillwright.Model;

/// <summary>Resolves one stable view of a document's numbering definitions.</summary>
internal sealed class NumberingResolver
{
    private readonly NumberingDefinitions _numbering;
    private readonly Dictionary<int, AbstractNumbering> _definitions = [];
    private readonly Dictionary<int, NumberingInstance> _instances = [];

    public NumberingResolver(NumberingDefinitions numbering)
    {
        ArgumentNullException.ThrowIfNull(numbering);
        _numbering = numbering;

        // TryAdd intentionally retains the first entry, matching List.FirstOrDefault used by
        // NumberingDefinitions even for a malformed part containing duplicate identifiers.
        foreach (AbstractNumbering definition in numbering.Definitions)
            _definitions.TryAdd(definition.Id, definition);
        foreach (NumberingInstance instance in numbering.Instances)
            _instances.TryAdd(instance.Id, instance);
    }

    public NumberingInstance? FindInstance(int numberingId) =>
        _instances.GetValueOrDefault(numberingId);

    public AbstractNumbering? ResolveDefinition(int numberingId) =>
        ResolveDefinition(numberingId, depth: 0);

    public NumberingLevel? ResolveLevel(int numberingId, int level) =>
        ResolveLevel(numberingId, level, depth: 0);

    private AbstractNumbering? ResolveDefinition(int numberingId, int depth)
    {
        NumberingInstance? instance = FindInstance(numberingId);
        if (instance is null)
            return null;

        AbstractNumbering? definition = _definitions.GetValueOrDefault(instance.AbstractId);
        if (definition is null || definition.Levels.Count > 0)
            return definition;

        return Linked(definition, depth) is { } linked
            ? ResolveDefinition(linked, depth + 1)
            : definition;
    }

    private NumberingLevel? ResolveLevel(int numberingId, int level, int depth)
    {
        NumberingInstance? instance = FindInstance(numberingId);
        if (instance is null)
            return null;

        if (instance.Overrides.FirstOrDefault(candidate => candidate.Level == level)?.Definition is { } overridden)
            return overridden;

        AbstractNumbering? definition = _definitions.GetValueOrDefault(instance.AbstractId);
        if (definition is null)
            return null;

        if (definition.Levels.FirstOrDefault(candidate => candidate.Level == level) is { } declared)
            return declared;

        return Linked(definition, depth) is { } linked
            ? ResolveLevel(linked, level, depth + 1)
            : null;
    }

    private int? Linked(AbstractNumbering definition, int depth)
    {
        if (depth >= 4 || definition.NumberingStyleLink is not { } styleId)
            return null;

        return _numbering.Owner?.Styles.Find(styleId)?.ParagraphFormat.NumberingId;
    }
}
