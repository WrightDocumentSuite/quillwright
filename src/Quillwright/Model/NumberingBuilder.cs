namespace Quillwright.Model;

/// <summary>
/// Appends numbering definitions and instances during one document-building operation without
/// rescanning all previously appended identifiers for every addition.
/// </summary>
internal sealed class NumberingBuilder
{
    private readonly NumberingDefinitions _numbering;
    private int _nextDefinitionId;
    private int _nextInstanceId;

    public NumberingBuilder(NumberingDefinitions numbering)
    {
        ArgumentNullException.ThrowIfNull(numbering);
        _numbering = numbering;
        _nextDefinitionId = numbering.Definitions.Count == 0
            ? 0
            : checked(numbering.Definitions.Max(static definition => definition.Id) + 1);
        _nextInstanceId = numbering.Instances.Count == 0
            ? 1
            : checked(numbering.Instances.Max(static instance => instance.Id) + 1);
    }

    public (AbstractNumbering Definition, NumberingInstance Instance) AddList(ListTemplate template) =>
        _numbering.AddList(template, checked(_nextDefinitionId++), checked(_nextInstanceId++));

    public NumberingInstance AddInstance(int abstractId)
    {
        var instance = new NumberingInstance
        {
            Id = checked(_nextInstanceId++),
            AbstractId = abstractId,
        };
        _numbering.Instances.Add(instance);
        return instance;
    }
}
