#pragma once

#include <windows.h>

// GDI 字体灰度 AA 复写模块：把 CLEARTYPE 强制降为灰度（ANTIALIASED）渲染。
// 复写目标：CreateFontIndirectW / CreateFontIndirectExW（Win32 文本渲染入口）。
//
// 说明：DirectWrite 走独立的渲染对象，需在渲染器层复写，超出本 DLL 盲写范围；
// 目前覆盖 GDI 路径，DirectWrite 灰度化列为本模块真机走查阶段的可选增强。

// 安装字体复写，返回是否全部成功。
bool FhInstall();

// 卸载字体复写。
void FhUninstall();

// 是否已安装。
bool FhActive();