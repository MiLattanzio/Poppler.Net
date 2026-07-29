using System.Collections.ObjectModel;
using Poppler.Core;

namespace Poppler.OptionalContent;

internal sealed class PdfOptionalContentModel
{
    private readonly PdfDocumentCore _document;
    private readonly Dictionary<PdfReference, GroupInfo> _groupsByReference;
    private readonly Dictionary<PdfDictionary, GroupInfo> _groupsByDictionary;

    private PdfOptionalContentModel(
        PdfDocumentCore document,
        IReadOnlyList<PdfOptionalContentGroup> groups,
        PdfOptionalContentConfiguration? configuration,
        Dictionary<PdfReference, GroupInfo> groupsByReference,
        Dictionary<PdfDictionary, GroupInfo> groupsByDictionary)
    {
        _document = document;
        Groups = groups;
        Configuration = configuration;
        _groupsByReference = groupsByReference;
        _groupsByDictionary = groupsByDictionary;
    }

    public IReadOnlyList<PdfOptionalContentGroup> Groups { get; }
    public PdfOptionalContentConfiguration? Configuration { get; }

    public static PdfOptionalContentModel Empty(PdfDocumentCore document) =>
        new(
            document,
            Array.Empty<PdfOptionalContentGroup>(),
            configuration: null,
            new Dictionary<PdfReference, GroupInfo>(),
            new Dictionary<PdfDictionary, GroupInfo>(
                ReferenceEqualityComparer.Instance));

    public static PdfOptionalContentModel Read(
        PdfDocumentCore document,
        PdfDictionary catalog)
    {
        PdfDictionary? properties =
            catalog.GetValueOrNull("OCProperties").AsDictionary(document);
        PdfArray? sourceGroups =
            properties?.GetValueOrNull("OCGs").AsArray(document);
        if (properties is null || sourceGroups is null)
            return Empty(document);
        if (sourceGroups.Count > document.Options.MaximumOptionalContentGroups)
        {
            throw new PdfLimitException(
                "Optional-content group count exceeds the configured limit.");
        }

        var byReference = new Dictionary<PdfReference, GroupInfo>();
        var byDictionary = new Dictionary<PdfDictionary, GroupInfo>(
            ReferenceEqualityComparer.Instance);
        var groups = new List<GroupInfo>(sourceGroups.Count);
        for (int index = 0; index < sourceGroups.Count; index++)
        {
            PdfObject source = sourceGroups[index];
            PdfDictionary? dictionary = source.AsDictionary(document);
            if (dictionary is null ||
                dictionary.GetValueOrNull("Type").AsName(document) != "OCG")
            {
                document.AddDiagnostic(
                    PdfDiagnosticSeverity.Warning,
                    "optional-content.group.invalid",
                    $"Optional-content group {index + 1} is invalid and was skipped.");
                continue;
            }

            PdfReference? reference = source as PdfReference;
            if ((reference is not null && byReference.ContainsKey(reference)) ||
                byDictionary.ContainsKey(dictionary))
            {
                continue;
            }

            string id = reference is null
                ? $"direct:{index + 1}"
                : $"{reference.ObjectNumber}:{reference.Generation}";
            string name = ReadText(dictionary.GetValueOrNull("Name"), document);
            if (string.IsNullOrWhiteSpace(name))
                name = $"Layer {index + 1}";
            var group = new GroupInfo(
                id,
                name,
                ReadNames(dictionary.GetValueOrNull("Intent"), document, "View"),
                ReadViewState(dictionary, document),
                dictionary);
            groups.Add(group);
            byDictionary[dictionary] = group;
            if (reference is not null)
                byReference[reference] = group;
        }

        PdfDictionary? configuration =
            properties.GetValueOrNull("D").AsDictionary(document);
        PdfOptionalContentBaseState baseState =
            ReadBaseState(configuration?.GetValueOrNull("BaseState"), document);
        bool initial = baseState != PdfOptionalContentBaseState.Off;
        foreach (GroupInfo group in groups)
            group.Visible = initial;

        ApplyExplicitState(
            configuration?.GetValueOrNull("ON"),
            visible: true,
            document,
            byReference,
            byDictionary);
        ApplyExplicitState(
            configuration?.GetValueOrNull("OFF"),
            visible: false,
            document,
            byReference,
            byDictionary);
        ApplyAutomaticViewState(
            configuration?.GetValueOrNull("AS"),
            groups,
            document,
            byReference,
            byDictionary);

        var locked = ResolveGroups(
            configuration?.GetValueOrNull("Locked"),
            document,
            byReference,
            byDictionary);
        foreach (GroupInfo group in locked)
            group.Locked = true;

        IReadOnlyList<IReadOnlyList<string>> radioButtonGroups =
            ReadRadioButtonGroups(
                configuration?.GetValueOrNull("RBGroups"),
                document,
                byReference,
                byDictionary);
        var publicGroups = new ReadOnlyCollection<PdfOptionalContentGroup>(
            groups.Select(group =>
            {
                group.Public = new PdfOptionalContentGroup(
                    group.Id,
                    group.Name,
                    group.Intents,
                    group.Visible,
                    group.Locked,
                    group.ViewState);
                return group.Public;
            }).ToArray());
        PdfOptionalContentConfiguration publicConfiguration = new(
            ReadText(configuration?.GetValueOrNull("Name"), document),
            ReadText(configuration?.GetValueOrNull("Creator"), document),
            baseState,
            ReadNames(configuration?.GetValueOrNull("Intent"), document, "View"),
            radioButtonGroups);
        return new PdfOptionalContentModel(
            document,
            publicGroups,
            publicConfiguration,
            byReference,
            byDictionary);
    }

