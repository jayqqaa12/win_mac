#pragma once

// 极简 x64 内联钩子（trampoline detour）：不依赖外部库，纯 MSVC C++ 即可编译。
// 仅支持 x64。为每个被钩函数生成一段保真跳板，并把原函数入口改写为
//  "mov rax, imm64 ; jmp rax" 的 12 字节绝对跳转。
//
// 安全策略：复制入口指令时若在前缀内遇到无法安全重定位的相对分支，
// 则放弃安装（返回 false），而不是冒险破坏原函数 —— 真机走查时不会因此崩溃。

#include <windows.h>
#include <cstdint>

class ApiDetour
{
public:
    ApiDetour() = default;
    ~ApiDetour() { Uninstall(); }

    ApiDetour(const ApiDetour&) = delete;
    ApiDetour& operator=(const ApiDetour&) = delete;

    // 解析目标模块/导出的函数地址作为被钩函数。
    bool Prepare(const wchar_t* module, const char* proc);

    // 在原函数入口写入跳转到 detour 的 12 字节绝对跳转；失败则整体回滚。
    bool Install(void* detour);

    // 恢复原函数（先抹去跳转再从原入口复制回原位）。
    void Uninstall();

    bool IsInstalled() const { return m_installed; }
    void* Original() const { return m_original; }

private:
    void* m_original = nullptr;      // 被钩函数地址
    BYTE  m_patch[16] = {};          // 入口前 12 字节原样备份
    void* m_trampoline = nullptr;    // 保真跳板（复制入口 + 跳回原函数）
    bool  m_installed = false;
};