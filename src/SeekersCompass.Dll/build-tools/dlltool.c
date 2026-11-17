// Stands in for dlltool when building Rust for windows-gnu: runs Rust's bundled GNU dlltool
// (path in REAL_DLLTOOL) with --as pointing at as.exe next to this file, since the bundled
// dlltool only looks for an assembler in its own folder and none is bundled.
#include <windows.h>
#include <stdio.h>
#include <wchar.h>

int wmain(int argc, wchar_t **argv) {
    wchar_t self[MAX_PATH], real[MAX_PATH], cmd[32768];
    if (!GetEnvironmentVariableW(L"REAL_DLLTOOL", real, MAX_PATH)) {
        fwprintf(stderr, L"dlltool wrapper: REAL_DLLTOOL is not set\n");
        return 1;
    }
    GetModuleFileNameW(NULL, self, MAX_PATH);
    *wcsrchr(self, L'\\') = 0;
    int n = swprintf(cmd, 32768, L"\"%ls\" --as \"%ls\\as.exe\"", real, self);
    for (int i = 1; i < argc; i++) n += swprintf(cmd + n, 32768 - n, L" \"%ls\"", argv[i]);
    STARTUPINFOW si = { sizeof si };
    PROCESS_INFORMATION pi;
    DWORD code = 1;
    if (!CreateProcessW(NULL, cmd, NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi)) {
        fwprintf(stderr, L"dlltool wrapper: cannot run %ls\n", cmd);
        return 1;
    }
    WaitForSingleObject(pi.hProcess, INFINITE);
    GetExitCodeProcess(pi.hProcess, &code);
    return (int)code;
}
