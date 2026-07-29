namespace Poppler;

public sealed class EmbeddedFile
{
    private readonly Lazy<byte[]> _data;

    internal EmbeddedFile(
        string name,
        string description,
        string mimeType,
        int? declaredSize,
        DateTimeOffset? creationDate,
        DateTimeOffset? modificationDate,
        byte[] checksum,
        Func<byte[]> dataFactory)
    {
        Name = name;
        Description = description;
        MimeType = mimeType;
        DeclaredSize = declaredSize;
        CreationDate = creationDate;
        ModificationDate = modificationDate;
        Checksum = checksum;
        _data = new Lazy<byte[]>(dataFactory);
    }

    public bool IsValid => !string.IsNullOrEmpty(Name);
    public string Name { get; }
    public string UnicodeName => Name;
    public string Description { get; }
    public string MimeType { get; }
    public int? DeclaredSize { get; }
    public int Size => Data.Length;
    public DateTimeOffset? CreationDate { get; }
    public DateTimeOffset? ModificationDate { get; }
    public ReadOnlyMemory<byte> Checksum { get; }
    public ReadOnlyMemory<byte> Data => _data.Value;

    public void SaveTo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Data.ToArray());
    }
}
