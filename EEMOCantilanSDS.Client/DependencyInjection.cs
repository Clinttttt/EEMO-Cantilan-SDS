namespace EEMOCantilanSDS.Client
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddClient(this IServiceCollection service)
        {
            service.AddRazorComponents()
                .AddInteractiveServerComponents();

            AddPersistince(service);
            AddCustomAuthentication(service);



            return service;
        }
        public static IServiceCollection AddPersistince(this IServiceCollection service)
        {

            return service;
        }
        public static IServiceCollection AddCustomAuthentication(this IServiceCollection service)
        {
            return service;
        }

    }
}
