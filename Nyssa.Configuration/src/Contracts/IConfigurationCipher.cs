using NyssaAI.Results;

namespace NyssaAI.Configuration.Contracts;

/// <summary>
/// IConfigurationCipher seals and opens a configuration value, binding the ciphertext to the namespace
/// and key it was stored under so a sealed value cannot be moved to another address.
/// </summary>
internal interface IConfigurationCipher
{
    /// <summary>Seals a value against the given address.</summary>
    /// <param name="value">The plaintext to seal.</param>
    /// <param name="namespace">The namespace the value is stored under.</param>
    /// <param name="key">The key the value is stored under.</param>
    Result<string> Seal(string value, string @namespace, string key);

    /// <summary>Opens a sealed value, failing when it was sealed against a different address.</summary>
    /// <param name="value">The sealed value to open.</param>
    /// <param name="namespace">The namespace the value is stored under.</param>
    /// <param name="key">The key the value is stored under.</param>
    Result<string> Open(string value, string @namespace, string key);
}
