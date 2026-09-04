/* =============================================================================
 * File        : PasswordHasher.cs
 * Project     : Smart Solar Microgrid Trading System - Central Web API
 * Module      : Security
 * Description : Salted PBKDF2-SHA256 password hashing. A fresh 16 byte salt is
 *               generated per account, 100,000 iterations are applied, and the
 *               verification compares in fixed time so the comparison itself
 *               leaks nothing about the stored hash.
 * Author      : Smart Solar Microgrid Team - SE4040 Enterprise Application Dev
 * Created     : 2026-09-04
 * References  : PBKDF2 usage follows the pattern documented by Microsoft at
 *               https://learn.microsoft.com/aspnet/core/security/data-protection/consumer-apis/password-hashing
 * =============================================================================
 */

using System.Security.Cryptography;

namespace SmartSolar.Api.Security;

/// <summary>Creates and verifies password hashes.</summary>
public interface IPasswordHasher
{
    /// <summary>Produces a new salt and the hash of the supplied password.</summary>
    (string Hash, string Salt) Hash(string password);

    /// <summary>Confirms a candidate password against a stored hash and salt.</summary>
    bool Verify(string password, string hash, string salt);
}

/// <summary>PBKDF2-SHA256 implementation of <see cref="IPasswordHasher"/>.</summary>
public class PasswordHasher : IPasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    /// <summary>Generates a cryptographically random salt and derives the hash from it.</summary>
    public (string Hash, string Salt) Hash(string password)
    {
        // A per account salt means two users with the same password never share a hash.
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>Re-derives the hash from the candidate password and compares it in fixed time.</summary>
    public bool Verify(string password, string hash, string salt)
    {
        // Malformed stored material must fail closed rather than throw during a login.
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrWhiteSpace(salt))
        {
            return false;
        }

        try
        {
            var saltBytes = Convert.FromBase64String(salt);
            var expected = Convert.FromBase64String(hash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, expected.Length);

            // FixedTimeEquals prevents a timing side channel on the comparison.
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
