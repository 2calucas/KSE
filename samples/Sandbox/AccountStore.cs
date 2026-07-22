using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>One local account record as persisted to disk. Only ever holds a salt + hash, never a password.</summary>
internal sealed class AccountRecord
{
    public string Username { get; set; } = "";
    public string SaltBase64 { get; set; } = "";
    public string HashBase64 { get; set; } = "";
    public int Iterations { get; set; }
    public DateTime CreatedUtc { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<AccountRecord>))]
internal sealed partial class AccountRecordJsonContext : JsonSerializerContext
{
}

/// <summary>Local, file-backed account store — no server, no network call. This protects the credential file
/// itself from casual reading/reuse (salted+hashed, never plaintext); it does not protect against someone with
/// full access to the machine's own user profile, which is a different threat model than this project has.</summary>
internal sealed class AccountStore
{
    private const int MinUsernameLength = 3;
    private const int MinPasswordLength = 8;

    private readonly string _filePath;
    private readonly List<AccountRecord> _accounts;

    public AccountStore()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KSE");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "accounts.json");

        _accounts = File.Exists(_filePath)
            ? JsonSerializer.Deserialize(File.ReadAllText(_filePath), AccountRecordJsonContext.Default.ListAccountRecord) ?? []
            : [];
    }

    public bool TryCreateAccount(string username, string password, out string error)
    {
        username = username.Trim();
        if (username.Length < MinUsernameLength)
        {
            error = $"Username must be at least {MinUsernameLength} characters.";
            return false;
        }
        if (password.Length < MinPasswordLength)
        {
            error = $"Password must be at least {MinPasswordLength} characters.";
            return false;
        }
        if (_accounts.Any(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase)))
        {
            error = "That username is already taken.";
            return false;
        }

        (byte[] salt, byte[] hash, int iterations) = PasswordHasher.Hash(password);
        _accounts.Add(new AccountRecord
        {
            Username = username,
            SaltBase64 = Convert.ToBase64String(salt),
            HashBase64 = Convert.ToBase64String(hash),
            Iterations = iterations,
            CreatedUtc = DateTime.UtcNow,
        });
        Save();

        error = "";
        return true;
    }

    /// <summary>Deliberately returns the same generic error for "no such user" and "wrong password" so a
    /// failed attempt can't be used to enumerate which usernames already have an account.</summary>
    public bool TryLogin(string username, string password, out string error)
    {
        username = username.Trim();
        AccountRecord? account = _accounts.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

        bool valid = account is not null && PasswordHasher.Verify(
            password,
            Convert.FromBase64String(account.SaltBase64),
            Convert.FromBase64String(account.HashBase64),
            account.Iterations);

        if (!valid)
        {
            error = "Invalid username or password.";
            return false;
        }

        error = "";
        return true;
    }

    private void Save()
    {
        string json = JsonSerializer.Serialize(_accounts, AccountRecordJsonContext.Default.ListAccountRecord);
        File.WriteAllText(_filePath, json);
    }
}
