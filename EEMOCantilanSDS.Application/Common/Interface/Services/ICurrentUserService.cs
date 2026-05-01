using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Common.Interface.Services
{
    public interface ICurrentUserService
    {
        bool IsAuthenticated { get; }
        AdminUserDto? GetCurrentUser();
   
    }
}
