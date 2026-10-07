using EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Services;

// Test-only session surface: the real service depends on native MAUI secure storage.
// This assembly compiles the actual page/control Razor without starting a device or signing in.
public sealed class MobileSessionService
{
    public MobileMenuDto Menu { get; } = new(Guid.NewGuid(), "Scratch collector", "test", new(2026, 10, 7), []);
    public string BrandingSeal => "images/LGU_CANTILAN_LOGO.jpg";
    public Task InitializeAsync() => Task.CompletedTask;
}
