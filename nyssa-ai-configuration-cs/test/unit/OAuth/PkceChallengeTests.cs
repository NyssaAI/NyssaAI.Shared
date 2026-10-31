using NyssaAI.Configuration.OAuth.Domain;

namespace NyssaAI.Configuration.Tests.OAuth;

/// <summary>
/// The flow's cryptographic material. Every property asserted here is one a happy-path sign-in
/// cannot observe: a weakened verifier, a predictable state, or a state comparison that accepts the
/// wrong value all leave the browser flow working exactly as before.
/// </summary>
public sealed class PkceChallengeTests
{
    // RFC 7636 Appendix B. The verifier and its S256 challenge, so the derivation is checked against
    // the specification rather than against itself.
    private const string RfcVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
    private const string RfcChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public void FromVerifier_WithTheRfcVector_ProducesTheRfcChallenge()
    {
        var pkce = PkceChallenge.FromVerifier(RfcVerifier, "state");

        Assert.Equal(RfcChallenge, pkce.Challenge);
        Assert.Equal(RfcVerifier, pkce.Verifier);
    }

    [Fact]
    public void Create_ProducesAVerifierOfTheLengthThatCarriesFullEntropy()
    {
        var pkce = PkceChallenge.Create();

        // 32 bytes base64url-encode to 43 characters: RFC 7636's minimum, and the length that
        // carries all 256 bits. A shorter verifier still completes a sign-in, so nothing else
        // would notice it shrinking.
        Assert.Equal(43, pkce.Verifier.Length);
        Assert.Equal(43, pkce.Challenge.Length);
    }

    [Theory]
    [InlineData("verifier")]
    [InlineData("challenge")]
    [InlineData("state")]
    public void Create_ProducesUrlSafeUnpaddedBase64(string part)
    {
        var pkce = PkceChallenge.Create();
        var value = part switch
        {
            "verifier" => pkce.Verifier,
            "challenge" => pkce.Challenge,
            _ => pkce.State,
        };

        Assert.DoesNotContain('+', value);
        Assert.DoesNotContain('/', value);
        Assert.DoesNotContain('=', value);
        Assert.All(value, c => Assert.True(
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_',
            $"'{c}' is not an unreserved character"));
    }

    [Fact]
    public void Create_ProducesDistinctMaterialEveryTime()
    {
        // Not a proof of randomness, but it fails immediately if the source is ever swapped for a
        // seeded or constant one, which is the realistic regression.
        var verifiers = new HashSet<string>(StringComparer.Ordinal);
        var states = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < 128; i++)
        {
            var pkce = PkceChallenge.Create();
            Assert.True(verifiers.Add(pkce.Verifier), "a verifier repeated");
            Assert.True(states.Add(pkce.State), "a state repeated");
        }
    }

    [Fact]
    public void Create_DerivesTheChallengeFromItsOwnVerifier()
    {
        var pkce = PkceChallenge.Create();

        Assert.Equal(PkceChallenge.FromVerifier(pkce.Verifier, pkce.State).Challenge, pkce.Challenge);
    }

    [Fact]
    public void MatchesState_WithTheFlowsOwnState_Accepts()
    {
        var pkce = PkceChallenge.Create();

        Assert.True(pkce.MatchesState(pkce.State));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    // Same length as a real state, so a comparison that only checks length still fails.
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]
    public void MatchesState_WithAnythingElse_Refuses(string? candidate)
    {
        var pkce = PkceChallenge.Create();

        // This is the CSRF check. Removing it, or letting null through, breaks nothing a
        // successful sign-in would reveal.
        Assert.False(pkce.MatchesState(candidate));
    }

    [Fact]
    public void MatchesState_WithAPrefixOfTheRealState_Refuses()
    {
        var pkce = PkceChallenge.Create();

        Assert.False(pkce.MatchesState(pkce.State[..^1]));
        Assert.False(pkce.MatchesState(pkce.State + "x"));
    }

    [Fact]
    public void MatchesState_IsBoundToOneFlow()
    {
        var first = PkceChallenge.Create();
        var second = PkceChallenge.Create();

        // A callback for one flow must not satisfy another, which is the whole point of binding
        // the state to the request.
        Assert.False(first.MatchesState(second.State));
        Assert.False(second.MatchesState(first.State));
    }
}
