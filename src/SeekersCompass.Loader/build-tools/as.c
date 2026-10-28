// Stands in for the GNU assembler that Rust's bundled dlltool calls (windows-gnu builds):
// assembles its GAS output with `zig cc -c` (clang). Accepts `[--64] [-o out] input.s`.
#include <windows.h>
#include <stdio.h>
#include <wchar.h>

int wmain(int argc, wchar_t **argv) {
    wchar_t self[MAX_PATH], cmd[32768];
    const wchar_t *out = NULL, *in = NULL;
    for (int i = 1; i < argc; i++) {
        if (!wcscmp(argv[i], L"-o") && i + 1 < argc) out = argv[++i];
        else if (argv[i][0] != L'-') in = argv[i];
    }
    if (!out || !in) { fwprintf(stderr, L"as wrapper: need -o <out> <input>\n"); return 1; }
    GetModuleFileNameW(NULL, self, MAX_PATH);
    *wcsrchr(self, L'\\') = 0;
    swprintf(cmd, 32768, L"\"%ls\\..\\zig\\zig.exe\" cc -target x86_64-windows-gnu -Wno-unused-command-line-argument -c -x assembler -o \"%ls\" \"%ls\"", self, out, in);
    STARTUPINFOW si = { sizeof si };
    PROCESS_INFORMATION pi;
    DWORD code = 1;
    if (!CreateProcessW(NULL, cmd, NULL, NULL, FALSE, 0, NULL, NULL, &si, &pi)) {
        fwprintf(stderr, L"as wrapper: cannot run %ls\n", cmd);
        return 1;
    }
    WaitForSingleObject(pi.hProcess, INFINITE);
    GetExitCodeProcess(pi.hProcess, &code);
    return (int)code;
}
