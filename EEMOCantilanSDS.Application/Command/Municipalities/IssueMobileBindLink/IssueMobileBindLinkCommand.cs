using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Municipalities.IssueMobileBindLink;

/// <summary>Returns the caller LGU's collector-app bind token, generating one if absent (or rotating it when
/// <paramref name="Rotate"/> is true). Returns the token; the API composes the shareable link.</summary>
public record IssueMobileBindLinkCommand(bool Rotate = false) : IRequest<Result<string>>;
