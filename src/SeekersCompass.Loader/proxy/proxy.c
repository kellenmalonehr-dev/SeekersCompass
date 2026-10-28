// Seeker's Compass proxy: the game loads this as xinput1_4.dll from its own folder at startup,
// before any game code runs. Every XInput call is passed on to Windows' real xinput1_4.dll.
//
// It then decides, without changing anything, whether Seeker's Compass may load:
//   - Easy Anti-Cheat is running                  -> no (the game may be going online);
//   - Mod Engine 2/3 started or is in the game    -> no (it loads mods itself, e.g. Convergence);
//   - otherwise (Seamless Co-op's launcher, or an offline start) -> load the Rust loader in
//     SeekersCompass\ and let it swap in the mod's files the way Mod Engine 3 does. Unless
//     Seamless Co-op's launcher started the game, the mod plays on its own save (ER0000.skc).
// Each decision is appended to SeekersCompass\launch.log.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <tlhelp32.h>
#include <stdarg.h>
#include <stdio.h>
#include <wchar.h>

// ---- XInput forwarding -------------------------------------------------------------------------

static const struct { const char *name; WORD ordinal; } k_xinput[] = {
    {"XInputGetState", 2}, {"XInputSetState", 3}, {"XInputGetCapabilities", 4}, {"XInputEnable", 5},
    {"XInputGetBatteryInformation", 7}, {"XInputGetKeystroke", 8}, {"XInputGetAudioDeviceIds", 10},
    {NULL, 100}, {NULL, 101}, {NULL, 102}, {NULL, 103}, {NULL, 104}, {NULL, 108}, {NULL, 109},
};
void *g_xinput[sizeof k_xinput / sizeof k_xinput[0]];

// Each export jumps straight to the real function, so no signatures are needed here.
#define XSTUB(name, index) ".globl " #name "\n" #name ":\n  jmp *g_xinput+" #index "*8(%rip)\n"
__asm__(".text\n"
    XSTUB(XInputGetState, 0) XSTUB(XInputSetState, 1) XSTUB(XInputGetCapabilities, 2)
    XSTUB(XInputEnable, 3) XSTUB(XInputGetBatteryInformation, 4) XSTUB(XInputGetKeystroke, 5)
    XSTUB(XInputGetAudioDeviceIds, 6) XSTUB(XInputOrdinal100, 7) XSTUB(XInputOrdinal101, 8)
    XSTUB(XInputOrdinal102, 9) XSTUB(XInputOrdinal103, 10) XSTUB(XInputOrdinal104, 11)
    XSTUB(XInputOrdinal108, 12) XSTUB(XInputOrdinal109, 13));

static DWORD WINAPI XInputMissing(void) { return ERROR_DEVICE_NOT_CONNECTED; }

static void LoadRealXInput(void) {
    WCHAR path[MAX_PATH];
    UINT n = GetSystemDirectoryW(path, MAX_PATH);
    HMODULE real = NULL;
    if (n && n < MAX_PATH - 20) { wcscpy(path + n, L"\\xinput1_4.dll"); real = LoadLibraryW(path); }
    for (size_t i = 0; i < sizeof k_xinput / sizeof k_xinput[0]; i++) {
        FARPROC f = real ? GetProcAddress(real, k_xinput[i].name ? k_xinput[i].name : MAKEINTRESOURCEA(k_xinput[i].ordinal)) : NULL;
        g_xinput[i] = f ? (void *)f : (void *)XInputMissing;
    }
}

// ---- deciding ----------------------------------------------------------------------------------

static HANDLE g_log = INVALID_HANDLE_VALUE;

static void Log(const WCHAR *fmt, ...) {
    if (g_log == INVALID_HANDLE_VALUE) return;
    WCHAR wide[1200];
    va_list ap; va_start(ap, fmt);
    int m = _vsnwprintf(wide, 1200 - 3, fmt, ap);
    va_end(ap);
    if (m < 0) m = 1200 - 3;
    wcscpy(wide + m, L"\r\n");
    char utf8[3600];
    int len = WideCharToMultiByte(CP_UTF8, 0, wide, -1, utf8, sizeof utf8, NULL, NULL);
    DWORD written;
    if (len > 1) WriteFile(g_log, utf8, len - 1, &written, NULL);
}

static const WCHAR *FileName(const WCHAR *p) {
    const WCHAR *name = p;
    for (; *p; p++) if (*p == L'\\' || *p == L'/') name = p + 1;
    return name;
}

