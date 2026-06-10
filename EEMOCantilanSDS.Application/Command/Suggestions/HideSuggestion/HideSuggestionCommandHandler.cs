using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Suggestions;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Suggestions.HideSuggestion;

public class HideSuggestionCommandHandler(
    ISuggestionRepository suggestionRepository,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<HideSuggestionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(HideSuggestionCommand request, CancellationToken ct)
    {
        var value = request.Value.Trim();

        // Idempotent: if it's already hidden, succeed without adding a duplicate.
        var hidden = await suggestionRepository.GetHiddenValuesAsync(request.Type, ct);
        if (hidden.Contains(value))
            return Result<bool>.Success(true);

        var entity = HiddenSuggestion.Create(request.Type, value, currentUser.Username ?? "Admin");
        await suggestionRepository.AddAsync(entity, ct);
        await uow.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
