using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RensaioTray.Utils;

/// <summary>
/// Windows Vectored Exception Handler (VEH) that records native exceptions
/// (AccessViolation, illegal instruction, heap corruption, stack overflow, ...)
/// which managed handlers can never observe. IKVM runs JVM-derived native code
/// paths that can fault without any managed exception surfacing — when that
/// happens the process dies silently and the crash log shows nothing between
/// the last normal entry and the next startup.
///
/// Two hooks are installed:
///  - A first-chance VEH that logs fatal-class native exception codes as they
///    are raised (rate-limited, because the CLR itself raises access
///    violations for managed NullReferenceExceptions).
///  - A SetUnhandledExceptionFilter final filter that logs the exception that
///    is actually killing the process, right before Windows Error Reporting
///    takes over.
///
/// Best effort by design: the handlers run managed code, so a stack overflow
/// or severe heap corruption may prevent the log write itself. Everything is
/// wrapped so the handler can never make the crash worse.
/// </summary>
public static class NativeCrashHandler
{
    private const uint EXCEPTION_ACCESS_VIOLATION = 0xC0000005;
    private const uint EXCEPTION_IN_PAGE_ERROR = 0xC0000006;
    private const uint EXCEPTION_ILLEGAL_INSTRUCTION = 0xC000001D;
    private const uint EXCEPTION_PRIV_INSTRUCTION = 0xC0000096;
    private const uint EXCEPTION_STACK_OVERFLOW = 0xC00000FD;
    private const uint STATUS_HEAP_CORRUPTION = 0xC0000374;
    private const uint STATUS_STACK_BUFFER_OVERRUN = 0xC0000409;

    private const int EXCEPTION_CONTINUE_SEARCH = 0;

    // The CLR translates first-chance access violations raised inside managed
    // code into NullReferenceException, so a burst of AVs is not necessarily a
    // native bug. Cap first-chance entries per process so a managed NRE storm
    // cannot flood the crash log the way the dex2jar exceptions once did.
    private const int MaxFirstChanceEntries = 64;

    private static string? _logPath;
    private static IntPtr _vehHandle = IntPtr.Zero;
    private static IntPtr _previousFilter = IntPtr.Zero;
    private static int _firstChanceEntries;
    private static int _inHandler;

    // Keep delegate instances alive for the process lifetime; if the GC
    // collects them the native callback becomes a dangling pointer.
    private static VectoredHandler? _vehDelegate;
    private static VectoredHandler? _unhandledFilterDelegate;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int VectoredHandler(IntPtr exceptionPointers);

    [StructLayout(LayoutKind.Sequential)]
    private struct EXCEPTION_POINTERS
    {
        public IntPtr ExceptionRecord;
        public IntPtr ContextRecord;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EXCEPTION_RECORD
    {
        public uint ExceptionCode;
        public uint ExceptionFlags;
        public IntPtr NestedRecord;
        public IntPtr ExceptionAddress;
        public uint NumberParameters;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)]
        public IntPtr[] ExceptionInformation;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr AddVectoredExceptionHandler(uint first, VectoredHandler handler);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint RemoveVectoredExceptionHandler(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr SetUnhandledExceptionFilter(VectoredHandler? filter);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private const uint GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS = 0x00000004;
    private const uint GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetModuleHandleEx(uint flags, IntPtr address, out IntPtr module);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetModuleFileName(IntPtr module, StringBuilder fileName, uint size);

    private const uint FILE_APPEND_DATA = 0x0004;
    private const uint FILE_SHARE_READ_WRITE = 0x00000003;
    private const uint OPEN_ALWAYS = 4;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr file, byte[] buffer, uint bytesToWrite,
        out uint bytesWritten, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// Sets the file the handler appends to. Must be called before Install().
    /// </summary>
    public static void SetLogPath(string path)
    {
        _logPath = path;
    }

    /// <summary>
    /// Installs the VEH and the final unhandled-exception filter. No-op on
    /// non-Windows platforms or if already installed.
    /// </summary>
    public static void Install()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || _vehHandle != IntPtr.Zero)
            return;

