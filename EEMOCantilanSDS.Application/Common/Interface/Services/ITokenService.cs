using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Common.Interface.Services
{
    public interface ITokenService
    {
        Task<TokenResponseDto> CreateTokenResponse(BaseUser user);
        Task<BaseUser> ValidateRefreshToken(string RefreshToken, CancellationToken cancellationToken = default);
    }
}
