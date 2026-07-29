using System.Collections.ObjectModel;

namespace Poppler;

/// <summary>Initial state used by the default optional-content configuration.</summary>
public enum PdfOptionalContentBaseState
{
    On,
    Off,
    Unchanged
}

/// <summary>Immutable metadata and default visibility for one PDF layer.</summary>
public sealed class PdfOptionalContentGroup
{
    internal PdfOptionalContentGroup(
        string id,
        string name,
        IEnumerable<string> intents,
        bool isVisible,
        bool isLocked,
        bool? viewState)
    {
        Id = id;
        Name = name;
        Intents = new ReadOnlyCollection<string>(intents.ToArray());
        IsVisible = isVisible;
        IsLocked = isLocked;
        ViewState = viewState;
    }

    /// <summary>Stable document-local identifier suitable for render overrides.</summary>
    public string Id { get; }

    /// <summary>Human-readable layer name from the OCG dictionary.</summary>
    public string Name { get; }

    /// <summary>Intents declared by the layer, normally <c>View</c>.</summary>
    public IReadOnlyList<string> Intents { get; }

    /// <summary>Visibility after applying the document's default configuration.</summary>
    public bool IsVisible { get; }

    /// <summary>Whether the default configuration marks the layer as locked.</summary>
    public bool IsLocked { get; }

    /// <summary>Optional <c>/Usage /View /ViewState</c> preference.</summary>
    public bool? ViewState { get; }
}

/// <summary>Immutable summary of the document's default layer configuration.</summary>
public sealed class PdfOptionalContentConfiguration
{
    internal PdfOptionalContentConfiguration(
        string name,
        string creator,
        PdfOptionalContentBaseState baseState,
        IEnumerable<string> intents,
        IEnumerable<IEnumerable<string>> radioButtonGroups)
    {
        Name = name;
        Creator = creator;
        BaseState = baseState;
        Intents = new ReadOnlyCollection<string>(intents.ToArray());
        RadioButtonGroups = new ReadOnlyCollection<IReadOnlyList<string>>(
            radioButtonGroups
                .Select(group =>
                    (IReadOnlyList<string>)new ReadOnlyCollection<string>(
                        group.ToArray()))
                .ToArray());
    }

    public string Name { get; }
    public string Creator { get; }
    public PdfOptionalContentBaseState BaseState { get; }
    public IReadOnlyList<string> Intents { get; }

    /// <summary>Mutually exclusive group sets expressed as group identifiers.</summary>
    public IReadOnlyList<IReadOnlyList<string>> RadioButtonGroups { get; }
}
