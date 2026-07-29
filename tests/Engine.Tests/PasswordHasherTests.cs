using Sandbox;

namespace Engine.Tests;

/// <summary>Unit tests for the critical/security-sensitive function in the codebase: password hashing
/// and verification (see `Documents/02-Research.md` §2.7 and `Documents/08-Testing.md`). These are the
/// two unit tests required by the assessment's Tests deliverable; the manual functional/UAT test cases
/// live in `Documents/08-Testing.md` and `Documents/07-Client.md` instead, since they need a running
/// window/GPU device that a unit test should not depend on.</summary>
public class PasswordHasherTests
{
    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        (byte[] salt, byte[] hash, int iterations) = PasswordHasher.Hash("correct-horse-battery-staple");

        bool result = PasswordHasher.Verify("correct-horse-battery-staple", salt, hash, iterations);

        Assert.True(result);
    }

    [Fact]
    public void Verify_WithWrongPassword_ReturnsFalse()
    {
        (byte[] salt, byte[] hash, int iterations) = PasswordHasher.Hash("correct-horse-battery-staple");

        bool result = PasswordHasher.Verify("wrong-password", salt, hash, iterations);

        Assert.False(result);
    }

    [Fact]
    public void Hash_CalledTwiceWithSamePassword_ProducesDifferentSaltsAndHashes()
    {
        // A per-account random salt is what stops two users with the same password from ending up with
        // identical stored hashes (which would otherwise leak that fact to anyone reading accounts.json).
        (byte[] salt1, byte[] hash1, _) = PasswordHasher.Hash("same-password");
        (byte[] salt2, byte[] hash2, _) = PasswordHasher.Hash("same-password");

        Assert.False(salt1.AsSpan().SequenceEqual(salt2));
        Assert.False(hash1.AsSpan().SequenceEqual(hash2));
    }

    [Fact]
    public void Hash_UsesDocumentedIterationCount()
    {
        // Pinned so a future edit can't silently weaken the OWASP-recommended work factor cited in
        // `Documents/02-Research.md` §2.7 without a test failing to flag it.
        (_, _, int iterations) = PasswordHasher.Hash("any-password");

        Assert.Equal(PasswordHasher.DefaultIterations, iterations);
        Assert.True(iterations >= 600_000);
    }

    [Fact]
    public void Verify_WithTamperedHash_ReturnsFalse()
    {
        (byte[] salt, byte[] hash, int iterations) = PasswordHasher.Hash("correct-horse-battery-staple");
        hash[0] ^= 0xFF; // simulate a corrupted/tampered stored hash

        bool result = PasswordHasher.Verify("correct-horse-battery-staple", salt, hash, iterations);

        Assert.False(result);
    }
}
