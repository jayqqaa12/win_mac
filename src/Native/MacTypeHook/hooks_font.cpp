#include "hooks_font.h"

#include "detour.h"
#include <windows.h>

namespace
{
// —— 被钩原函数（gdi32） ——
ApiDetour g_fontCreate;      // CreateFontIndirectW
ApiDetour g_fontCreateEx;    // CreateFontIndirectExW

bool g_active = false;

// CreateFontIndirectW 原函数类型与接管函数。
using CreateFontIndirectW_t = HFONT(WINAPI*)(const LOGFONTW* lf);
CreateFontIndirectW_t CreateFontIndirectW_orig = nullptr;

// CreateFontIndirectExW 原函数类型与接管函数。
using CreateFontIndirectExW_t = HFONT(WINAPI*)(const ENUMLOGFONTEXDVW* pelf, DWORD flags);
CreateFontIndirectExW_t CreateFontIndirectExW_orig = nullptr;

HFONT WINAPI Hook_CreateFontIndirectW(const LOGFONTW* lf)
{
    if (!CreateFontIndirectW_orig || !lf) return NULL;
    // 复制一份而非改写调用方结构；仅把 ClearType 质素降为灰度 AA。
    LOGFONTW copy = *lf;
    if (copy.lfQuality == CLEARTYPE_QUALITY)
        copy.lfQuality = ANTIALIASED_QUALITY;
    // 也兼容某些进程渲染时用 DEFAULT_QUALITY（其仍可能走 ClearType）→ 强制灰度
    else if (copy.lfQuality == DEFAULT_QUALITY || copy.lfQuality == NONANTIALIASED_QUALITY)
        copy.lfQuality = ANTIALIASED_QUALITY;
    return CreateFontIndirectW_orig(&copy);
}

HFONT WINAPI Hook_CreateFontIndirectExW(const ENUMLOGFONTEXDVW* pelf, DWORD flags)
{
    if (!CreateFontIndirectExW_orig || !pelf) return NULL;
    ENUMLOGFONTEXDVW copy = *pelf;
    if (copy.elfEnumLogfontEx.elfLogFont.lfQuality == CLEARTYPE_QUALITY ||
        copy.elfEnumLogfontEx.elfLogFont.lfQuality == DEFAULT_QUALITY ||
        copy.elfEnumLogfontEx.elfLogFont.lfQuality == NONANTIALIASED_QUALITY)
        copy.elfEnumLogfontEx.elfLogFont.lfQuality = ANTIALIASED_QUALITY;
    return CreateFontIndirectExW_orig(&copy, flags);
}
} // namespace

bool FhInstall()
{
    if (g_active) return true;

    // 解析 gdi32 中两个字体创建入口。gdi32 已静态链接/加载，不会触发额外 load，规避 loader lock。
    if (g_fontCreate.Prepare(L"gdi32.dll", "CreateFontIndirectW") &&
        g_fontCreate.Install(static_cast<void*>(Hook_CreateFontIndirectW)))
    {
        CreateFontIndirectW_orig =
            reinterpret_cast<CreateFontIndirectW_t>(g_fontCreate.Original());
    }

    if (g_fontCreateEx.Prepare(L"gdi32.dll", "CreateFontIndirectExW") &&
        g_fontCreateEx.Install(static_cast<void*>(Hook_CreateFontIndirectExW)))
    {
        CreateFontIndirectExW_orig =
            reinterpret_cast<CreateFontIndirectExW_t>(g_fontCreateEx.Original());
    }

    g_active = g_fontCreate.IsInstalled() || g_fontCreateEx.IsInstalled();
    return g_active;
}

void FhUninstall()
{
    if (g_fontCreate.IsInstalled()) g_fontCreate.Uninstall();
    if (g_fontCreateEx.IsInstalled()) g_fontCreateEx.Uninstall();
    CreateFontIndirectW_orig = nullptr;
    CreateFontIndirectExW_orig = nullptr;
    g_active = false;
}

bool FhActive() { return g_active; }