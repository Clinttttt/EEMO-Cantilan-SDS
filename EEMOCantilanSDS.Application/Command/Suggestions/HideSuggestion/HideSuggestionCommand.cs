using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Suggestions.HideSuggestion;

/// <summary>Hides a pick-list value so it is no longer suggested to collectors. Idempotent.</summary>
public record HideSuggestionCommand(SuggestionType Type, string Value) : IRequest<Result<bool>>;
