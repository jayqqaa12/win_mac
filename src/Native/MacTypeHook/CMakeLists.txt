cmake_minimum_required(VERSION 3.20)
project(MacTypeHook CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# MacType 式全局灰度字体渲染钩子 DLL。
# 由 WinMac.Setup.Installer 通过 SetWindowsHookEx(WH_GETMESSAGE) 注入到所有 GUI 进程；
# DLL 在目标进程 DllMain 里安装 GDI 字体灰度 AA 复写（把 CLEARTYPE 强制降为灰度）。
add_library(MacTypeHook SHARED
    dllmain.cpp
    detour.cpp
    hooks_font.cpp
)

target_include_directories(MacTypeHook PRIVATE "${CMAKE_CURRENT_LIST_DIR}")
target_compile_definitions(MacTypeHook PRIVATE _WIN32_WINNT=0x0A00 NOMINMAX STRICT UNICODE _UNICODE)

set_target_properties(MacTypeHook PROPERTIES
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
)

if(WIN32)
    target_link_libraries(MacTypeHook PRIVATE user32 gdi32 kernel32)
endif()