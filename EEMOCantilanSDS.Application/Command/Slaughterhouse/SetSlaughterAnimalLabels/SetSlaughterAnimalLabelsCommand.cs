using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.SetSlaughterAnimalLabels;

public sealed record SetSlaughterAnimalLabelsCommand(string Hog, string Carabao, string Cow) : IRequest<Result<bool>>;
