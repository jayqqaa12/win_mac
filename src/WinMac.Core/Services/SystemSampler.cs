using WinMac.Core.Win32;

namespace WinMac.Core.Services;

/// <summary>一次系统资源快照（可被多个组件复用）。</summary>
public readonly record struct SystemSample(double CpuPercent, double UsedMemoryPercent, ulong UsedMib, ulong TotalMib);

/// <summary>
/// 轻量的 CPU / 内存采样器（低频调用，每样本仅 2 次 kernel32 调用，零分配）。
/// CPU 使用率用两次 <see cref="NativeMethods.GetSystemTimes"/> 的时间差计算，
/// 首次调用返回 0（尚无基线）。
/// </summary>
public sealed class SystemSampler
{
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasPrev;

    public SystemSample Sample()
    {
        double cpu = 0;
        if (NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong idleTime = idle.ToUInt64();
            ulong kernelTime = kernel.ToUInt64();
            ulong userTime = user.ToUInt64();

            if (_hasPrev)
            {
                ulong idleDiff = idleTime - _prevIdle;
                ulong kernelDiff = kernelTime - _prevKernel;
                ulong userDiff = userTime - _prevUser;
                ulong total = kernelDiff + userDiff;
                if (total > 0)
                    cpu = 100.0 * (1.0 - (double)idleDiff / total);
            }

            _prevIdle = idleTime;
            _prevKernel = kernelTime;
            _prevUser = userTime;
            _hasPrev = true;
        }

        var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        NativeMethods.GlobalMemoryStatusEx(ref mem);

        ulong totalMib = mem.ullTotalPhys / (1024 * 1024);
        ulong usedMib = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024 * 1024);
        double usedPercent = totalMib > 0 ? 100.0 * usedMib / totalMib : 0;

        return new SystemSample(Math.Clamp(cpu, 0, 100), usedPercent, usedMib, totalMib);
    }
}