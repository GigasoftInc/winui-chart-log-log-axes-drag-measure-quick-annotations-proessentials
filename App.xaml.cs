// =====================================================================================================
// DEPLOYING A WINUI APP (important -- WinUI deploys differently than WPF / WinForms)
//
// 1) A .NET app is NOT a single exe. This exe is only a launcher; your code lives in <app>.dll, and it
//    needs <app>.deps.json + <app>.runtimeconfig.json beside it. Ship the ENTIRE build/publish output
//    folder, never a hand-picked subset of files.
//
// 2) Unlike WPF / WinForms, a WinUI app depends on the Windows App SDK, so the target machine needs TWO
//    runtimes (plus the Visual C++ Redistributable): the .NET 10 Desktop Runtime AND the Windows App SDK
//    Runtime. Two ways to satisfy this:
//      a) Framework-dependent: install those two runtimes once on the machine (they are shared by every
//         app) and ship the small app folder. Both have silent installers a setup can chain.
//      b) Self-contained: bundle both runtimes INTO the app folder so nothing needs installing --
//           dotnet publish -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true
//         (use win-arm64 for ARM64). Larger and architecture-specific, but runs on a clean machine.
//
// 3) WATCH OUT: this app will run from an under-filled folder on YOUR development machine, because the
//    runtimes are already installed here. ALWAYS validate on a CLEAN machine (no .NET, no Windows App SDK)
//    to see what your customers actually experience.
//
// WindowsPackageType=None (see the .csproj) auto-initializes the Windows App SDK bootstrapper, which
// prompts the user to install the runtime when it is missing (the full app folder must be present).
// =====================================================================================================

using Microsoft.UI.Xaml;

namespace LogLogDragMeasureWinUI
{
    public partial class App : Application
    {
        private Window _window;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