// The exe name of the process that started this one, and whether any Easy Anti-Cheat process is
// running. Toolhelp only reads the process list, which is safe this early in startup.
static void ScanProcesses(WCHAR *parent, size_t parentLen, BOOL *eac) {
    parent[0] = 0; *eac = FALSE;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) { *eac = TRUE; return; } // can't check: assume the worst
    PROCESSENTRY32W pe = { .dwSize = sizeof pe };
    DWORD self = GetCurrentProcessId(), parentId = 0;
    for (BOOL ok = Process32FirstW(snap, &pe); ok; ok = Process32NextW(snap, &pe)) {
        if (pe.th32ProcessID == self) parentId = pe.th32ParentProcessID;
        if (_wcsnicmp(pe.szExeFile, L"EasyAntiCheat", 13) == 0) *eac = TRUE;
    }
    pe.dwSize = sizeof pe;
    for (BOOL ok = Process32FirstW(snap, &pe); ok && parentId; ok = Process32NextW(snap, &pe))
        if (pe.th32ProcessID == parentId) { wcsncpy(parent, pe.szExeFile, parentLen - 1); parent[parentLen - 1] = 0; break; }
    CloseHandle(snap);
}

typedef BOOL (*AttachFn)(const WCHAR *modDir, size_t len, BOOL seamless);

static void Start(HMODULE self) {
    WCHAR exe[MAX_PATH], dll[MAX_PATH], gameDir[MAX_PATH], modDir[MAX_PATH], path[MAX_PATH];
    if (!GetModuleFileNameW(NULL, exe, MAX_PATH) || _wcsicmp(FileName(exe), L"eldenring.exe") != 0) return;
    wcscpy(gameDir, exe); *(WCHAR *)FileName(gameDir) = 0;
    if (!GetModuleFileNameW(self, dll, MAX_PATH)) return;
    wcscpy(modDir, dll); *(WCHAR *)FileName(modDir) = 0;
    wcscat(modDir, L"SeekersCompass");
    _snwprintf(path, MAX_PATH, L"%ls\\seekers_compass_loader.dll", modDir);
    if (GetFileAttributesW(path) == INVALID_FILE_ATTRIBUTES) return; // mod not installed here

    WCHAR logPath[MAX_PATH];
    _snwprintf(logPath, MAX_PATH, L"%ls\\launch.log", modDir);
    g_log = CreateFileW(logPath, FILE_APPEND_DATA, FILE_SHARE_READ, NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    SYSTEMTIME t; GetLocalTime(&t);
    WCHAR parent[MAX_PATH]; BOOL eac;
    ScanProcesses(parent, MAX_PATH, &eac);
    Log(L"%04d-%02d-%02d %02d:%02d:%02d game started by %ls", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute, t.wSecond,
        parent[0] ? parent : L"(unknown)");

    if (eac) { Log(L"  Easy Anti-Cheat is running: Seeker's Compass stays off."); goto done; }
    if (_wcsnicmp(parent, L"me3", 3) == 0 || _wcsnicmp(parent, L"modengine", 9) == 0 ||
        GetModuleHandleW(L"me3_mod_host.dll") || GetModuleHandleW(L"modengine2.dll")) {
        Log(L"  Mod Engine is loading mods: this loader stays off."); goto done;
    }

    WCHAR ersc[MAX_PATH];
    _snwprintf(ersc, MAX_PATH, L"%lsSeamlessCoop\\ersc.dll", gameDir);
    BOOL seamless = _wcsicmp(parent, L"ersc_launcher.exe") == 0 && GetFileAttributesW(ersc) != INVALID_FILE_ATTRIBUTES;

    HMODULE loader = LoadLibraryW(path);
    AttachFn attach = loader ? (AttachFn)(void *)GetProcAddress(loader, "seekers_compass_attach") : NULL;
    if (!attach) { Log(L"  could not load seekers_compass_loader.dll (error %lu): the game runs unmodded.", GetLastError()); goto done; }
    BOOL ok = attach(modDir, wcslen(modDir), seamless);
    Log(ok ? L"  Seeker's Compass on%ls (details in loader.log)." : L"  the loader failed to start%ls: the game runs unmodded (see loader.log).",
        seamless ? L", with Seamless Co-op's save" : L", on its own save (ER0000.skc)");
done:
    CloseHandle(g_log);
    g_log = INVALID_HANDLE_VALUE;
}

BOOL WINAPI DllMain(HINSTANCE self, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(self);
        LoadRealXInput();
        Start(self);
    }
    return TRUE;
}
