using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile
{
    /// <summary>
    /// Lets an LGU Head update their own municipality's branding (office/report-header label, address, seal)
    /// after go-live. Scoped to the caller's municipality via their token; only non-empty fields overwrite,
    /// so a partial update never blanks existing branding.
    /// </summary>
    public record UpdateOfficeProfileCommand(string OfficeName, string? Address, string? SealPath, string? OfficeAcronym = null)
        : IRequest<Result<bool>>;
}
