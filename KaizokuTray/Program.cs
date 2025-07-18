using Avalonia;
using KaizokuBackend.Utils;
using KaizokuTray.Utils;
using System;
using System.Runtime.InteropServices;

namespace KaizokuTray;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!EnvironmentSetup.IsApplicationAlreadyRunning())
        {
            try
            {
                // On Windows, set up the console before doing anything else.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    InitializeConsole();
                }
                
                // Build and run the Avalonia application.
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception ex)
            {
                // Log any critical startup errors.
                Console.WriteLine($"Application startup failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Allocates a console, sets its icon, disables the close button, and hides it.
    /// </summary>
    private static void InitializeConsole()
    {
        // Allocate a new console window for the application.
        if (ConsoleUtils.AllocConsole())
        {
            Console.WriteLine("Console allocated successfully.");

            // Set the console icon
            if (ConsoleUtils.SetConsoleIcon())
            {
                Console.WriteLine("Console icon set successfully.");
            }
            else
            {
                Console.WriteLine("Warning: Failed to set console icon.");
            }

            // Disable the close button on the console window.
            if (ConsoleUtils.DisableConsoleCloseButton())
            {
                Console.WriteLine("Console close button disabled successfully. The console cannot be closed by the user.");
            }
            else
            {
                Console.WriteLine("Warning: Failed to disable the console close button. The console may be closeable by the user.");
            }

            // Hide the console window initially. It can be shown later by the application logic.
            IntPtr consoleWindow = ConsoleUtils.GetConsoleWindow();
            if (consoleWindow != IntPtr.Zero)
            {
                ConsoleUtils.ShowWindow(consoleWindow, ConsoleUtils.SW_HIDE);
                Console.WriteLine("Console window is now hidden.");
            }
        }
        else
        {
            Console.WriteLine("Could not allocate a new console.");
        }
    }

    /// <summary>
    /// Configures and builds the Avalonia application.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
