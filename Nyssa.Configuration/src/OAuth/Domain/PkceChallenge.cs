using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace NyssaAI.Configuration.OAuth.Domain;

/// <summary>
/// PkceChallenge is the cryptographic material of one authorization flow: the PKCE verifier and
/// its S256 challenge (RFC 7636), and the <c>state</c> value that binds the callback to the
/// request. It touches no socket, clock, or HTTP, so it is testable on its own.
/// </summary>
internal sealed class PkceChallenge
{
    private PkceChallenge(string verifier, string challenge, string state)
    {
        Verifier = verifier;
        Challenge = challenge;
        State = state;
    }

    /// <summary>Gets the code verifier, sent with the code at the token exchange.</summary>
    public string Verifier { get; }

    /// <summary>Gets the S256 code challenge, sent at authorization.</summary>
    public string Challenge { get; }

    /// <summary>Gets the opaque state value, sent at authorization and echoed by the callback.</summary>
    public string State { get; }

    /// <summary>Generates a fresh verifier, challenge, and state from the cryptographic random source.</summary>
    public static PkceChallenge Create()
    {
        // 32 random bytes encode to 43 characters, the minimum RFC 7636 allows and the length
        // that carries the full 256 bits of entropy.
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        return FromVerifier(verifier, state);
    }

    /// <summary>Derives the challenge for a given verifier; used to check against known-answer vectors.</summary>
    /// <param name="verifier">The code verifier.</param>
    /// <param name="state">The state value.</param>
    internal static PkceChallenge FromVerifier(string verifier, string state) =>
        new(verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), state);

    /// <summary>
    /// Compares a callback's state with this flow's in fixed time, so a mismatch takes as long to
    /// refuse as a match takes to accept. Null and empty are refused without throwing.
    /// </summary>
    /// <param name="candidate">The state the callback carried.</param>
    public bool MatchesState(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(State),
            Encoding.UTF8.GetBytes(candidate));
    }
}
