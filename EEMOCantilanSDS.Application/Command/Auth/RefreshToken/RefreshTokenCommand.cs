using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Command.Auth.GenerateRefreshToken
{
    public class RefreshTokenCommand : IRequest<Result<TokenResponseDto>>
    {
        public string RefreshToken { get; set; } = string.Empty;
    }
  
}
