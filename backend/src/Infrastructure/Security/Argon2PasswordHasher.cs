using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ComicPlatform.Application.Abstractions;
using ComicPlatform.Infrastructure.Configuration;
using Konscious.Security.Cryptography;

namespace ComicPlatform.Infrastructure.Security;

/// <summary>
/// Argon2id password hashing stored as a PHC string:
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>.
/// Hashing is memory/CPU heavy, so a semaphore bounds concurrent hashes to protect the 2-OCPU host.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher, IDisposable
{
    private const int SaltSize = 16;
    private const int HashSize = 32;

    private readonly PasswordHashSettings _settings;
    private readonly SemaphoreSlim _gate;
    private readonly Lazy<string> _dummyHash;

    public Argon2PasswordHasher(PasswordHashSettings settings)
    {
        _settings = settings;
        _gate = new SemaphoreSlim(Math.Max(1, settings.MaxConcurrentHashes));
        _dummyHash = new Lazy<string>(() => Encode(RandomNumberGenerator.GetBytes(SaltSize), new byte[HashSize], _settings.MemoryKiB, _settings.Iterations, _settings.Parallelism));
    }

    public async Task<string> HashAsync(string password, CancellationToken ct = default)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = await ComputeAsync(password, salt, _settings.MemoryKiB, _settings.Iterations, _settings.Parallelism, ct);
        return Encode(salt, hash, _settings.MemoryKiB, _settings.Iterations, _settings.Parallelism);
    }

    public async Task<PasswordCheck> VerifyAsync(string encoded, string password, CancellationToken ct = default)
    {
        if (!TryDecode(encoded, out var p))
        {
            return PasswordCheck.Failed;
        }

        var actual = await ComputeAsync(password, p.Salt, p.MemoryKiB, p.Iterations, p.Parallelism, ct);
        if (!CryptographicOperations.FixedTimeEquals(actual, p.Hash))
        {
            return PasswordCheck.Failed;
        }

        var weaker = p.MemoryKiB < _settings.MemoryKiB || p.Iterations < _settings.Iterations || p.Parallelism != _settings.Parallelism;
        return weaker ? PasswordCheck.SuccessRehashNeeded : PasswordCheck.Success;
    }

    public async Task DummyVerifyAsync(string password, CancellationToken ct = default) =>
        await VerifyAsync(_dummyHash.Value, password, ct);

    public void Dispose() => _gate.Dispose();

    private async Task<byte[]> ComputeAsync(string password, byte[] salt, int memoryKiB, int iterations, int parallelism, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                MemorySize = memoryKiB,
                Iterations = iterations,
                DegreeOfParallelism = parallelism,
            };

            return await argon.GetBytesAsync(HashSize);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Encode(byte[] salt, byte[] hash, int m, int t, int p) =>
        string.Create(CultureInfo.InvariantCulture, $"$argon2id$v=19$m={m},t={t},p={p}${B64(salt)}${B64(hash)}");

    private static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=');

    private static byte[] FromB64(string s)
    {
        var padded = s.PadRight(s.Length + ((4 - (s.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private sealed record Parsed(int MemoryKiB, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);

    private static bool TryDecode(string encoded, out Parsed parsed)
    {
        parsed = null!;
        try
        {
            // ["", "argon2id", "v=19", "m=..,t=..,p=..", salt, hash]
            var parts = encoded.Split('$');
            if (parts.Length != 6 || parts[1] != "argon2id" || parts[2] != "v=19")
            {
                return false;
            }

            int m = 0, t = 0, p = 0;
            foreach (var kv in parts[3].Split(','))
            {
                var pair = kv.Split('=');
                if (pair.Length != 2)
                {
                    return false;
                }

                var value = int.Parse(pair[1], CultureInfo.InvariantCulture);
                switch (pair[0])
                {
                    case "m": m = value; break;
                    case "t": t = value; break;
                    case "p": p = value; break;
                    default: return false;
                }
            }

            // Guard against absurd parameters coming from a tampered hash column.
            if (m is < 8 or > 1_048_576 || t is < 1 or > 20 || p is < 1 or > 16)
            {
                return false;
            }

            parsed = new Parsed(m, t, p, FromB64(parts[4]), FromB64(parts[5]));
            return parsed.Salt.Length >= 8 && parsed.Hash.Length == HashSize;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