    public PdfOptionalContentEvaluator CreateEvaluator(
        IReadOnlyDictionary<string, bool>? overrides = null)
    {
        if (overrides is not null)
        {
            foreach (string id in overrides.Keys)
            {
                if (!Groups.Any(
                        group => string.Equals(
                            group.Id,
                            id,
                            StringComparison.Ordinal)))
                {
                    throw new ArgumentException(
                        $"Unknown optional-content group identifier '{id}'.",
                        nameof(overrides));
                }
            }
        }

        return new PdfOptionalContentEvaluator(this, overrides);
    }

    internal bool TryGetGroup(
        PdfObject? value,
        out PdfOptionalContentGroup group)
    {
        if (TryGetGroupInfo(value, out GroupInfo? info) &&
            info is not null)
        {
            group = info.Public!;
            return true;
        }

        group = null!;
        return false;
    }

    internal bool TryGetGroupInfo(PdfObject? value, out GroupInfo? group)
    {
        if (value is PdfReference reference &&
            _groupsByReference.TryGetValue(
                reference,
                out GroupInfo? referencedGroup))
        {
            group = referencedGroup;
            return true;
        }

        PdfDictionary? dictionary = value.AsDictionary(_document);
        if (dictionary is not null &&
            _groupsByDictionary.TryGetValue(
                dictionary,
                out GroupInfo? directGroup))
        {
            group = directGroup;
            return true;
        }

        group = null;
        return false;
    }

    internal PdfDocumentCore Document => _document;

    private static void ApplyExplicitState(
        PdfObject? source,
        bool visible,
        PdfDocumentCore document,
        IReadOnlyDictionary<PdfReference, GroupInfo> byReference,
        IReadOnlyDictionary<PdfDictionary, GroupInfo> byDictionary)
    {
        foreach (GroupInfo group in ResolveGroups(
                     source,
                     document,
                     byReference,
                     byDictionary))
        {
            group.Visible = visible;
        }
    }

