using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTransporterProfile;

public class GetTransporterProfileQueryValidator : AbstractValidator<GetTransporterProfileQuery>
{
    public GetTransporterProfileQueryValidator()
    {
        RuleFor(x => x.TransporterId).NotEmpty();
    }
}
