using System.Linq;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Audit.GetAuditTrail;

public class GetAuditTrailQueryValidator : AbstractValidator<GetAuditTrailQuery>
{
    private static readonly string[] AllowedActions = { "Created", "Updated", "Deleted" };

    public GetAuditTrailQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be 1 or greater.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.Action)
            .Must(a => string.IsNullOrWhiteSpace(a) || AllowedActions.Contains(a))
            .WithMessage("Action must be one of: Created, Updated, Deleted.");

        RuleFor(x => x)
            .Must(x => !x.FromUtc.HasValue || !x.ToUtc.HasValue || x.FromUtc.Value <= x.ToUtc.Value)
            .WithMessage("FromUtc must be on or before ToUtc.");
    }
}
