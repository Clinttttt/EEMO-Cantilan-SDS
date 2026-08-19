using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Municipalities.TestPaymentConnection;

/// <summary>
/// Asks PayMongo whether this LGU's account credentials work, and records the answer.
/// </summary>
/// <param name="SecretKey">
/// The key to test. Optional: when omitted, the LGU's already-stored key is tested instead.
///
/// <para>
/// Both cases are needed. Before saving, the office wants to know that what it just pasted is right - testing the stored
/// key would test the previous one. After saving, there is nothing in the box to test, and the question becomes whether the
/// connection still works, which is not the same question.
/// </para>
/// </param>
public record TestPaymentConnectionCommand(string? SecretKey = null) : IRequest<Result<PaymentConnectionTestDto>>;

/// <summary>What the office is told about the attempt.</summary>
/// <param name="Ok">PayMongo accepted the key.</param>
/// <param name="Message">Said in the office's terms, whether it worked or not.</param>
/// <param name="Mode">"Live" or "Test", read from the key that was actually tested.</param>
/// <param name="VerifiedAtUtc">When it was confirmed. Null when it was not.</param>
public record PaymentConnectionTestDto(bool Ok, string Message, string? Mode, DateTime? VerifiedAtUtc);
