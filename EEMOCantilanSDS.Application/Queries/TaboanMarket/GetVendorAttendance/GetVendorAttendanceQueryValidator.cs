using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetVendorAttendance;

public class GetVendorAttendanceQueryValidator : AbstractValidator<GetVendorAttendanceQuery>
{
    public GetVendorAttendanceQueryValidator()
    {
        RuleFor(x => x.MarketDate)
            .Must(date => date.DayOfWeek == DayOfWeek.Friday)
            .WithMessage("Market date must be a Friday.");
    }
}
