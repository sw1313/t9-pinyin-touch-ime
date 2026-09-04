// 向指定屏幕坐标注入一次真实的合成触摸点击，用来在无人值守的情况下复现
// 「平板上手指点输入框」这条路径。
//
//   TouchTap.exe <x> <y>

#include <windows.h>
#include <cstdio>
#include <cstdlib>

int wmain(int argc, wchar_t** argv)
{
    if (argc < 3)
    {
        wprintf(L"用法: TouchTap.exe <x> <y>\n");
        return 2;
    }

    POINT at = {_wtoi(argv[1]), _wtoi(argv[2])};

    const auto device = CreateSyntheticPointerDevice(PT_TOUCH, 1, POINTER_FEEDBACK_DEFAULT);
    if (!device)
    {
        wprintf(L"CreateSyntheticPointerDevice 失败: %lu\n", GetLastError());
        return 1;
    }

    POINTER_TYPE_INFO info = {};
    info.type = PT_TOUCH;
    info.touchInfo.pointerInfo.pointerType = PT_TOUCH;
    info.touchInfo.pointerInfo.pointerId = 0;
    info.touchInfo.pointerInfo.ptPixelLocation = at;
    info.touchInfo.touchFlags = TOUCH_FLAG_NONE;
    info.touchInfo.touchMask = TOUCH_MASK_CONTACTAREA | TOUCH_MASK_PRESSURE;
    info.touchInfo.rcContact = {at.x - 4, at.y - 4, at.x + 4, at.y + 4};
    info.touchInfo.pressure = 1024;

    struct Step
    {
        UINT32 flags;
        DWORD pause;
    };
    const Step steps[] = {
        {POINTER_FLAG_DOWN | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT, 60},
        {POINTER_FLAG_UPDATE | POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT, 60},
        {POINTER_FLAG_UP, 0},
    };

    for (const auto& step : steps)
    {
        info.touchInfo.pointerInfo.pointerFlags = step.flags;
        if (!InjectSyntheticPointerInput(device, &info, 1))
        {
            wprintf(L"InjectSyntheticPointerInput 失败: %lu\n", GetLastError());
            DestroySyntheticPointerDevice(device);
            return 1;
        }
        if (step.pause)
        {
            Sleep(step.pause);
        }
    }

    DestroySyntheticPointerDevice(device);
    wprintf(L"已在 (%ld,%ld) 注入触摸点击\n", at.x, at.y);
    return 0;
}