        _vehDelegate = FirstChanceHandler;
        // first=1: called before other VEH handlers so nothing can swallow the
        // record before we log it.
        _vehHandle = AddVectoredExceptionHandler(1, _vehDelegate);

        _unhandledFilterDelegate = UnhandledFilter;
        _previousFilter = SetUnhandledExceptionFilter(_unhandledFilterDelegate);
    }

    /// <summary>
    /// Removes the handlers. Safe to call multiple times.
    /// </summary>
    public static void Uninstall()
    {
        if (_vehHandle != IntPtr.Zero)
        {
            RemoveVectoredExceptionHandler(_vehHandle);
            _vehHandle = IntPtr.Zero;
        }
        if (_unhandledFilterDelegate != null)
        {
            SetUnhandledExceptionFilter(null);
            _unhandledFilterDelegate = null;
        }
        _vehDelegate = null;
    }

    private static bool IsFatalNativeCode(uint code) => code switch
    {
        EXCEPTION_ACCESS_VIOLATION => true,
        EXCEPTION_IN_PAGE_ERROR => true,
        EXCEPTION_ILLEGAL_INSTRUCTION => true,
        EXCEPTION_PRIV_INSTRUCTION => true,
        EXCEPTION_STACK_OVERFLOW => true,
        STATUS_HEAP_CORRUPTION => true,
        STATUS_STACK_BUFFER_OVERRUN => true,
        _ => false
    };

    private static int FirstChanceHandler(IntPtr exceptionPointers)
    {
        try
        {
            // Never recurse if logging itself faults on this thread.
            if (Interlocked.CompareExchange(ref _inHandler, 1, 0) != 0)
                return EXCEPTION_CONTINUE_SEARCH;
            try
            {
                if (!TryReadRecord(exceptionPointers, out EXCEPTION_RECORD record))
                    return EXCEPTION_CONTINUE_SEARCH;
                // Only fatal-class native codes; managed exceptions (0xE0434352),
                // C++ EH (0xE06D7363), breakpoints, thread-naming, and debugger
                // notifications all pass through VEH and are pure noise here.
                if (!IsFatalNativeCode(record.ExceptionCode))
                    return EXCEPTION_CONTINUE_SEARCH;
                int entry = Interlocked.Increment(ref _firstChanceEntries);
                if (entry > MaxFirstChanceEntries)
                    return EXCEPTION_CONTINUE_SEARCH;

                AppendEntry("NATIVE FIRST-CHANCE", record,
                    entry == MaxFirstChanceEntries
                        ? "(first-chance limit reached; further first-chance native exceptions suppressed)"
                        : null);
            }
            finally
            {
                Interlocked.Exchange(ref _inHandler, 0);
            }
        }
        catch
        {
            // The handler must never make the crash worse.
        }
        return EXCEPTION_CONTINUE_SEARCH;
    }

    private static int UnhandledFilter(IntPtr exceptionPointers)
    {
        try
        {
            // No rate limit here: this is the exception actually killing the
            // process, logged at most once before WER/default handling runs.
            if (TryReadRecord(exceptionPointers, out EXCEPTION_RECORD record))
            {
                AppendEntry("NATIVE FATAL (unhandled, process terminating)", record, null);
            }
        }
        catch
        {
        }

        // Chain to whatever filter was installed before us (the CLR installs
        // its own), so normal crash semantics are preserved.
        if (_previousFilter != IntPtr.Zero)
        {
            try
            {
                var previous = Marshal.GetDelegateForFunctionPointer<VectoredHandler>(_previousFilter);
                return previous(exceptionPointers);
            }
            catch
            {
            }
        }
        return EXCEPTION_CONTINUE_SEARCH;
    }

    private static bool TryReadRecord(IntPtr exceptionPointers, out EXCEPTION_RECORD record)
    {
        record = default;
        if (exceptionPointers == IntPtr.Zero)
            return false;
        EXCEPTION_POINTERS pointers = Marshal.PtrToStructure<EXCEPTION_POINTERS>(exceptionPointers);
        if (pointers.ExceptionRecord == IntPtr.Zero)
            return false;
        record = Marshal.PtrToStructure<EXCEPTION_RECORD>(pointers.ExceptionRecord);
        return true;
    }

    private static void AppendEntry(string header, in EXCEPTION_RECORD record, string? note)
    {
        string? path = _logPath;
        if (string.IsNullOrEmpty(path))
            return;

        var sb = new StringBuilder(512);
        sb.Append("\r\n[").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] CRASH: ")
          .Append(header).Append("\r\n");
        sb.Append("  Code:    0x").Append(record.ExceptionCode.ToString("X8"))
          .Append(" (").Append(DescribeCode(record.ExceptionCode)).Append(")\r\n");
        sb.Append("  Address: 0x").Append(record.ExceptionAddress.ToString("X"))
          .Append(DescribeModule(record.ExceptionAddress)).Append("\r\n");
        sb.Append("  Thread:  ").Append(GetCurrentThreadId()).Append("\r\n");

        // For AV / in-page errors the first two parameters describe the access:
        // [0] 0=read 1=write 8=execute, [1] the address that was accessed.
        if ((record.ExceptionCode == EXCEPTION_ACCESS_VIOLATION ||
             record.ExceptionCode == EXCEPTION_IN_PAGE_ERROR) &&
            record.NumberParameters >= 2 && record.ExceptionInformation != null)
        {
            long kind = record.ExceptionInformation[0].ToInt64();
            string access = kind switch { 0 => "read", 1 => "write", 8 => "execute", _ => "unknown" };
            sb.Append("  Access:  ").Append(access)
              .Append(" of 0x").Append(record.ExceptionInformation[1].ToString("X")).Append("\r\n");
        }
        if (note != null)
            sb.Append("  Note:    ").Append(note).Append("\r\n");

        WriteRaw(path, sb.ToString());
    }

    private static string DescribeCode(uint code) => code switch
    {
        EXCEPTION_ACCESS_VIOLATION => "ACCESS_VIOLATION",
        EXCEPTION_IN_PAGE_ERROR => "IN_PAGE_ERROR",
        EXCEPTION_ILLEGAL_INSTRUCTION => "ILLEGAL_INSTRUCTION",
        EXCEPTION_PRIV_INSTRUCTION => "PRIVILEGED_INSTRUCTION",
        EXCEPTION_STACK_OVERFLOW => "STACK_OVERFLOW",
        STATUS_HEAP_CORRUPTION => "HEAP_CORRUPTION",
        STATUS_STACK_BUFFER_OVERRUN => "STACK_BUFFER_OVERRUN / FAIL_FAST",
        _ => "UNKNOWN"
    };

    private static string DescribeModule(IntPtr address)
    {
        try
        {
            if (GetModuleHandleEx(
                    GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                    address, out IntPtr module) && module != IntPtr.Zero)
            {
                var name = new StringBuilder(1024);
                if (GetModuleFileName(module, name, (uint)name.Capacity) > 0)
                {
                    long offset = address.ToInt64() - module.ToInt64();
                    return " (" + name + "+0x" + offset.ToString("X") + ")";
                }
            }
        }
        catch
        {
        }
        // JIT-compiled or trampoline code has no backing module.
        return " (no module - dynamic/JIT code)";
    }

    private static void WriteRaw(string path, string text)
    {
        // Win32 append instead of File.AppendAllText: FILE_APPEND_DATA writes
        // are atomic per call, tolerate FallbackCrashLogger having the file
        // open, and avoid FileStream's buffering machinery while the process
        // is in an undefined state.
        IntPtr handle = CreateFile(path, FILE_APPEND_DATA, FILE_SHARE_READ_WRITE,
            IntPtr.Zero, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            return;
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            WriteFile(handle, bytes, (uint)bytes.Length, out _, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(handle);
        }
    }
}
