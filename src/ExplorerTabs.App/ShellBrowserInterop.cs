using System;
using System.Runtime.InteropServices;

namespace ExplorerTabs.App
{
    // Flattened IWebBrowser2 ABI from Windows SDK ExDisp.idl / SHDocVw type library.
    // Do not remove/reorder unused inherited methods: native vtable slots must stay exact.
    // The former partial IDispatch declaration routed PIDL navigation through Invoke.
    [ComImport, Guid("D30C1661-CDAF-11D0-8A3E-00C04FC9E26E"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface IWebBrowser2Native
    {
        void GoBack();
        void GoForward();
        void GoHome();
        void GoSearch();
        void Navigate([In, MarshalAs(UnmanagedType.BStr)] string url, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object Flags, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object TargetFrameName, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object PostData, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object Headers);
        void Refresh();
        void Refresh2([In, Optional, MarshalAs(UnmanagedType.Struct)] ref object Level);
        void Stop();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object GetApplication();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object GetParent();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object GetContainer();
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object GetDocument();
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetTopLevelContainer();
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetType();
        int GetLeft();
        void SetLeft([In] int pl);
        int GetTop();
        void SetTop([In] int pl);
        int GetWidth();
        void SetWidth([In] int pl);
        int GetHeight();
        void SetHeight([In] int pl);
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetLocationName();
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetLocationURL();
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetBusy();
        void Quit();
        void ClientToWindow([In, Out] ref int pcx, [In, Out] ref int pcy);
        void PutProperty([In, MarshalAs(UnmanagedType.BStr)] string Property, [In, MarshalAs(UnmanagedType.Struct)] object vtValue);
        [return: MarshalAs(UnmanagedType.Struct)]
        object GetProperty([In, MarshalAs(UnmanagedType.BStr)] string Property);
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetName();
        long GetHWND();
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetFullName();
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetPath();
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetVisible();
        void SetVisible([In, MarshalAs(UnmanagedType.VariantBool)] bool pBool);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetStatusBar();
        void SetStatusBar([In, MarshalAs(UnmanagedType.VariantBool)] bool pBool);
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetStatusText();
        void SetStatusText([In, MarshalAs(UnmanagedType.BStr)] string StatusText);
        int GetToolBar();
        void SetToolBar([In] int Value);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetMenuBar();
        void SetMenuBar([In, MarshalAs(UnmanagedType.VariantBool)] bool Value);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetFullScreen();
        void SetFullScreen([In, MarshalAs(UnmanagedType.VariantBool)] bool pbFullScreen);
        void Navigate2([In, MarshalAs(UnmanagedType.Struct)] ref object url, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object Flags, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object TargetFrameName, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object PostData, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object Headers);
        int QueryStatusWB([In] int cmdID);
        void ExecWB([In] int cmdID, [In] int cmdexecopt, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object pvaIn, [In, Out, Optional, MarshalAs(UnmanagedType.Struct)] ref object pvaOut);
        void ShowBrowserBar([In, MarshalAs(UnmanagedType.Struct)] ref object pvaClsid, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object pvarShow, [In, Optional, MarshalAs(UnmanagedType.Struct)] ref object pvarSize);
        int GetReadyState();
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetOffline();
        void SetOffline([In, MarshalAs(UnmanagedType.VariantBool)] bool pbOffline);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetSilent();
        void SetSilent([In, MarshalAs(UnmanagedType.VariantBool)] bool pbSilent);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetRegisterAsBrowser();
        void SetRegisterAsBrowser([In, MarshalAs(UnmanagedType.VariantBool)] bool pbRegister);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetRegisterAsDropTarget();
        void SetRegisterAsDropTarget([In, MarshalAs(UnmanagedType.VariantBool)] bool pbRegister);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetTheaterMode();
        void SetTheaterMode([In, MarshalAs(UnmanagedType.VariantBool)] bool pbRegister);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetAddressBar();
        void SetAddressBar([In, MarshalAs(UnmanagedType.VariantBool)] bool Value);
        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool GetResizable();
        void SetResizable([In, MarshalAs(UnmanagedType.VariantBool)] bool Value);
    }
}
