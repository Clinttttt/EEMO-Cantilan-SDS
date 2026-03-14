using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Api
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApi(this IServiceCollection service, IConfiguration iconfiguration)
        {
            service.AddControllers();
            service.AddSwaggerGen();
            service.AddCors(options =>
            {
                options.AddPolicy("AllowAll", builder =>
                {
                    builder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
                });
            });
            service.AddDbContext<AppDbContext>(options =>
            {
                options.UseNpgsql(iconfiguration.GetConnectionString(""));
            });



            return service;
        }
    }
}
