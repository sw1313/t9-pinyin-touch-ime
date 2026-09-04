#pragma once
#include <windows.h>

// 在宿主进程内接住系统输入面板的显示请求并取消，官方触摸键盘因此根本不会出现，
// 而不是弹出后再被藏掉——弹出瞬间就会打乱 T9 的定位。
//
// CoreInputView 按线程绑定「调用时的前台窗口」，所以订阅必须跟着 TSF 的线程走，
// 并在本线程重新拿到焦点时重建。切走输入法后必须立刻停拦：语言栏已不是 T9
// 时，即使本线程 TIP 还没卸，显示请求也不能再 Cancel。不留注册表残留。
namespace SipCancel
{
    bool Enable();
    void BindHost(HWND hwnd);
    void Refresh();
    void Disable();
}
