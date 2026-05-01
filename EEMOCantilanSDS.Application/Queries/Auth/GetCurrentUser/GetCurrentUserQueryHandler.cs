using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser
{
    public class GetCurrentUserQueryHandler(ICurrentUserService current) : IRequestHandler<GetCurrentUserQuery, Result<AdminUserDto>>
    {
        public Task<Result<AdminUserDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
        {
            var user = current.GetCurrentUser();
            return Task.FromResult(user is not null
                ? Result<AdminUserDto>.Success(user)
                : Result<AdminUserDto>.Unauthorized());
        }
    }
}
