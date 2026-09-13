#include "detour.h"

#include <windows.h>
#include <cstdint>

namespace
{
constexpr DWORD kCopyLen = 12;  // 需要覆盖的最小跳转长度

// ---- x64 指令长度解码（legacy map，仅计算每条指令字节数，不做完整反汇编） ----
// 用于判断入口前缀内是否有 PC 相对分支；一旦出现相对分支即放弃安装（保守、安全）。
size_t DecodeLength(const BYTE* b, size_t max)
{
    size_t i = 0;
    auto need = [&](size_t c) { return i + c <= max; };

    // 前缀（legacy prefix + REX）
    while (i < 15 && i < max)
    {
        BYTE x = b[i];
        if (x == 0x66 || x == 0x67 || x == 0xF0 || x == 0xF2 || x == 0xF3 ||
            (x >= 0x40 && x <= 0x4F) || x == 0x2E || x == 0x36 || x == 0x3E ||
            x == 0x64 || x == 0x65) { i++; continue; }
        break;
    }
    if (i + 1 > max) return 0;         // 预算不足
    BYTE op = b[i];

    auto modrm = [&](size_t& len) -> int {
        // 解析 ModRM 后的位移长度（返回需要向 i 增加的编码字节数 2 或 3/6）
        if (!need(i + 2)) return -1;
        BYTE m = b[i + 1];
        unsigned mod = (m >> 6) & 3, rm = m & 7;
        size_t add = 2;                // opcode + modrm
        if (mod == 0 && rm == 5) add += 4;
        else if (mod == 1) add += 1;
        else if (mod == 2) add += 4;
        if (mod != 3 && rm == 4) add += 1 + ((mod == 1) ? 1 : ((mod == 2) ? 4 : 0)); // SIB
        len = add;
        return 0;
    };

    if (op == 0x0F)
    {
        if (!need(i + 2)) return 0;
        BYTE op2 = b[i + 1];
        // 0F 8x = 相对近跳（6 字节）
        if ((op2 & 0xF0) == 0x80) return (i + 6 <= max) ? i + 6 : 0;
        // 其它 0F 指令多数带 ModRM
        size_t len;
        if (modrm(len) < 0) return 0;
        return (i + len <= max) ? i + len : 0;
    }
    if (op == 0xE8 || op == 0xE9)      // call/jmp rel32
    {
        return (i + 5 <= max) ? i + 5 : 0;
    }
    if (op == 0xEB || (op >= 0x70 && op <= 0x7F))   // jmp/jcc rel8
    {
        return (i + 2 <= max) ? i + 2 : 0;
    }
    if (op == 0xC3 || op == 0xC2 || op == 0xCC || op == 0xC9 || op == 0xCD || op == 0xF4 || op == 0x90)
    {
        return (op == 0xC2 || op == 0xCA) ? ((i + 3 <= max) ? i + 3 : 0) : (i + 1);
    }
    if (op == 0x68) return (i + 5 <= max) ? i + 5 : 0;            // push imm32
    if (op >= 0xB8 && op <= 0xBF) return (i + 5 <= max) ? i + 5 : 0; // mov r, imm32
    if (op >= 0x50 && op <= 0x57) return i + 1;                  // push r
    if (op >= 0x58 && op <= 0x5F) return i + 1;                  // pop r
    if (op >= 0x40 && op <= 0x4F) return 0;                       // REX 前缀（已在上方处理，理论上走不到）

    // 通用带 ModRM 指令
    size_t len;
    if (modrm(len) < 0) return 0;
    return (i + len <= max) ? i + len : 0;
}

// 找出可安全复制的入口前缀长度（≤ kCopyLen）。前缀内出现相对分支 → 返回 0（放弃）。
int SafePrefixLength(const BYTE* fn)
{
    size_t off = 0;
    while (off < kCopyLen)
    {
        size_t len = DecodeLength(fn + off, kCopyLen - off);
        if (len == 0 || len > (kCopyLen - off)) break;   // 指令越界 → 用已积累部分
        off += len;
    }
    return (int)off;
}

// 写入 kCopyLen 字节绝对跳转：mov rax, imm64(10B) ; jmp rax(2B)
bool WriteAbsoluteJump(void* target, void* destination)
{
    BYTE code[kCopyLen];
    code[0] = 0x48; code[1] = 0xB8;                 // mov rax, imm64
    memcpy(code + 2, &destination, sizeof(void*));  // rax = destination
    code[10] = 0xFF; code[11] = 0xE0;               // jmp rax

    DWORD old = 0;
    if (!VirtualProtect(target, kCopyLen, PAGE_EXECUTE_READWRITE, &old)) return false;
    memcpy(target, code, kCopyLen);
    VirtualProtect(target, kCopyLen, old, &old);
    FlushInstructionCache(GetCurrentProcess(), target, kCopyLen);
    return true;
}

// 生成跳板：复制前缀 + 追加跳回原函数入口+kCopyLen 的绝对跳转。
bool BuildTrampoline(const BYTE* fn, int prefixLen, void* tramp)
{
    BYTE* dst = (BYTE*)tramp;
    DWORD old;
    if (!VirtualProtect(tramp, prefixLen + 12, PAGE_EXECUTE_READWRITE, &old)) return false;
    memcpy(dst, fn, prefixLen);

    BYTE* backCode = dst + prefixLen;
    backCode[0] = 0x48; backCode[1] = 0xB8;
    void* backTarget = (BYTE*)fn + prefixLen;
    memcpy(backCode + 2, &backTarget, sizeof(void*));
    backCode[10] = 0xFF; backCode[11] = 0xE0;

    VirtualProtect(tramp, prefixLen + 12, old, &old);
    FlushInstructionCache(GetCurrentProcess(), tramp, prefixLen + 12);
    return true;
}
} // namespace

