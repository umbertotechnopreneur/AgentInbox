/* VBWR B
 *
 * Project: AgentInbox
 * Repository: https://github.com/umbertotechnopreneur/AgentInbox
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 *
 * VibeWare: Human intent, AI execution, and plenty of tokens
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 *
 * File: Windows notification area icon, native menu and activity notifications.
 * Created with AI: OpenAI Codex, 2026-11-10.
 * Human guidance: Umberto Giacobbi; match the tray menu on Windows and Linux.
 *
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT - see LICENSE
 * SPDX-License-Identifier: MIT
 *
 * VBWR E */

using System.Runtime.InteropServices;

namespace MailMeUp.Desktop.Views;

/// <summary>Bind the Win32 notification area to configuration actions.</summary>
internal sealed class NotificationArea : IDisposable
{
    private const uint Callback = 0x8001;
    private const uint Add = 0;
    private const uint Modify = 1;
    private const uint Delete = 2;
    private const uint SetVersion = 4;
    private const uint Message = 1;
    private const uint Icon = 2;
    private const uint Tip = 4;
    private const uint Info = 16;
    private const uint ShowTip = 128;
    private readonly nint _window;
    private nint _icon;
    private readonly uint _taskbarCreated;
    private readonly SubclassProcedure _subclass;
    private readonly (string Text, Action Invoke)[] _actions;
    private bool _attached;
    private string _status = "MCP status unavailable";

    internal bool Available { get; private set; }

    /// <summary>Load the bundled icon and attach the Shell callback to the owning window.</summary>
    /// <param name="window">Native handle of the WinUI configuration window.</param>
    /// <param name="iconPath">Absolute path to the bundled AgentInbox icon.</param>
    /// <param name="actions">Menu labels and handlers in display order, starting with Open AgentInbox.</param>
    internal NotificationArea(nint window, string iconPath, (string Text, Action Invoke)[] actions)
    {
        _window = window;
        _actions = actions;
        _subclass = HandleMessage;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _icon = LoadImage(0, iconPath, 1, 0, 0, 0x0050);
        _attached = _icon != 0 && SetWindowSubclass(_window, _subclass, 1, 0);
        if (_attached) Register();
    }

    /// <summary>Update the tooltip and optionally request one native notification.</summary>
    /// <param name="status">Safe connection and mailbox status shown in the tooltip and menu.</param>
    /// <param name="notification">Optional transition summary displayed by the Windows shell.</param>
    internal void Update(string status, string? notification)
    {
        _status = status;
        if (!Available) return;
        var data = MakeData(Tip | ShowTip);
        if (notification is not null)
        {
            data.Flags |= Info;
            data.InfoTitle = "AgentInbox";
            data.InfoText = notification;
            data.InfoFlags = 1;
        }
        ShellNotifyIcon(Modify, ref data);
    }

    /// <summary>Register the icon or restore it after Explorer recreates the taskbar.</summary>
    private void Register()
    {
        var data = MakeData(Message | Icon | Tip | ShowTip);
        Available = ShellNotifyIcon(Add, ref data);
        if (Available)
        {
            data.Version = 4;
            if (!ShellNotifyIcon(SetVersion, ref data))
            {
                ShellNotifyIcon(Delete, ref data);
                Available = false;
            }
        }
    }

    /// <summary>Build a Unicode notification payload for this icon.</summary>
    /// <param name="flags">Fields the shell should read for this update.</param>
    private IconData MakeData(uint flags) => new()
    {
        Size = Marshal.SizeOf<IconData>(), Window = _window, Id = 1, Flags = flags, Callback = Callback,
        Icon = _icon, Tip = "AgentInbox · " + _status, State = 0, StateMask = 0, InfoText = "", Version = 0,
        InfoTitle = "", InfoFlags = 0, ItemGuid = Guid.Empty, BalloonIcon = 0,
    };