    private static void ApplyAutomaticViewState(
        PdfObject? source,
        IReadOnlyList<GroupInfo> allGroups,
        PdfDocumentCore document,
        IReadOnlyDictionary<PdfReference, GroupInfo> byReference,
        IReadOnlyDictionary<PdfDictionary, GroupInfo> byDictionary)
    {
        PdfArray? entries = source.AsArray(document);
        if (entries is null)
            return;
        foreach (PdfObject entryObject in entries)
        {
            PdfDictionary? entry = entryObject.AsDictionary(document);
            if (entry is null ||
                entry.GetValueOrNull("Event").AsName(document) != "View" ||
                !ReadNames(entry.GetValueOrNull("Category"), document)
                    .Contains("View", StringComparer.Ordinal))
            {
                continue;
            }

            IReadOnlyList<GroupInfo> selected = ResolveGroups(
                entry.GetValueOrNull("OCGs"),
                document,
                byReference,
                byDictionary);
            if (selected.Count == 0)
                selected = allGroups;
            foreach (GroupInfo group in selected)
            {
                if (group.ViewState is { } visible)
                    group.Visible = visible;
            }
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> ReadRadioButtonGroups(
        PdfObject? source,
        PdfDocumentCore document,
        IReadOnlyDictionary<PdfReference, GroupInfo> byReference,
        IReadOnlyDictionary<PdfDictionary, GroupInfo> byDictionary)
    {
        PdfArray? groups = source.AsArray(document);
        if (groups is null)
            return Array.Empty<IReadOnlyList<string>>();
        return groups
            .Select(item => ResolveGroups(
                item,
                document,
                byReference,
                byDictionary))
            .Select(group =>
                (IReadOnlyList<string>)group
                    .Select(item => item.Id)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray())
            .Where(group => group.Count > 0)
            .ToArray();
    }

    private static IReadOnlyList<GroupInfo> ResolveGroups(
        PdfObject? source,
        PdfDocumentCore document,
        IReadOnlyDictionary<PdfReference, GroupInfo> byReference,
        IReadOnlyDictionary<PdfDictionary, GroupInfo> byDictionary)
    {
        if (source is null)
            return Array.Empty<GroupInfo>();
        IEnumerable<PdfObject> items = source.AsArray(document) is { } array
            ? array
            : new[] { source };
        var result = new List<GroupInfo>();
        foreach (PdfObject item in items)
        {
            GroupInfo? group = null;
            if (item is PdfReference reference)
                byReference.TryGetValue(reference, out group);
            if (group is null &&
                item.AsDictionary(document) is { } dictionary)
            {
                byDictionary.TryGetValue(dictionary, out group);
            }
            if (group is not null && !result.Contains(group))
                result.Add(group);
        }

        return result;
    }

    private static PdfOptionalContentBaseState ReadBaseState(
        PdfObject? value,
        PdfDocumentCore document) =>
        value.AsName(document) switch
        {
            "OFF" => PdfOptionalContentBaseState.Off,
            "Unchanged" => PdfOptionalContentBaseState.Unchanged,
            _ => PdfOptionalContentBaseState.On
        };

    private static bool? ReadViewState(
        PdfDictionary group,
        PdfDocumentCore document)
    {
        PdfDictionary? usage =
            group.GetValueOrNull("Usage").AsDictionary(document);
        PdfDictionary? view =
            usage?.GetValueOrNull("View").AsDictionary(document);
        return view?.GetValueOrNull("ViewState").AsName(document) switch
        {
            "ON" => true,
            "OFF" => false,
            _ => null
        };
    }

    private static IReadOnlyList<string> ReadNames(
        PdfObject? value,
        PdfDocumentCore document,
        string? fallback = null)
    {
        var names = new List<string>();
        PdfObject? resolved = value?.Resolve(document);
        if (resolved is PdfName name)
            names.Add(name.Value);
        else if (resolved is PdfArray array)
        {
            names.AddRange(array
                .Select(item => item.AsName(document))
                .OfType<string>());
        }
        if (names.Count == 0 && fallback is not null)
            names.Add(fallback);
        return names.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string ReadText(
        PdfObject? value,
        PdfDocumentCore document) =>
        value?.Resolve(document) switch
        {
            PdfString text => text.Text,
            PdfName name => name.Value,
            _ => ""
        };

    internal sealed class GroupInfo
    {
        public GroupInfo(
            string id,
            string name,
            IReadOnlyList<string> intents,
            bool? viewState,
            PdfDictionary dictionary)
        {
            Id = id;
            Name = name;
            Intents = intents;
            ViewState = viewState;
            Dictionary = dictionary;
        }

        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<string> Intents { get; }
        public bool? ViewState { get; }
        public PdfDictionary Dictionary { get; }
        public bool Visible { get; set; }
        public bool Locked { get; set; }
        public PdfOptionalContentGroup? Public { get; set; }
    }
}

internal sealed class PdfOptionalContentEvaluator
{
    private readonly PdfOptionalContentModel _model;
    private readonly IReadOnlyDictionary<string, bool> _overrides;

    public PdfOptionalContentEvaluator(
        PdfOptionalContentModel model,
        IReadOnlyDictionary<string, bool>? overrides)
    {
        _model = model;
        _overrides = overrides ?? EmptyOverrides;
    }

    public bool IsVisible(PdfObject? value, PdfDictionary? resources = null)
    {
        if (value is PdfName propertyName)
        {
            PdfDictionary? properties = resources?
                .GetValueOrNull("Properties")
                .AsDictionary(_model.Document);
            value = properties?.GetValueOrNull(propertyName.Value);
        }
        if (value is null)
            return true;

        var budget = new EvaluationBudget(
            _model.Document.Options.MaximumOptionalContentExpressionNodes);
        return EvaluateMembership(
            value,
            depth: 0,
            budget,
            new HashSet<PdfReference>());
    }

    private bool EvaluateMembership(
        PdfObject value,
        int depth,
        EvaluationBudget budget,
        HashSet<PdfReference> active)
    {
        CheckDepth(depth);
        budget.Count();
        if (_model.TryGetGroupInfo(
                value,
                out PdfOptionalContentModel.GroupInfo? group) &&
            group is not null)
        {
            return _overrides.TryGetValue(group.Id, out bool visible)
                ? visible
                : group.Visible;
        }

        PdfReference? reference = value as PdfReference;
        if (reference is not null && !active.Add(reference))
            return true;
        try
        {
            PdfDictionary? dictionary = value.AsDictionary(_model.Document);
            if (dictionary is null)
                return true;
            string? type = dictionary.GetValueOrNull("Type").AsName(_model.Document);
            if (type == "OCG")
                return true;
            if (type != "OCMD")
                return true;
            if (dictionary.GetValueOrNull("VE").AsArray(_model.Document) is { } expression)
            {
                return EvaluateExpression(
                    expression,
                    depth + 1,
                    budget,
                    active);
            }

            PdfObject? source = dictionary.GetValueOrNull("OCGs");
            IEnumerable<PdfObject> members =
                source.AsArray(_model.Document) is { } array
                    ? array
                    : source is null
                        ? Array.Empty<PdfObject>()
                        : new[] { source };
            bool[] states = members
                .Select(member => EvaluateMembership(
                    member,
                    depth + 1,
                    budget,
                    active))
                .ToArray();
            if (states.Length == 0)
                return true;
            return dictionary.GetValueOrNull("P").AsName(_model.Document) switch
            {
                "AllOn" => states.All(state => state),
                "AnyOff" => states.Any(state => !state),
                "AllOff" => states.All(state => !state),
                _ => states.Any(state => state)
            };
        }
        finally
        {
            if (reference is not null)
                active.Remove(reference);
        }
    }

    private bool EvaluateExpression(
        PdfArray expression,
        int depth,
        EvaluationBudget budget,
        HashSet<PdfReference> active)
    {
        CheckDepth(depth);
        budget.Count();
        if (expression.Count < 2 ||
            expression[0].AsName(_model.Document) is not { } operation)
        {
            return true;
        }

        return operation switch
        {
            "And" => expression
                .Skip(1)
                .All(value => EvaluateExpressionValue(
                    value,
                    depth + 1,
                    budget,
                    active)),
            "Or" => expression
                .Skip(1)
                .Any(value => EvaluateExpressionValue(
                    value,
                    depth + 1,
                    budget,
                    active)),
            "Not" when expression.Count == 2 =>
                !EvaluateExpressionValue(
                    expression[1],
                    depth + 1,
                    budget,
                    active),
            _ => true
        };
    }

    private bool EvaluateExpressionValue(
        PdfObject value,
        int depth,
        EvaluationBudget budget,
        HashSet<PdfReference> active) =>
        value.AsArray(_model.Document) is { } expression
            ? EvaluateExpression(expression, depth, budget, active)
            : EvaluateMembership(value, depth, budget, active);

    private void CheckDepth(int depth)
    {
        if (depth > _model.Document.Options.MaximumOptionalContentDepth)
        {
            throw new PdfLimitException(
                "Optional-content nesting exceeds the configured limit.");
        }
    }

    private sealed class EvaluationBudget
    {
        private readonly int _maximum;
        private int _count;

        public EvaluationBudget(int maximum) => _maximum = maximum;

        public void Count()
        {
            _count++;
            if (_count > _maximum)
            {
                throw new PdfLimitException(
                    "Optional-content expression exceeds the configured limit.");
            }
        }
    }

    private static readonly IReadOnlyDictionary<string, bool> EmptyOverrides =
        new ReadOnlyDictionary<string, bool>(
            new Dictionary<string, bool>(StringComparer.Ordinal));
}
