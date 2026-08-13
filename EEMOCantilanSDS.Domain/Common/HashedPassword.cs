namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// A password that has already been hashed.
///
/// <para>
/// Exists to make a dangerous mistake impossible to compile. The user factories and password-change methods used to take a
/// plaintext <c>string</c> and hash it themselves; moving the hashing out left them taking a <c>string</c> that must ALREADY
/// be hashed — and nothing distinguishes the two. A caller passing plaintext would store it verbatim as the account's hash,
/// the account could never sign in again, and neither the compiler nor a passing build would say a word.
/// </para>
///
/// <para>
/// With this type the mistake is a compile error at every call site, and the only way to obtain one is to ask the hasher.
/// Domain still holds no opinion on HOW a password is hashed: this carries the result, it does not produce it.
/// </para>
/// </summary>
public readonly record struct HashedPassword
{
    /// <param name="value">The hash produced by the application's password hasher.</param>
    /// <exception cref="ArgumentException">When empty — an empty hash would accept nothing and reads as a bug, not a state.</exception>
    public HashedPassword(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A hashed password cannot be empty.", nameof(value));

        Value = value;
    }

    /// <summary>The stored hash.</summary>
    public string Value { get; }

    public override string ToString() => Value;
}
