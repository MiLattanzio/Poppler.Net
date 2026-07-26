using System.Security.Cryptography;
using System.Text;
using Poppler.Core;

namespace Poppler.Security;

internal enum PdfCryptMethod
{
    Identity,
    Rc4,
    Aes128,
    Aes256
}

internal static class PdfCryptography
{
    private static readonly byte[] PasswordPadding =
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80,
        0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A
    };

    public static byte[] EncodePassword(string password, bool modern)
    {
        ArgumentNullException.ThrowIfNull(password);
        byte[] encoded = password.All(character => character <= byte.MaxValue)
            ? Encoding.Latin1.GetBytes(password)
            : Encoding.UTF8.GetBytes(password);
        int maximum = modern ? 127 : 32;
        return encoded.Length <= maximum ? encoded : encoded[..maximum];
    }

    public static byte[] PadLegacyPassword(ReadOnlySpan<byte> password)
    {
        var result = new byte[32];
        int copied = Math.Min(password.Length, result.Length);
        password[..copied].CopyTo(result);
        PasswordPadding.AsSpan(0, result.Length - copied).CopyTo(result.AsSpan(copied));
        return result;
    }

    public static ReadOnlySpan<byte> LegacyPasswordPadding => PasswordPadding;

    public static byte[] Rc4(ReadOnlySpan<byte> key, ReadOnlySpan<byte> input)
    {
        if (key.IsEmpty)
            throw new PdfFormatException("An RC4 encryption key cannot be empty.");

        Span<byte> state = stackalloc byte[256];
        for (int index = 0; index < state.Length; index++)
            state[index] = (byte)index;

        int second = 0;
        for (int index = 0; index < state.Length; index++)
        {
            second = (second + state[index] + key[index % key.Length]) & 0xFF;
            (state[index], state[second]) = (state[second], state[index]);
        }

        var output = new byte[input.Length];
        int x = 0;
        int y = 0;
        for (int index = 0; index < input.Length; index++)
        {
            x = (x + 1) & 0xFF;
            y = (y + state[x]) & 0xFF;
            (state[x], state[y]) = (state[y], state[x]);
            output[index] = (byte)(input[index] ^ state[(state[x] + state[y]) & 0xFF]);
        }

        CryptographicOperations.ZeroMemory(state);
        return output;
    }

    public static byte[] Md5(ReadOnlySpan<byte> input) => MD5.HashData(input);
    public static byte[] Sha256(ReadOnlySpan<byte> input) => SHA256.HashData(input);

    public static byte[] Revision6Hash(
        ReadOnlySpan<byte> password,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> userData)
    {
        byte[] initial = Concat(password, salt, userData);
        byte[] key = SHA256.HashData(initial);
        CryptographicOperations.ZeroMemory(initial);

        int round = 0;
        while (true)
        {
            round++;
            byte[] block = Concat(password, key, userData);
            var repeated = new byte[checked(block.Length * 64)];
            for (int repetition = 0; repetition < 64; repetition++)
                block.CopyTo(repeated, repetition * block.Length);

            byte[] encrypted = AesTransform(
                key.AsSpan(0, 16),
                key.AsSpan(16, 16),
                repeated,
                CipherMode.CBC,
                PaddingMode.None,
                encrypt: true);
            int selector = 0;
            for (int index = 0; index < 16; index++)
                selector += encrypted[index];

            int va = selector % 3;
            byte[] next = va switch
            {
                0 => SHA256.HashData(encrypted),
                1 => SHA384.HashData(encrypted),
                _ => SHA512.HashData(encrypted)
            };
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(block);
            CryptographicOperations.ZeroMemory(repeated);
            key = next;

            bool complete = round >= 64 && encrypted[^1] <= round - 32;
            CryptographicOperations.ZeroMemory(encrypted);
            if (complete)
                break;
        }

        byte[] result = key[..32];
        CryptographicOperations.ZeroMemory(key);
        return result;
    }

    public static byte[] DecryptObjectData(
        PdfCryptMethod method,
        ReadOnlySpan<byte> fileKey,
        PdfReference reference,
        ReadOnlySpan<byte> input)
    {
        if (method == PdfCryptMethod.Identity)
            return input.ToArray();
        if (method == PdfCryptMethod.Aes256)
            return DecryptPdfAesPayload(fileKey, input);

        byte[] objectKey = DeriveObjectKey(
            fileKey,
            reference,
            useAesSalt: method == PdfCryptMethod.Aes128);
        try
        {
            return method switch
            {
                PdfCryptMethod.Rc4 => Rc4(objectKey, input),
                PdfCryptMethod.Aes128 => DecryptPdfAesPayload(objectKey, input),
                _ => throw new PdfUnsupportedFeatureException($"crypt method {method}")
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(objectKey);
        }
    }

    public static byte[] DecryptAes256Key(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> input) =>
        AesTransform(
            key,
            new byte[16],
            input,
            CipherMode.CBC,
            PaddingMode.None,
            encrypt: false);

    public static byte[] DecryptAes256Permissions(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> input) =>
        AesTransform(
            key,
            ReadOnlySpan<byte>.Empty,
            input,
            CipherMode.ECB,
            PaddingMode.None,
            encrypt: false);

    public static bool FixedEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

    public static byte[] Concat(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var result = new byte[checked(first.Length + second.Length)];
        first.CopyTo(result);
        second.CopyTo(result.AsSpan(first.Length));
        return result;
    }

    public static byte[] Concat(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        ReadOnlySpan<byte> third)
    {
        var result = new byte[checked(first.Length + second.Length + third.Length)];
        first.CopyTo(result);
        second.CopyTo(result.AsSpan(first.Length));
        third.CopyTo(result.AsSpan(first.Length + second.Length));
        return result;
    }

    public static byte[] Concat(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second,
        ReadOnlySpan<byte> third,
        ReadOnlySpan<byte> fourth,
        ReadOnlySpan<byte> fifth)
    {
        int length = checked(
            first.Length + second.Length + third.Length + fourth.Length + fifth.Length);
        var result = new byte[length];
        int offset = 0;
        first.CopyTo(result.AsSpan(offset));
        offset += first.Length;
        second.CopyTo(result.AsSpan(offset));
        offset += second.Length;
        third.CopyTo(result.AsSpan(offset));
        offset += third.Length;
        fourth.CopyTo(result.AsSpan(offset));
        offset += fourth.Length;
        fifth.CopyTo(result.AsSpan(offset));
        return result;
    }

    private static byte[] DeriveObjectKey(
        ReadOnlySpan<byte> fileKey,
        PdfReference reference,
        bool useAesSalt)
    {
        int extra = useAesSalt ? 9 : 5;
        var material = new byte[fileKey.Length + extra];
        fileKey.CopyTo(material);
        int offset = fileKey.Length;
        material[offset] = (byte)reference.ObjectNumber;
        material[offset + 1] = (byte)(reference.ObjectNumber >> 8);
        material[offset + 2] = (byte)(reference.ObjectNumber >> 16);
        material[offset + 3] = (byte)reference.Generation;
        material[offset + 4] = (byte)(reference.Generation >> 8);
        if (useAesSalt)
        {
            material[offset + 5] = 0x73;
            material[offset + 6] = 0x41;
            material[offset + 7] = 0x6C;
            material[offset + 8] = 0x54;
        }

        byte[] digest = MD5.HashData(material);
        CryptographicOperations.ZeroMemory(material);
        byte[] result = digest[..Math.Min(fileKey.Length + 5, 16)];
        CryptographicOperations.ZeroMemory(digest);
        return result;
    }

    private static byte[] DecryptPdfAesPayload(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> input)
    {
        if (input.Length < 32 || (input.Length - 16) % 16 != 0)
            throw new PdfFormatException("An encrypted AES object has an invalid length.");

        return AesTransform(
            key,
            input[..16],
            input[16..],
            CipherMode.CBC,
            PaddingMode.PKCS7,
            encrypt: false);
    }

    private static byte[] AesTransform(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> initializationVector,
        ReadOnlySpan<byte> input,
        CipherMode mode,
        PaddingMode padding,
        bool encrypt)
    {
        try
        {
            using Aes aes = Aes.Create();
            aes.Key = key.ToArray();
            aes.Mode = mode;
            aes.Padding = padding;
            if (mode != CipherMode.ECB)
                aes.IV = initializationVector.ToArray();
            using ICryptoTransform transform = encrypt
                ? aes.CreateEncryptor()
                : aes.CreateDecryptor();
            return transform.TransformFinalBlock(input.ToArray(), 0, input.Length);
        }
        catch (CryptographicException exception)
        {
            throw new PdfFormatException("Invalid AES-encrypted PDF data.", exception);
        }
    }
}
