using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Monitoring.GetContractsMonitoring;

public sealed class GetContractsMonitoringQueryValidator : AbstractValidator<GetContractsMonitoringQuery>
{
    private static readonly string[] Operations = [.. Enum.GetNames<FacilityCode>(), "KM", "VF"];
    private static readonly string[] Sorts = ["expiry", "expiryDesc", "occupant", "operation", "space"];
    public GetContractsMonitoringQueryValidator()
    {
        RuleFor(x => x.Filters).NotNull();
        When(x => x.Filters is not null, () =>
        {
            RuleFor(x => x.Filters.Search).MaximumLength(200);
            RuleFor(x => x.Filters.Operation).Must(v => Blank(v) || Operations.Contains(v!.Trim(), StringComparer.OrdinalIgnoreCase)).WithMessage("Unknown rental operation.");
            RuleFor(x => x.Filters.Status).Must(v => Blank(v) || Enum.GetNames<Dtos.Monitoring.ContractMonitoringStatus>().Contains(v!.Trim(), StringComparer.OrdinalIgnoreCase)).WithMessage("Unknown contract status.");
            RuleFor(x => x.Filters.OccupancyBasis).Must(v => Blank(v) || new[] { "Unknown", "SignedContract", "SpaceOnly", "Extension" }.Contains(v!.Trim(), StringComparer.OrdinalIgnoreCase)).WithMessage("Unknown occupancy basis.");
            RuleFor(x => x.Filters.FacilityId).Must(v => v is null || v != Guid.Empty);
            RuleFor(x => x.Filters.ExpiryWindow).Must(v => v is null or 7 or 30).WithMessage("Expiry window must be 7 or 30 days.");
            RuleFor(x => x.Filters.Page).InclusiveBetween(1, 10000);
            RuleFor(x => x.Filters.PageSize).InclusiveBetween(1, 100);
            RuleFor(x => x.Filters.Sort).Must(v => Blank(v) || Sorts.Contains(v!.Trim(), StringComparer.OrdinalIgnoreCase)).WithMessage("Unknown contract sort.");
        });
    }
    private static bool Blank(string? v) => string.IsNullOrWhiteSpace(v);
}
