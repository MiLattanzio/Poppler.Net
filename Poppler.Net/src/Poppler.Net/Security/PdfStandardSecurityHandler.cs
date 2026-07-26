using System.Buffers.Binary;
using System.Security.Cryptography;
using Poppler.Core;

namespace Poppler.Security;

internal sealed class PdfStandardSecurityHandler : IDisposable
{
    private readonly PdfDocumentCore _document;
    private readonly PdfDictionary _dictionary;
    private readonly byte[] _ownerValue;
    private readonly byte[] _userValue;
    private readonly byte[] _ownerEncrypted;
    private readonly byte[] _userEncrypted;
    private readonly byte[] _permissionsEncrypted;
    private readonly byte[] _fileIdentifier;
    private readonly Dictionary<string, PdfCryptMethod> _cryptFilters =
        new(StringComparer.Ordinal);
    private byte[]? _fileKey;

    public PdfStandardSecurityHandler(PdfDocumentCore document, PdfDictionary dictionary)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));

        string filter = dictionary.GetValueOrNull("Filter").AsName(document) ?? "Standard";
        if (filter != "Standard")
            throw new PdfUnsupportedFeatureException($"security handler {filter}");

        Version = RequireInteger("V");
        Revision = RequireInteger("R");
        PermissionsValue = ReadPermissionsValue();
        EncryptMetadata =
            dictionary.GetValueOrNull("EncryptMetadata")?.Resolve(document) is not PdfBoolean
            {
                Value: false
            };

        KeyLengthBytes = ReadKeyLength();
        ValidateVersionAndRevision();
        _ownerValue = RequireString("O", Revision <= 4 ? 32 : 48);
        _userValue = RequireString("U", Revision <= 4 ? 32 : 48);
        _ownerEncrypted = Revision >= 5 ? RequireString("OE", 32) : Array.Empty<byte>();
        _userEncrypted = Revision >= 5 ? RequireString("UE", 32) : Array.Empty<byte>();
        _permissionsEncrypted = Revision >= 5
            ? RequireString("Perms", 16)
            : Array.Empty<byte>();
        _fileIdentifier = ReadFileIdentifier();

        (StringMethod, StreamMethod, EmbeddedFileMethod) = ReadCryptMethods();
        EncryptionInfo = new PdfEncryptionInfo(
            Version,
            Revision,
            KeyLengthBytes * 8,
            ToPublic(StringMethod),
            ToPublic(StreamMethod),
            ToPublic(EmbeddedFileMethod),
            EncryptMetadata);
    }

    public int Version { get; }
    public int Revision { get; }
    public int KeyLengthBytes { get; }
    public int PermissionsValue { get; }
    public bool EncryptMetadata { get; }
    public bool IsLocked => _fileKey is null;
    public PdfPasswordKind PasswordKind { get; private set; }
    public PdfEncryptionInfo EncryptionInfo { get; }
    public PdfCryptMethod StringMethod { get; }
    public PdfCryptMethod StreamMethod { get; }
    public PdfCryptMethod EmbeddedFileMethod { get; }

    public Permission Permissions
    {
        get
        {
            if (IsLocked)
                return Permission.None;
            if (PasswordKind == PdfPasswordKind.Owner)
                return Permission.All;

            Permission result = Permission.None;
            if (HasPermissionBit(3))
                result |= Permission.Print;
            if (HasPermissionBit(4))
                result |= Permission.Modify;
            if (HasPermissionBit(5))
                result |= Permission.Copy;
            if (HasPermissionBit(6))
                result |= Permission.AddNotes;
            if (HasPermissionBit(9))
                result |= Permission.FillForms;
            if (HasPermissionBit(10))
                result |= Permission.Accessibility;
            if (HasPermissionBit(11))
                result |= Permission.Assemble;
            if (Revision == 2 ? HasPermissionBit(3) : HasPermissionBit(3) && HasPermissionBit(12))
                result |= Permission.HighResolutionPrint;
            return result;
        }
    }

    public bool Authenticate(string ownerPassword, string userPassword)
    {
        ArgumentNullException.ThrowIfNull(ownerPassword);
        ArgumentNullException.ThrowIfNull(userPassword);
        ClearKey();

        return Revision <= 4
            ? AuthenticateLegacy(ownerPassword, userPassword)
            : AuthenticateModern(ownerPassword, userPassword);
    }

    public PdfObject DecryptObject(PdfObject value, PdfReference reference)
    {
        if (_fileKey is null)
            throw new PdfEncryptedException();

        return value switch
        {
            PdfString text => new PdfString(
                PdfCryptography.DecryptObjectData(
                    StringMethod,
                    _fileKey,
                    reference,
                    text.Bytes.Span)),
            PdfArray array => new PdfArray(
                array.Select(item => DecryptObject(item, reference))),
            PdfDictionary dictionary => DecryptDictionary(dictionary, reference),
            PdfStream stream => DecryptStreamObject(stream, reference),
            _ => value
        };
    }

    public byte[] DecryptExplicitStream(
        PdfStream stream,
        ReadOnlySpan<byte> input,
        string cryptFilterName)
    {
        if (_fileKey is null)
            throw new PdfEncryptedException();
        if (cryptFilterName == "Identity")
            return input.ToArray();
        PdfReference reference = stream.SourceReference ??
            throw new PdfFormatException(
                "An explicitly encrypted stream has no object reference.");
        if (!_cryptFilters.TryGetValue(cryptFilterName, out PdfCryptMethod method))
            throw new PdfFormatException($"Unknown crypt filter /{cryptFilterName}.");
        if (!EncryptMetadata &&
            stream.Dictionary.GetValueOrNull("Type").AsName(_document) == "Metadata")
        {
            return input.ToArray();
        }

        return PdfCryptography.DecryptObjectData(
            method,
            _fileKey,
            reference,
            input);
    }

    public void Dispose()
    {
        ClearKey();
        CryptographicOperations.ZeroMemory(_ownerEncrypted);
        CryptographicOperations.ZeroMemory(_userEncrypted);
    }

    private bool AuthenticateLegacy(string ownerPassword, string userPassword)
    {
        byte[] ownerBytes = PdfCryptography.EncodePassword(ownerPassword, modern: false);
        byte[] recoveredUser = RecoverUserPassword(ownerBytes);
        if (TryLegacyUserPassword(recoveredUser, alreadyPadded: true, out byte[] ownerFileKey))
        {
            SetKey(ownerFileKey, PdfPasswordKind.Owner);
            CryptographicOperations.ZeroMemory(ownerBytes);
            CryptographicOperations.ZeroMemory(recoveredUser);
            return true;
        }

        byte[] userBytes = PdfCryptography.EncodePassword(userPassword, modern: false);
        bool success = TryLegacyUserPassword(userBytes, alreadyPadded: false, out byte[] userFileKey);
        if (success)
            SetKey(userFileKey, PdfPasswordKind.User);
        else
            CryptographicOperations.ZeroMemory(userFileKey);
        CryptographicOperations.ZeroMemory(ownerBytes);
        CryptographicOperations.ZeroMemory(recoveredUser);
        CryptographicOperations.ZeroMemory(userBytes);
        return success;
    }

    private bool AuthenticateModern(string ownerPassword, string userPassword)
    {
        byte[] ownerBytes = PdfCryptography.EncodePassword(ownerPassword, modern: true);
        if (TryModernPassword(ownerBytes, owner: true, out byte[] ownerFileKey))
        {
            SetKey(ownerFileKey, PdfPasswordKind.Owner);
            CryptographicOperations.ZeroMemory(ownerBytes);
            ValidateModernPermissions();
            return true;
        }

        byte[] userBytes = PdfCryptography.EncodePassword(userPassword, modern: true);
        bool success = TryModernPassword(userBytes, owner: false, out byte[] userFileKey);
        if (success)
        {
            SetKey(userFileKey, PdfPasswordKind.User);
            ValidateModernPermissions();
        }
        else
        {
            CryptographicOperations.ZeroMemory(userFileKey);
        }

        CryptographicOperations.ZeroMemory(ownerBytes);
        CryptographicOperations.ZeroMemory(userBytes);
        return success;
    }

    private byte[] RecoverUserPassword(ReadOnlySpan<byte> ownerPassword)
    {
        byte[] paddedOwner = PdfCryptography.PadLegacyPassword(ownerPassword);
        byte[] digest = PdfCryptography.Md5(paddedOwner);
        if (Revision >= 3)
        {
            for (int iteration = 0; iteration < 50; iteration++)
            {
                byte[] next = PdfCryptography.Md5(digest);
                CryptographicOperations.ZeroMemory(digest);
                digest = next;
            }
        }

        byte[] result = _ownerValue[..32];
        if (Revision == 2)
        {
            result = PdfCryptography.Rc4(digest.AsSpan(0, KeyLengthBytes), result);
        }
        else
        {
            for (int iteration = 19; iteration >= 0; iteration--)
            {
                byte[] iterationKey = XorKey(digest.AsSpan(0, KeyLengthBytes), iteration);
                byte[] next = PdfCryptography.Rc4(iterationKey, result);
                CryptographicOperations.ZeroMemory(iterationKey);
                CryptographicOperations.ZeroMemory(result);
                result = next;
            }
        }

        CryptographicOperations.ZeroMemory(paddedOwner);
        CryptographicOperations.ZeroMemory(digest);
        return result;
    }

    private bool TryLegacyUserPassword(
        ReadOnlySpan<byte> password,
        bool alreadyPadded,
        out byte[] fileKey)
    {
        byte[] padded = alreadyPadded
            ? password[..Math.Min(password.Length, 32)].ToArray()
            : PdfCryptography.PadLegacyPassword(password);
        if (padded.Length < 32)
            Array.Resize(ref padded, 32);
        fileKey = ComputeLegacyFileKey(padded);
        byte[] expected = ComputeLegacyUserValue(fileKey);
        int comparisonLength = Revision == 2 ? 32 : 16;
        bool result = PdfCryptography.FixedEquals(
            expected.AsSpan(0, comparisonLength),
            _userValue.AsSpan(0, comparisonLength));
        CryptographicOperations.ZeroMemory(padded);
        CryptographicOperations.ZeroMemory(expected);
        if (!result)
        {
            CryptographicOperations.ZeroMemory(fileKey);
            fileKey = Array.Empty<byte>();
        }

        return result;
    }

    private byte[] ComputeLegacyFileKey(ReadOnlySpan<byte> paddedPassword)
    {
        var permissions = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(permissions, PermissionsValue);
        byte[] metadataMarker = Revision >= 4 && !EncryptMetadata
            ? new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }
            : Array.Empty<byte>();
        byte[] material = PdfCryptography.Concat(
            paddedPassword,
            _ownerValue.AsSpan(0, 32),
            permissions,
            _fileIdentifier,
            metadataMarker);
        byte[] digest = PdfCryptography.Md5(material);
        if (Revision >= 3)
        {
            for (int iteration = 0; iteration < 50; iteration++)
            {
                byte[] next = PdfCryptography.Md5(digest.AsSpan(0, KeyLengthBytes));
                CryptographicOperations.ZeroMemory(digest);
                digest = next;
            }
        }

        byte[] result = digest[..KeyLengthBytes];
        CryptographicOperations.ZeroMemory(material);
        CryptographicOperations.ZeroMemory(digest);
        return result;
    }

    private byte[] ComputeLegacyUserValue(ReadOnlySpan<byte> fileKey)
    {
        if (Revision == 2)
            return PdfCryptography.Rc4(fileKey, PdfCryptography.LegacyPasswordPadding);

        byte[] material = PdfCryptography.Concat(
            PdfCryptography.LegacyPasswordPadding,
            _fileIdentifier);
        byte[] digest = PdfCryptography.Md5(material);
        byte[] result = PdfCryptography.Rc4(fileKey, digest);
        for (int iteration = 1; iteration <= 19; iteration++)
        {
            byte[] iterationKey = XorKey(fileKey, iteration);
            byte[] next = PdfCryptography.Rc4(iterationKey, result);
            CryptographicOperations.ZeroMemory(iterationKey);
            CryptographicOperations.ZeroMemory(result);
            result = next;
        }

        Array.Resize(ref result, 32);
        CryptographicOperations.ZeroMemory(material);
        CryptographicOperations.ZeroMemory(digest);
        return result;
    }

    private bool TryModernPassword(
        ReadOnlySpan<byte> password,
        bool owner,
        out byte[] fileKey)
    {
        byte[] entry = owner ? _ownerValue : _userValue;
        ReadOnlySpan<byte> userData = owner ? _userValue.AsSpan(0, 48) : ReadOnlySpan<byte>.Empty;
        byte[] validationHash = ComputeModernHash(password, entry.AsSpan(32, 8), userData);
        bool matches = PdfCryptography.FixedEquals(validationHash, entry.AsSpan(0, 32));
        CryptographicOperations.ZeroMemory(validationHash);
        if (!matches)
        {
            fileKey = Array.Empty<byte>();
            return false;
        }

        byte[] intermediateKey = ComputeModernHash(password, entry.AsSpan(40, 8), userData);
        fileKey = PdfCryptography.DecryptAes256Key(
            intermediateKey,
            owner ? _ownerEncrypted : _userEncrypted);
        CryptographicOperations.ZeroMemory(intermediateKey);
        return fileKey.Length == 32;
    }

    private byte[] ComputeModernHash(
        ReadOnlySpan<byte> password,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> userData)
    {
        if (Revision == 6)
            return PdfCryptography.Revision6Hash(password, salt, userData);

        byte[] material = PdfCryptography.Concat(password, salt, userData);
        byte[] result = PdfCryptography.Sha256(material);
        CryptographicOperations.ZeroMemory(material);
        return result;
    }

    private void ValidateModernPermissions()
    {
        if (_fileKey is null)
            return;
        byte[] decrypted = PdfCryptography.DecryptAes256Permissions(
            _fileKey,
            _permissionsEncrypted);
        bool markerValid =
            decrypted.Length == 16 &&
            decrypted[9] == (byte)'a' &&
            decrypted[10] == (byte)'d' &&
            decrypted[11] == (byte)'b';
        bool permissionsValid =
            markerValid &&
            BinaryPrimitives.ReadInt32LittleEndian(decrypted) == PermissionsValue;
        bool metadataValid =
            markerValid &&
            decrypted[8] == (EncryptMetadata ? (byte)'T' : (byte)'F');
        if (!permissionsValid || !metadataValid)
        {
            _document.AddDiagnostic(
                PdfDiagnosticSeverity.Warning,
                "security.perms",
                "The AES-256 /Perms validation block does not match the encryption dictionary.");
        }

        CryptographicOperations.ZeroMemory(decrypted);
    }

    private PdfDictionary DecryptDictionary(
        PdfDictionary dictionary,
        PdfReference reference) =>
        new(dictionary.Select(
            pair => new KeyValuePair<string, PdfObject>(
                pair.Key,
                DecryptObject(pair.Value, reference))));

    private PdfStream DecryptStreamObject(PdfStream stream, PdfReference reference)
    {
        PdfDictionary dictionary = DecryptDictionary(stream.Dictionary, reference);
        byte[] bytes = stream.EncodedBytes.ToArray();
        bool explicitCryptFilter = HasCryptFilter(dictionary);
        bool metadataExcluded =
            !EncryptMetadata &&
            dictionary.GetValueOrNull("Type").AsName(_document) == "Metadata";
        bool xrefStream = dictionary.GetValueOrNull("Type").AsName(_document) == "XRef";
        if (!explicitCryptFilter && !metadataExcluded && !xrefStream)
        {
            PdfCryptMethod method =
                dictionary.GetValueOrNull("Type").AsName(_document) == "EmbeddedFile"
                    ? EmbeddedFileMethod
                    : StreamMethod;
            bytes = PdfCryptography.DecryptObjectData(method, _fileKey!, reference, bytes);
        }

        return new PdfStream(dictionary, bytes, reference);
    }

    private bool HasCryptFilter(PdfDictionary dictionary)
    {
        PdfObject? filter = dictionary.GetValueOrNull("Filter");
        if (filter is null)
            return false;
        PdfObject resolved = filter.Resolve(_document);
        if (resolved is PdfName name)
            return name.Value == "Crypt";
        return resolved is PdfArray array &&
               array.Any(item => item.AsName(_document) == "Crypt");
    }

    private (PdfCryptMethod Strings, PdfCryptMethod Streams, PdfCryptMethod EmbeddedFiles)
        ReadCryptMethods()
    {
        if (Version <= 2)
            return (PdfCryptMethod.Rc4, PdfCryptMethod.Rc4, PdfCryptMethod.Rc4);

        PdfDictionary? filters = _dictionary.GetValueOrNull("CF").AsDictionary(_document);
        if (filters is not null)
        {
            foreach ((string name, PdfObject value) in filters)
            {
                PdfDictionary filter = value.AsDictionary(_document) ??
                    throw new PdfFormatException($"Crypt filter /{name} is not a dictionary.");
                string methodName = filter.GetValueOrNull("CFM").AsName(_document) ?? "None";
                _cryptFilters[name] = methodName switch
                {
                    "None" => PdfCryptMethod.Identity,
                    "V2" => PdfCryptMethod.Rc4,
                    "AESV2" => PdfCryptMethod.Aes128,
                    "AESV3" => PdfCryptMethod.Aes256,
                    _ => throw new PdfUnsupportedFeatureException($"crypt filter method {methodName}")
                };
            }
        }

        string stringName = _dictionary.GetValueOrNull("StrF").AsName(_document) ?? "Identity";
        string streamName = _dictionary.GetValueOrNull("StmF").AsName(_document) ?? "Identity";
        string embeddedName =
            _dictionary.GetValueOrNull("EFF").AsName(_document) ?? streamName;
        return (
            ResolveCryptFilter(stringName),
            ResolveCryptFilter(streamName),
            ResolveCryptFilter(embeddedName));
    }

    private PdfCryptMethod ResolveCryptFilter(string name)
    {
        if (name == "Identity")
            return PdfCryptMethod.Identity;
        if (!_cryptFilters.TryGetValue(name, out PdfCryptMethod method))
            throw new PdfFormatException($"Encryption dictionary references unknown crypt filter /{name}.");
        return method;
    }

    private int ReadKeyLength()
    {
        if (Revision == 2)
            return 5;
        if (Revision is 5 or 6)
            return 32;

        int bits = _dictionary.GetValueOrNull("Length").AsInteger(_document) ?? 40;
        if (bits is < 40 or > 128 || bits % 8 != 0)
            throw new PdfFormatException($"Invalid Standard Security Handler key length {bits}.");
        return bits / 8;
    }

    private void ValidateVersionAndRevision()
    {
        bool supported = (Version, Revision) switch
        {
            (1 or 2, 2) => true,
            (2, 3) => true,
            (4, 4) => true,
            (5, 5 or 6) => true,
            _ => false
        };
        if (!supported)
        {
            throw new PdfUnsupportedFeatureException(
                $"Standard Security Handler V={Version}, R={Revision}");
        }
    }

    private int RequireInteger(string key) =>
        _dictionary.GetValueOrNull(key).AsInteger(_document) ??
        throw new PdfFormatException($"Encryption dictionary has no integer /{key}.");

    private int ReadPermissionsValue()
    {
        if (_dictionary.GetValueOrNull("P")?.Resolve(_document) is not PdfNumber
            {
                IsInteger: true
            } value ||
            value.Value is < int.MinValue or > uint.MaxValue)
        {
            throw new PdfFormatException("Encryption dictionary has no valid integer /P.");
        }

        return value.Value <= int.MaxValue
            ? (int)value.Value
            : unchecked((int)(uint)value.Value);
    }

    private byte[] RequireString(string key, int minimumLength)
    {
        if (_dictionary.GetValueOrNull(key)?.Resolve(_document) is not PdfString value ||
            value.Bytes.Length < minimumLength)
        {
            throw new PdfFormatException(
                $"Encryption dictionary /{key} must contain at least {minimumLength} bytes.");
        }

        return value.Bytes.Span[..minimumLength].ToArray();
    }

    private byte[] ReadFileIdentifier()
    {
        PdfArray? identifiers = _document.Trailer.GetValueOrNull("ID").AsArray(_document);
        return identifiers is not null &&
               identifiers.Count > 0 &&
               identifiers[0].Resolve(_document) is PdfString identifier
            ? identifier.Bytes.ToArray()
            : Array.Empty<byte>();
    }

    private bool HasPermissionBit(int oneBasedBit) =>
        (unchecked((uint)PermissionsValue) & (1U << (oneBasedBit - 1))) != 0;

    private static byte[] XorKey(ReadOnlySpan<byte> key, int value)
    {
        var result = new byte[key.Length];
        for (int index = 0; index < result.Length; index++)
            result[index] = (byte)(key[index] ^ value);
        return result;
    }

    private void SetKey(byte[] key, PdfPasswordKind passwordKind)
    {
        ClearKey();
        _fileKey = key;
        PasswordKind = passwordKind;
    }

    private void ClearKey()
    {
        if (_fileKey is not null)
            CryptographicOperations.ZeroMemory(_fileKey);
        _fileKey = null;
        PasswordKind = PdfPasswordKind.None;
    }

    private static PdfEncryptionAlgorithm ToPublic(PdfCryptMethod method) => method switch
    {
        PdfCryptMethod.Identity => PdfEncryptionAlgorithm.Identity,
        PdfCryptMethod.Rc4 => PdfEncryptionAlgorithm.Rc4,
        PdfCryptMethod.Aes128 => PdfEncryptionAlgorithm.Aes128,
        PdfCryptMethod.Aes256 => PdfEncryptionAlgorithm.Aes256,
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };
}
