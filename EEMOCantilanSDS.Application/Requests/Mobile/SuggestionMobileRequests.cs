using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record HideMobileSuggestionRequest(SuggestionType Type, string Value);
