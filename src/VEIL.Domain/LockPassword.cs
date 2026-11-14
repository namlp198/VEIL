using System.Security.Cryptography;

namespace VEIL.Domain;

/// <summary>
/// The password that ends the full lock from the lock icon on the Focus screen. It is stored only as a salted PBKDF2 hash
/// in the protected configuration, never in clear. Until the Super Admin sets one in Settings the default <see cref="Default"/>
/// applies, so a fresh install can always be unlocked; changing it is strongly advised.
/// </summary>
public static class LockPassword
{
    public const string Default = "admin";
    public const int MinLength = 4, MaxLength = 128;
    private const int Iterations = 200_000, SaltBytes = 16, HashBytes = 32;
    private const string Scheme = "pbkdf2-sha256";

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Null when acceptable, otherwise what is wrong.</summary>
    public static string? Validate(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinLength ? $"The password needs at least {MinLength} characters."
        : password.Length > MaxLength ? $"The password is limited to {MaxLength} characters."
        : null;

    /// <summary>Checks a typed password against the stored hash, or against the default when none is stored. Never throws.</summary>
    public static bool Verify(string? attempt, string? stored)
    {
        if (attempt is null || attempt.Length > MaxLength) return false;
        try
        {
            if (string.IsNullOrEmpty(stored))
            {
                var salt0 = new byte[SaltBytes];
                return CryptographicOperations.FixedTimeEquals(
                    Rfc2898DeriveBytes.Pbkdf2(attempt, salt0, 1000, HashAlgorithmName.SHA256, HashBytes),
                    Rfc2898DeriveBytes.Pbkdf2(Default, salt0, 1000, HashAlgorithmName.SHA256, HashBytes));
            }
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations is < 1000 or > 5_000_000) return false;
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(attempt, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException) { return false; }
    }
}
