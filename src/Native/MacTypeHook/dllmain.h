#pragma once

#include <windows.h>

// —— 可被安装器经 GetProcAddress 取到的导出接口 ——
extern "C"
{
    // WH_GETMESSAGE 挂钩回调：系统把本 DLL 注入到所有 GUI 进程时即被回调；
    // 真实的 GDI 字体复写在 DllMain(DLL_PROCESS_ATTACH) 里安装。
    __declspec(dllexport) LRESULT WINAPI MacTypeHookProc(int code, WPARAM wParam, LPARAM lParam);

    // 手动安装 / 卸载 GDI 灰度字体复写（返回是否成功）。
    __declspec(dllexport) BOOL WINAPI InstallHook(void);
    __declspec(dllexport) BOOL WINAPI UninstallHook(void);

    // 版本号，便于安装器做兼容判断。
    __declspec(dllexport) DWORD WINAPI GetHookVersion(void);
}