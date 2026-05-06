using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;

public class MarkVendorPaidCommandValidator : AbstractValidator<MarkVendorPaidCommand>
{
    public MarkVendorPaidCommandValidator()
    {
        RuleFor(x => x.AttendanceId).NotEmpty();
        RuleFor(x => x.CollectorId).NotEmpty();
    }
}