bool ApiDetour::Prepare(const wchar_t* module, const char* proc)
{
    HMODULE hm = LoadLibraryW(module);
    if (!hm) return false;
    m_original = static_cast<void*>(GetProcAddress(hm, proc));
    return m_original != nullptr;
}

bool ApiDetour::Install(void* detour)
{
    if (m_installed || !m_original || !detour) return false;

    BYTE* fn = static_cast<BYTE*>(m_original);
    int prefixLen = SafePrefixLength(fn);
    if (prefixLen < 5 || prefixLen > kCopyLen) return false;

    // 备份原入口字节（用于卸载/回滚）
    DWORD old;
    if (!VirtualProtect(fn, kCopyLen, PAGE_EXECUTE_READ, &old)) return false;
    memcpy(m_patch, fn, kCopyLen);
    VirtualProtect(fn, kCopyLen, old, &old);

    // 分配可执行跳板
    void* tramp = VirtualAlloc(nullptr, kCopyLen + 32, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE);
    if (!tramp) return false;
    if (!BuildTrampoline(fn, prefixLen, tramp))
    {
        VirtualFree(tramp, 0, MEM_RELEASE);
        return false;
    }

    // 写入入口绝对跳转；失败则回滚
    if (!WriteAbsoluteJump(fn, detour))
    {
        if (VirtualProtect(fn, kCopyLen, PAGE_EXECUTE_READWRITE, &old))
        {
            memcpy(fn, m_patch, kCopyLen);
            VirtualProtect(fn, kCopyLen, old, &old);
        }
        VirtualFree(tramp, 0, MEM_RELEASE);
        return false;
    }

    m_trampoline = tramp;
    m_installed = true;
    return true;
}

void ApiDetour::Uninstall()
{
    if (!m_installed || !m_original) return;
    BYTE* fn = static_cast<BYTE*>(m_original);
    DWORD old;
    if (VirtualProtect(fn, kCopyLen, PAGE_EXECUTE_READWRITE, &old))
    {
        memcpy(fn, m_patch, kCopyLen);
        VirtualProtect(fn, kCopyLen, old, &old);
        FlushInstructionCache(GetCurrentProcess(), fn, kCopyLen);
    }
    if (m_trampoline)
    {
        VirtualFree(m_trampoline, 0, MEM_RELEASE);
        m_trampoline = nullptr;
    }
    m_installed = false;
}