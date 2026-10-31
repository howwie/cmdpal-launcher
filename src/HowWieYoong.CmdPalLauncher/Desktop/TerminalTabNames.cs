using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static HowWieYoong.CmdPalLauncher.Desktop.NativeMethods;

namespace HowWieYoong.CmdPalLauncher.Desktop;

/// <summary>Reads the tab names in a Windows Terminal window, which Terminal doesn't offer any other way.</summary>
/// <remarks>
/// Calls UI Automation through its COM vtables, numbered as in UIAutomationClient.h, because its interfaces don't
/// support late binding like <see cref="ExplorerFolders"/> and this works in trimmed and AOT builds too.
/// </remarks>
internal static unsafe class TerminalTabNames
{
    private const int UIA_ControlTypePropertyId = 30003;
    private const int UIA_TabItemControlTypeId = 50019;
    private const int TreeScope_Descendants = 4;
    private const ushort VT_I4 = 3;

    private static readonly Guid CLSID_CUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    private static readonly Guid IID_IUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");

    /// <summary>Tab names from left to right, or null if they can't be read, e.g. when the tab row is hidden.</summary>
    public static IReadOnlyList<string>? For(nint window)
    {
        nint automation = 0, element = 0, condition = 0, found = 0;
        try
        {
            if (CoCreateInstance(CLSID_CUIAutomation, 0, CLSCTX_INPROC_SERVER, IID_IUIAutomation, out automation) < 0
                || ((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Method(automation, 6))(automation, window, &element) < 0 // ElementFromHandle
                || ((delegate* unmanaged[Stdcall]<nint, int, VARIANT, nint*, int>)Method(automation, 23))(automation, UIA_ControlTypePropertyId, new VARIANT { vt = VT_I4, lVal = UIA_TabItemControlTypeId }, &condition) < 0 // CreatePropertyCondition
                || element == 0
                || ((delegate* unmanaged[Stdcall]<nint, int, nint, nint*, int>)Method(element, 6))(element, TreeScope_Descendants, condition, &found) < 0 // FindAll
                || found == 0)
            {
                return null;
            }

            var count = 0;
            if (((delegate* unmanaged[Stdcall]<nint, int*, int>)Method(found, 3))(found, &count) < 0) // get_Length
            {
                return null;
            }

            var names = new List<string>();
            for (var i = 0; i < count; i++)
            {
                if (TabName(found, i) is { Length: > 0 } name)
                {
                    names.Add(name);
                }
            }

            return names.Count > 0 ? names : null;
        }
        finally
        {
            Release(found);
            Release(condition);
            Release(element);
            Release(automation);
        }
    }

    private static string? TabName(nint elements, int index)
    {
        nint tab = 0, name = 0;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Method(elements, 4))(elements, index, &tab) < 0 // GetElement
                || tab == 0
                || ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Method(tab, 23))(tab, &name) < 0) // get_CurrentName
            {
                return null;
            }

            return Marshal.PtrToStringBSTR(name);
        }
        finally
        {
            Marshal.FreeBSTR(name);
            Release(tab);
        }
    }

    private static void* Method(nint comObject, int slot) => (*(void***)comObject)[slot];

    private static void Release(nint comObject)
    {
        if (comObject != 0)
        {
            Marshal.Release(comObject);
        }
    }
}
