using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetVendorAttendance;

public class GetVendorAttendanceQueryValidator : AbstractValidator<GetVendorAttendanceQuery>
{
    public GetVendorAttendanceQueryValidator()
    {
        // The market weekday is per-LGU (Cantilan = Friday); attendance is fetched for whatever date is
        // requested, so no fixed-weekday rule is enforced here.
    }
}
