using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Mail;
public static class Credentials
{
    public static string User(string? value) => value is not null && Regex.IsMatch(value, "^[A-Za-z0-9_]{1,16}$") ? value.ToUpperInvariant() : throw new CredentialException("Username must contain 1–16 letters, digits or underscores.");
    public static string Email(string? value)
    {
        var s = value?.Trim().ToLowerInvariant() ?? "";
        if (s.Length > 254 || !MailAddress.TryCreate(s, out var m) || m.Address != s || !s.Contains('@')) throw new CredentialException("Enter a valid email address.");
        return s;
    }
    public static string Password(string? value) => value is not null && value.Length is >= 8 and <= 16 && value.All(c => c is >= '!' and <= '~') ? value.ToUpperInvariant() : throw new CredentialException("Password must contain 8–16 printable ASCII characters without spaces.");
    public static (string Salt, string Verifier) Verifier(string username, string password, byte[]? salt = null)
    {
        salt ??= RandomNumberGenerator.GetBytes(32);
        if (salt.Length != 32) throw new CredentialException("Salt must be 32 bytes.");
        // CMaNGOS BigNumber serializes little-endian; hex SQL fields are big-endian integers.
        salt[31] |= 0x80;
        var identity = SHA1.HashData(Encoding.ASCII.GetBytes(User(username) + ":" + Password(password)));
        var exponent = new BigInteger(SHA1.HashData(salt.Concat(identity).ToArray()), true, false);
        var modulus = new BigInteger(Convert.FromHexString("894B645E89E1535BBDAD5B8B290650530801B18EBFBF5E8FAB3C82872A3E9BB7"), true, true);
        return (Convert.ToHexString(salt.Reverse().ToArray()), Convert.ToHexString(BigInteger.ModPow(7, exponent, modulus).ToByteArray(true, true)));
    }
    public static byte[] CodeHash(string secret, string purpose, string user, string email, string code) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(string.Join('\n', purpose, user, email, code)));
}

public sealed class CredentialException(string message) : Exception(message);

