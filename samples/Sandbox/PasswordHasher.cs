using System.Security.Cryptography;

namespace Sandbox;

/// <summary>Salted PBKDF2-HMAC-SHA256 password hashing (NIST SP 800-132 / OWASP-recommended iteration count
/// for this algorithm). Passwords are never stored, logged, or compared as plaintext — only a per-account
/// random salt and its derived hash ever touch disk (see <see cref="AccountStore"/>).</summary>
internal static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    /// <summary>OWASP-recommended minimum for PBKDF2-HMAC-SHA256 as of this writing. Deliberately expensive —
    /// this is what makes brute-forcing a stolen hash slow, not the algorithm choice alone.</summary>
    public const int DefaultIterations = 600_000;

    public static (byte[] Salt, byte[] Hash, int Iterations) Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, DefaultIterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return (salt, hash, DefaultIterations);
    }

    /// <summary>Constant-time comparison (<see cref="CryptographicOperations.FixedTimeEquals"/>) so a failed
    /// login can't be timed to learn how many leading bytes of the hash matched.</summary>
    public static bool Verify(string password, byte[] salt, byte[] expectedHash, int iterations)
    {
        byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
