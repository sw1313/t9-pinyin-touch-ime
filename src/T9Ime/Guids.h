#pragma once
#include <initguid.h>

// T9 九键文本服务（自有 CLSID，与多文无关）
// {A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001}
DEFINE_GUID(CLSID_T9Ime,
    0xa7e91c20, 0x4b3d, 0x4f18, 0x9c, 0x2a, 0x1b, 0x8e, 0x6d, 0x0a, 0x10, 0x01);

// {A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1002}
DEFINE_GUID(GUID_T9ImeProfile,
    0xa7e91c20, 0x4b3d, 0x4f18, 0x9c, 0x2a, 0x1b, 0x8e, 0x6d, 0x0a, 0x10, 0x02);

#define T9IME_DESC L"T9 九键"
#define T9IME_PIPE L"\\\\.\\pipe\\T9Pane.Ime"
#define T9IME_CMD_HOST L"\\\\.\\pipe\\T9Pane.Ime.Cmd"
#define T9IME_PIPE_LOCAL L"\\\\.\\pipe\\LOCAL\\T9Pane.Ime"
#define T9IME_CMD_HOST_LOCAL L"\\\\.\\pipe\\LOCAL\\T9Pane.Ime.Cmd"
#define T9IME_MSG_CLASS L"T9Ime.Msg"
#define T9IME_REG_ROOT L"Software\\T9Pane"
