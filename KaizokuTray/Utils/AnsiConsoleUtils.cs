using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Spectre.Console;

namespace KaizokuTray.Utils;

/// <summary>
/// Utility class for safe console operations in GUI applications with ANSI/Rich console support
/// </summary>
public static class AnsiConsoleUtils
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    // Windows API declarations
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeConsole();

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint mode);

    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    /// <summary>
    /// Sets the console window title
    /// </summary>
    /// <param name="title">Title to set</param>
    /// <returns>True if successful, false otherwise</returns>
    public static bool SetConsoleTitle(string title)
    {
        try
        {
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow != IntPtr.Zero)
            {
                return SetWindowText(consoleWindow, title);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Hides the console window
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    public static bool HideConsoleWindow()
    {
        try
        {
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow != IntPtr.Zero)
            {
                return ShowWindow(consoleWindow, SW_HIDE);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Restores/shows the console window
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    public static bool RestoreConsoleWindow()
    {
        try
        {
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow != IntPtr.Zero)
            {
                return ShowWindow(consoleWindow, SW_RESTORE);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    /// <summary>
    /// Checks if the console window is currently visible.
    /// </summary>
    /// <returns>True if the console window exists and is visible, false otherwise.</returns>
    public static bool IsConsoleWindowVisible()
    {
        try
        {
            var consoleWindow = GetConsoleWindow();
            if (consoleWindow != IntPtr.Zero)
            {
                return IsWindowVisible(consoleWindow);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
    /// <summary>
    /// Safely writes a line to console with rich formatting support
    /// </summary>
    /// <param name="message">Message to write</param>
    /// <param name="color">Optional color for the message</param>
    public static void SafeWriteLine(string message, Color? color = null)
    {
        try
        {
            if (color.HasValue)
            {
                AnsiConsole.MarkupLine($"[{color.Value.ToMarkup()}]{message.EscapeMarkup()}[/]");
            }
            else
            {
                AnsiConsole.WriteLine(message);
            }
        }
        catch
        {
            // Fallback to regular console if ANSI fails
            try
            {
                Console.WriteLine(message);
            }
            catch
            {
                // Ignore console write failures - they can happen in various console states
            }
        }
    }

    /// <summary>
    /// Safely writes to console with rich formatting support
    /// </summary>
    /// <param name="message">Message to write</param>
    /// <param name="color">Optional color for the message</param>
    public static void SafeWrite(string message, Color? color = null)
    {
        try
        {
            if (color.HasValue)
            {
                AnsiConsole.Markup($"[{color.Value.ToMarkup()}]{message.EscapeMarkup()}[/]");
            }
            else
            {
                AnsiConsole.Write(message);
            }
        }
        catch
        {
            // Fallback to regular console if ANSI fails
            try
            {
                Console.Write(message);
            }
            catch
            {
                // Ignore console write failures
            }
        }
    }

    /// <summary>
    /// Writes a styled header with ANSI formatting
    /// </summary>
    /// <param name="title">Header title</param>
    /// <param name="style">Optional style for the header</param>
    public static void WriteHeader(string title, Style? style = null)
    {
        try
        {
            var rule = new Rule(title);
            if (style != null)
            {
                rule.Style = style;
            }
            else
            {
                rule.Style = Style.Parse("bold cyan");
            }
            AnsiConsole.Write(rule);
        }
        catch
        {
            // Fallback to simple header
            SafeWriteLine($"=== {title} ===");
        }
    }

    /// <summary>
    /// Writes a success message with green color
    /// </summary>
    /// <param name="message">Success message</param>
    public static void WriteSuccess(string message)
    {
        SafeWriteLine($"? {message}", Color.Green);
    }

    /// <summary>
    /// Writes an error message with red color
    /// </summary>
    /// <param name="message">Error message</param>
    public static void WriteError(string message)
    {
        SafeWriteLine($"? {message}", Color.Red);
    }

    /// <summary>
    /// Writes a warning message with yellow color
    /// </summary>
    /// <param name="message">Warning message</param>
    public static void WriteWarning(string message)
    {
        SafeWriteLine($"??  {message}", Color.Yellow);
    }

    /// <summary>
    /// Writes an info message with blue color
    /// </summary>
    /// <param name="message">Info message</param>
    public static void WriteInfo(string message)
    {
        SafeWriteLine($"??  {message}", Color.Blue);
    }

    /// <summary>
    /// Creates and displays a simple table
    /// </summary>
    /// <param name="title">Table title</param>
    /// <param name="headers">Column headers</param>
    /// <param name="rows">Table rows</param>
    public static void WriteTable(string title, string[] headers, string[][] rows)
    {
        try
        {
            var table = new Table()
                .Title(title)
                .BorderColor(Color.Grey);

            // Add columns
            foreach (var header in headers)
            {
                table.AddColumn(header);
            }

            // Add rows
            foreach (var row in rows)
            {
                table.AddRow(row);
            }

            AnsiConsole.Write(table);
        }
        catch
        {
            // Fallback to simple table
            SafeWriteLine($"=== {title} ===");
            SafeWriteLine(string.Join(" | ", headers));
            SafeWriteLine(new string('-', headers.Sum(h => h.Length + 3)));
            foreach (var row in rows)
            {
                SafeWriteLine(string.Join(" | ", row));
            }
        }
    }

    /// <summary>
    /// Creates a progress bar for long-running operations
    /// </summary>
    /// <param name="description">Progress description</param>
    /// <returns>Progress context for updating progress</returns>
    public static ProgressContext? CreateProgress(string description)
    {
        try
        {
            return AnsiConsole.Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn())
                .Start(ctx =>
                {
                    var task = ctx.AddTask(description);
                    return ctx;
                });
        }
        catch
        {
            // Fallback - just write the description
            SafeWriteLine($"Starting: {description}");
            return null;
        }
    }

    /// <summary>
    /// Prompts user for input with ANSI styling
    /// </summary>
    /// <param name="prompt">Prompt message</param>
    /// <param name="defaultValue">Default value if user presses enter</param>
    /// <returns>User input or default value</returns>
    public static string SafePrompt(string prompt, string? defaultValue = null)
    {
        try
        {
            if (IsConsoleInputAvailable())
            {
                var promptWidget = new TextPrompt<string>(prompt);
                if (!string.IsNullOrEmpty(defaultValue))
                {
                    promptWidget.DefaultValue(defaultValue);
                }
                return promptWidget.Show(AnsiConsole.Console);
            }
            else
            {
                SafeWriteLine($"{prompt} (Input not available, using default: {defaultValue ?? "none"})");
                return defaultValue ?? string.Empty;
            }
        }
        catch
        {
            SafeWriteLine($"{prompt} (Input error, using default: {defaultValue ?? "none"})");
            return defaultValue ?? string.Empty;
        }
    }

    /// <summary>
    /// Shows a status spinner while executing an operation
    /// </summary>
    /// <param name="status">Status message</param>
    /// <param name="operation">Operation to execute</param>
    public static async Task WithStatusAsync(string status, Func<Task> operation)
    {
        try
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(status, async ctx =>
                {
                    await operation();
                });
        }
        catch
        {
            // Fallback without status spinner
            SafeWriteLine($"? {status}");
            await operation();
            SafeWriteLine("? Done");
        }
    }

    /// <summary>
    /// Checks if console input is available and functional
    /// </summary>
    /// <returns>True if console input can be used, false otherwise</returns>
    public static bool IsConsoleInputAvailable()
    {
        try
        {
            // Check if input is redirected
            if (Console.IsInputRedirected)
                return false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // On Windows, check if we have a valid console input handle
                IntPtr inputHandle = GetStdHandle(STD_INPUT_HANDLE);
                if (inputHandle == IntPtr.Zero || inputHandle == new IntPtr(-1))
                    return false;

                // Check if console mode can be retrieved (indicates valid console)
                return GetConsoleMode(inputHandle, out _);
            }
            else
            {
                // On non-Windows platforms, assume available if not redirected
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Safely reads a key from console with fallback for unavailable input
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds, 0 for no timeout</param>
    /// <returns>True if a key was read, false if timed out or input unavailable</returns>
    public static bool SafeReadKey(int timeoutMs = 0)
    {
        try
        {
            if (!IsConsoleInputAvailable())
                return false;

            if (timeoutMs > 0)
            {
                // Implement timeout logic
                var startTime = DateTime.UtcNow;
                while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
                {
                    if (Console.KeyAvailable)
                    {
                        Console.ReadKey(true);
                        return true;
                    }
                    System.Threading.Thread.Sleep(50);
                }
                return false;
            }
            else
            {
                Console.ReadKey();
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // Console input not available
            return false;
        }
        catch
        {
            // Other console-related errors
            return false;
        }
    }

    /// <summary>
    /// Checks if a console window exists (Windows only)
    /// </summary>
    /// <returns>True if console window exists, false otherwise</returns>
    public static bool HasConsoleWindow()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetConsoleWindow() != IntPtr.Zero;
            }
            else
            {
                // On non-Windows, check if output is not redirected as approximation
                return !Console.IsOutputRedirected;
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Displays a welcome banner with application info
    /// </summary>
    /// <param name="appName">Application name</param>
    /// <param name="version">Application version</param>
    public static void ShowWelcomeBanner(string appName, string? version = null)
    {
        try
        {
            var figlet = new FigletText(appName)
                .Centered()
                .Color(Color.Cyan1);

            AnsiConsole.Write(figlet);

            if (!string.IsNullOrEmpty(version))
            {
                AnsiConsole.MarkupLine($"[dim]Version: {version}[/]");
            }

            AnsiConsole.WriteLine();
        }
        catch
        {
            // Fallback to simple banner
            WriteHeader($"{appName} {version ?? ""}");
        }
    }

    /// <summary>
    /// Initializes ANSI console support and capabilities
    /// </summary>
    /// <returns>True if ANSI support is available, false otherwise</returns>
    public static bool InitializeAnsiSupport()
    {
        try
        {
            // Enable ANSI support for Windows console
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                bool windowsAnsiEnabled = EnableWindowsAnsiSupport();
                if (!windowsAnsiEnabled)
                {
                    Console.WriteLine("Warning: Could not enable Windows ANSI support");
                }
            }

            // Configure Spectre.Console capabilities
            try
            {
                AnsiConsole.Profile.Capabilities.Ansi = true;
                AnsiConsole.Profile.Capabilities.Unicode = true;
            }
            catch
            {
                // Some versions may not allow setting these properties
                Console.WriteLine("Note: Could not configure Spectre.Console capabilities directly");
            }

            // Test ANSI support by writing a colored test message
            try
            {
                AnsiConsole.MarkupLine("[green]?[/] [dim]ANSI console support initialized[/]");
                return true;
            }
            catch
            {
                // Fallback test with simpler markup
                try
                {
                    AnsiConsole.WriteLine("ANSI console support initialized");
                    return true;
                }
                catch
                {
                    Console.WriteLine("ANSI console support initialized (basic mode)");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ANSI console initialization failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enables ANSI support on Windows console
    /// </summary>
    /// <returns>True if successful, false otherwise</returns>
    private static bool EnableWindowsAnsiSupport()
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return true; // Not Windows, assume ANSI is supported

            const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
            const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return false;

            if (!GetConsoleMode(handle, out uint mode))
                return false;

            mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN;
            return SetConsoleMode(handle, mode);
        }
        catch
        {
            return false;
        }
    }
}