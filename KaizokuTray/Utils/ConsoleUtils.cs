using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace KaizokuTray.Utils;

/// <summary>
/// Utility class for console window management in a GUI application.
/// </summary>
public static class ConsoleUtils
{
    // --- Windows API Declarations ---

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll")]
    public static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

    [DllImport("user32.dll")]
    public static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadIcon(IntPtr hInstance, string lpIconName);

    [DllImport("user32.dll")]
    public static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    // --- Constants for ShowWindow ---
    
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    // --- Constants for System Menu ---

    public const uint SC_CLOSE = 0xF060;
    public const uint MF_BYCOMMAND = 0x00000000;
    public const uint MF_GRAYED = 0x00000001;
    public const uint MF_DISABLED = 0x00000002;

    // --- Constants for Window Messages ---

    public const uint WM_SETICON = 0x0080;
    public const uint ICON_SMALL = 0;
    public const uint ICON_BIG = 1;

    // --- Constants for LoadImage ---

    public const uint IMAGE_ICON = 1;
    public const uint LR_DEFAULTSIZE = 0x00000040;
    public const uint LR_LOADFROMFILE = 0x00000010;

    /// <summary>
    /// Disables the close button on the console window.
    /// </summary>
    /// <returns>True if the close button was disabled successfully, false otherwise.</returns>
    public static bool DisableConsoleCloseButton()
    {
        try
        {
            IntPtr consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
            {
                SafeWriteLine("Console window not found.");
                return false;
            }

            IntPtr systemMenu = GetSystemMenu(consoleWindow, false);
            if (systemMenu == IntPtr.Zero)
            {
                SafeWriteLine("Could not get system menu.");
                return false;
            }

            // Remove the close menu item entirely
            bool result = DeleteMenu(systemMenu, SC_CLOSE, MF_BYCOMMAND);
            if (result)
            {
                SafeWriteLine("Console close button disabled successfully.");
            }
            else
            {
                SafeWriteLine("Failed to disable console close button.");
            }

            return result;
        }
        catch (Exception ex)
        {
            SafeWriteLine($"Failed to disable console close button: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sets the console window icon using the kaizoku.ico from embedded resources.
    /// </summary>
    /// <returns>True if the icon was set successfully, false otherwise.</returns>
    public static bool SetConsoleIcon()
    {
        try
        {
            IntPtr consoleWindow = GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
            {
                SafeWriteLine("Console window not found.");
                return false;
            }

            // Load the icon from the embedded resource
            IntPtr iconHandle = LoadIconFromResource();
            if (iconHandle == IntPtr.Zero)
            {
                SafeWriteLine("Failed to load icon from resource.");
                return false;
            }

            // Set both small and large icons
            IntPtr result1 = SendMessage(consoleWindow, WM_SETICON, new IntPtr(ICON_SMALL), iconHandle);
            IntPtr result2 = SendMessage(consoleWindow, WM_SETICON, new IntPtr(ICON_BIG), iconHandle);

            SafeWriteLine("Console icon set successfully.");
            return true;
        }
        catch (Exception ex)
        {
            SafeWriteLine($"Failed to set console icon: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Loads the kaizoku.ico icon from the embedded resource.
    /// </summary>
    /// <returns>Handle to the loaded icon, or IntPtr.Zero if loading failed.</returns>
    private static IntPtr LoadIconFromResource()
    {
        try
        {
            // Get the current assembly
            Assembly assembly = Assembly.GetExecutingAssembly();
            
            // Get all resource names for debugging
            string[] resourceNames = assembly.GetManifestResourceNames();
            SafeWriteLine($"Available embedded resources: {string.Join(", ", resourceNames)}");
            
            // Try different possible resource names
            string[] possibleNames = {
                "KaizokuTray.Assets.kaizoku.net.ico",
                "Assets.kaizoku.net.ico",
                "kaizoku.net.ico"
            };
            
            Stream? stream = null;
            string? usedResourceName = null;
            
            foreach (string resourceName in possibleNames)
            {
                stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    usedResourceName = resourceName;
                    SafeWriteLine($"Found embedded resource: {resourceName}");
                    break;
                }
            }
            
            if (stream == null)
            {
                SafeWriteLine("Could not find any embedded resource for kaizoku.net.ico");
                SafeWriteLine($"Tried: {string.Join(", ", possibleNames)}");
                return IntPtr.Zero;
            }
            
            using (stream)
            {
                // Create a temporary file to extract the icon
                string tempIconPath = Path.GetTempFileName();
                tempIconPath = Path.ChangeExtension(tempIconPath, ".ico");
                
                using (var fileStream = File.Create(tempIconPath))
                {
                    stream.CopyTo(fileStream);
                }

                SafeWriteLine($"Extracted icon to temporary file: {tempIconPath}");

                // Load the icon from the temporary file
                IntPtr iconHandle = LoadImage(IntPtr.Zero, tempIconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);

                // Clean up the temporary file
                try
                {
                    File.Delete(tempIconPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }

                if (iconHandle != IntPtr.Zero)
                {
                    SafeWriteLine($"Successfully loaded icon from resource: {usedResourceName}");
                }
                else
                {
                    SafeWriteLine("LoadImage failed to load the icon");
                }

                return iconHandle;
            }
        }
        catch (Exception ex)
        {
            SafeWriteLine($"Failed to load icon from resource: {ex.Message}");
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Safely writes a line to the console without throwing exceptions.
    /// </summary>
    public static void SafeWriteLine(string message)
    {
        try
        {
            // This check is important because the console might not be allocated
            if (GetConsoleWindow() != IntPtr.Zero)
            {
                Console.WriteLine(message);
            }
        }
        catch
        {
            // Ignore console write failures
        }
    }
}