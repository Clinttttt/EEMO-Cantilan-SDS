using EEMOCantilanSDS.Domain.Common;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser
{
    public class GetCurrentUserQuery : IRequest<Result<AdminUserDto>>;
 
}