    /// <summary>Forward taskbar callbacks and leave other native messages with WinUI.</summary>
    /// <param name="window">Window handle receiving the native message.</param>
    /// <param name="message">Win32 message ID.</param>
    /// <param name="wParam">Message data, including tray coordinates for version 4 callbacks.</param>
    /// <param name="lParam">Message data, including the tray event in its low word.</param>
    /// <param name="subclassId">Unique subclass ID for this window.</param>
    /// <param name="referenceData">Optional application data registered with the subclass.</param>
    private nint HandleMessage(nint window, uint message, nuint wParam, nint lParam, nuint subclassId, nuint referenceData)
    {
        if (message == _taskbarCreated)
            Register();
        else if (message == Callback)
        {
            switch ((uint)lParam & 0xffff)
            {
                case 0x007b: ShowMenu(wParam); break; // WM_CONTEXTMENU
                case 0x0400: // NIN_SELECT
                case 0x0401: // NIN_KEYSELECT
                case 0x0405: _actions[0].Invoke(); break; // NIN_BALLOONUSERCLICK
            }
            return 0;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    /// <summary>Open the shell context menu and dispatch a chosen configuration action.</summary>
    /// <param name="position">Packed signed screen coordinates from the tray callback.</param>
    private void ShowMenu(nuint position)
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        uint selected;
        try
        {
            AppendMenu(menu, 1, 0, _status);
            for (var index = 0; index < _actions.Length; index++)
                AppendMenu(menu, 0, (nuint)index + 1, _actions[index].Text);
            SetForegroundWindow(_window);
            selected = TrackPopupMenuEx(menu, 0x0102, (short)(position & 0xffff),
                (short)((position >> 16) & 0xffff), _window, 0);
            PostMessage(_window, 0, 0, 0);
        }
        finally { DestroyMenu(menu); }
        if (selected > 0 && selected <= _actions.Length)
            _actions[(int)selected - 1].Invoke();
    }

    /// <summary>Release the shell icon, window subclass and owned native icon.</summary>
    public void Dispose()
    {
        if (Available)
        {
            var data = MakeData(0);
            ShellNotifyIcon(Delete, ref data);
            Available = false;
        }
        if (_attached)
        {
            RemoveWindowSubclass(_window, _subclass, 1);
            _attached = false;
        }
        if (_icon != 0)
        {
            DestroyIcon(_icon);
            _icon = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public int Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string InfoText;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam,
        nuint subclassId, nuint referenceData);

    /// <summary>Add, update or delete an icon in the Windows notification area.</summary>
    /// <param name="operation">Shell notification-area operation ID.</param>
    /// <param name="data">Complete native notification icon record.</param>
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint operation, ref IconData data);

    /// <summary>Load an icon from a bundled file.</summary>
    /// <param name="instance">Zero when the image is loaded from a file.</param>
    /// <param name="name">Absolute icon filename.</param>
    /// <param name="type">Native image type, IMAGE_ICON for this caller.</param>
    /// <param name="width">Desired width in pixels, or zero for the system default.</param>
    /// <param name="height">Desired height in pixels, or zero for the system default.</param>
    /// <param name="flags">Flags selecting a file and default icon dimensions.</param>
    [DllImport("user32.dll", EntryPoint = "LoadImageW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);

    /// <summary>Free an icon owned by this view.</summary>
    /// <param name="icon">Native handle returned by LoadImage.</param>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    /// <summary>Look up the taskbar-recreated broadcast message.</summary>
    /// <param name="name">Windows message name registered by Explorer.</param>
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    /// <summary>Register a callback for the owning window.</summary>
    /// <param name="window">Native WinUI window handle.</param>
    /// <param name="procedure">Delegate retained for the lifetime of the registration.</param>
    /// <param name="id">Subclass identifier for this procedure.</param>
    /// <param name="referenceData">Optional application data, unused by this view.</param>
    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure procedure, nuint id, nuint referenceData);

    /// <summary>Remove the callback from the owning window.</summary>
    /// <param name="window">Native WinUI window handle.</param>
    /// <param name="procedure">Previously registered delegate.</param>
    /// <param name="id">Subclass identifier used during registration.</param>
    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure procedure, nuint id);

    /// <summary>Forward a message through the rest of the native subclass chain.</summary>
    /// <param name="window">Native window receiving the message.</param>
    /// <param name="message">Message ID forwarded unchanged.</param>
    /// <param name="wParam">First native message argument.</param>
    /// <param name="lParam">Second native message argument.</param>
    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    /// <summary>Create a native popup menu.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint CreatePopupMenu();

    /// <summary>Add a status or action row to the native popup menu.</summary>
    /// <param name="menu">Handle of the current popup menu.</param>
    /// <param name="flags">Native menu item state flags.</param>
    /// <param name="id">Menu command ID, or zero for the disabled status row.</param>
    /// <param name="text">Plain display label.</param>
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string text);

    /// <summary>Display the popup at the tray event and return its selected command ID.</summary>
    /// <param name="menu">Native popup menu handle.</param>
    /// <param name="flags">Flags for returning the command and accepting right-clicks.</param>
    /// <param name="x">Horizontal screen coordinate in pixels.</param>
    /// <param name="y">Vertical screen coordinate in pixels.</param>
    /// <param name="window">Native owner window.</param>
    /// <param name="parameters">Optional extended placement parameters, unused here.</param>
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);

    /// <summary>Destroy the popup and its native items.</summary>
    /// <param name="menu">Owned popup menu handle.</param>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(nint menu);

    /// <summary>Make the menu's owning window eligible to receive a popup dismissal.</summary>
    /// <param name="window">Native owner window.</param>
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    /// <summary>Post the required follow-up message after a tray popup closes.</summary>
    /// <param name="window">Native owner window.</param>
    /// <param name="message">Message to enqueue, WM_NULL for this caller.</param>
    /// <param name="wParam">First native message argument, unused here.</param>
    /// <param name="lParam">Second native message argument, unused here.</param>
    [DllImport("user32.dll", EntryPoint = "PostMessageW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
}
