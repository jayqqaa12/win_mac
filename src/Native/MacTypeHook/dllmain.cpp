#include "dllmain.h"

#include "hooks_font.h"  // 与 hooks_font.cpp 共享的字体复写安装接口

#include <windows.h>

// —— 导出接口 ——

LRESULT WINAPI MacTypeHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    // WH_GETMESSAGE 挂钩回调：只需把消息继续往前传，保持目标进程正常处理。
    // 真正的 GDI 灰度字体复写已在 DllMain(DLL_PROCESS_ATTACH) 安装好。
    return CallNextHookEx(NULL, code, wParam, lParam);
}

BOOL WINAPI InstallHook(void)
{
    return FhInstall() ? TRUE : FALSE;
}

BOOL WINAPI UninstallHook(void)
{
    FhUninstall();
    return TRUE;
}

DWORD WINAPI GetHookVersion(void)
{
    return 0x0001;  // 版本 1
}

// —— 模块入口 ——
BOOL APIENTRY DllMain(HINSTANCE hinst, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
    {
        HINSTANCE self = hinst ? hinst : GetModuleHandleW(L"MacTypeHook.dll");
        DisableThreadLibraryCalls(self);
        // loader lock 注意：本 DLL 静态链接 gdi32，解析其导出不触发额外加载，
        // 因此在此处做 VirtualAlloc/VirtualProtect 是安全的。
        FhInstall();
        break;
    }
    case DLL_PROCESS_DETACH:
        if (reserved != nullptr) break;   // 进程退出：交由 OS 回收，不冒险拆钩
        FhUninstall();
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
        break;
    }
    return TRUE;
}