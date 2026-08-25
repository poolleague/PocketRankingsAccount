using System.Security.Cryptography;

namespace PocketRankingsAccount.Services;

// Matches PoolLeagueWeb/Services/PasswordService.cs exactly -- same
// algorithm, same parameters, same stored-hash format. There is no
// product-specific reason to hash passwords differently across the
// platform, so this is a near-verbatim copy rather than a reinvention.
public static class PasswordService
{
    // PBKDF2 settings. If you raise Iterations later, existing hashes still verify
    // because the iteration count is stored inside the hash string.
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    public const int MinimumPasswordLength = 12;
    public const int MaximumPasswordLength = 128;

    public static string Hash(string password)
    {
        // Each password gets a unique salt so equal passwords do not share a hash.
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        try
        {
            // Expected stored format: PBKDF2$iterations$salt$key
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var iterations)) return false;

            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            // FixedTimeEquals avoids leaking whether an early byte matched.
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    public static string? ValidateNewPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumPasswordLength)
            return $"New password must be at least {MinimumPasswordLength} characters.";
        if (password.Length > MaximumPasswordLength)
            return $"New password must be no more than {MaximumPasswordLength} characters.";
        return null;
    }
}
