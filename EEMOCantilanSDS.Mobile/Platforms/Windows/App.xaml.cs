using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace EEMOCantilanSDS.Mobile.WinUI
{
    public partial class App : MauiWinUIApplication
    {
        public App()
        {
            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);

            var window = Application.Windows[0].Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (window is null) return;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // Scale 360×800 logical dp to physical pixels so the window renders
            // at true phone size regardless of the Windows display scale setting.
            uint dpi = GetDpiForWindow(hwnd);
            double scale = dpi / 96.0; // 96 DPI = 100% scale

            appWindow.Resize(new SizeInt32((int)Math.Round(360 * scale), (int)Math.Round(800 * scale)));
            appWindow.Title = "EEMO Mobile [Windows Debug]";
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}
