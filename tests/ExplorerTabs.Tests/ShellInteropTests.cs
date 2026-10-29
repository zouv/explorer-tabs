using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using ExplorerTabs.Core;

namespace ExplorerTabs.Tests
{
    internal static partial class Program
    {
        private static void ShellInteropTests()
        {
            Check(SourceWindowMatch.IsExact(10, 10, 0, 0, false), "source matching can use exact automation HWND when no view/browser is available");
            Check(!SourceWindowMatch.IsExact(10, 99, 99, 0, false), "shared host/root is not a source match");
            Check(SourceWindowMatch.IsExact(10, 99, 99, 20, true), "embedded source matches its own descendant Shell view even when automation reports host");
            Check(!SourceWindowMatch.IsExact(10, 10, 10, 20, false), "unrelated view vetoes stale automation/browser match");
            Check(!SourceWindowMatch.IsExact(10, 99, 99, 30, false), "another tab under same host cannot be copied");
            Check(!SourceWindowMatch.IsExact(0, 0, 0, 0, true), "zero source is never a match");
            Check(SourceWindowMatch.IsExact(10, 99, 10, 0, false), "exact browser HWND is usable without active view");
            Check(!SourceWindowMatch.IsExact(10, 10, 99, 0, false), "available unrelated browser vetoes automation match");
            Check(SourceWindowMatch.IsExact(10, 99, 99, 10, false), "exact Shell-view HWND is accepted");

            var assembly = Assembly.Load("ExplorerTabs");
            var type = assembly.GetType("ExplorerTabs.App.IWebBrowser2Native", true)!;
            var methods = type.GetMethods();
            Check(type.GetCustomAttribute<InterfaceTypeAttribute>()!.Value == ComInterfaceType.InterfaceIsDual,
                "browser interop uses native dual-interface vtable, not a partial IDispatch definition");
            Check(type.GUID == new Guid("D30C1661-CDAF-11D0-8A3E-00C04FC9E26E") && methods.Length == 64,
                "SDK browser IID and all inherited native slots are preserved");
            Check(methods[23].Name == "GetLocationURL" && methods[24].Name == "GetBusy" && methods[30].Name == "GetHWND" &&
                methods[34].Name == "SetVisible" && methods[45].Name == "Navigate2", "SDK browser property and navigation vtable slots are exact");
            Check(methods[30].ReturnType == typeof(long), "x64 browser HWND remains SHANDLE_PTR, not truncated int32");
            var parameters = methods[45].GetParameters();
            Check(parameters.Length == 5 && parameters.All(p => p.ParameterType == typeof(object).MakeByRefType() && p.IsIn &&
                p.GetCustomAttribute<MarshalAsAttribute>()!.Value == UnmanagedType.Struct), "Navigate2 receives five native VARIANT pointers");
            Check(!parameters[0].IsOptional && parameters.Skip(1).All(p => p.IsOptional), "Navigate2 optional metadata matches SDK");
            Check(assembly.GetType("ExplorerTabs.App.MainForm", true)!.GetMethod("ConfirmEmbedding", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                "repeated experimental-confirmation dialog entry point removed from all creation/join routes");

            // A raw, in-process native vtable stub, NOT a fake managed backend or real Explorer.
            // This exercises the actual CLR COM marshaller and SHParseDisplayName/ILGetSize PIDL path.
            var logs = new List<string>();
            using (var native = new NativeBrowserStub())
            {
                var browser = OpenBrowserStub(assembly, native, logs.Add);
                try
                {
                    Check(browser.Handle == 0x1234 && !browser.Busy && browser.LocationUrl == "file:///C:/stub",
                        "native browser getters exercise real COM vtable/marshalling without touching Explorer");
                    browser.Navigate(Path.GetTempPath(), CancellationToken.None);
                    Check(native.NavigateCalls == 1 && native.InvokeCalls == 0, "PIDL navigation calls native Navigate2 slot directly; no late-bound Invoke");
                    Check(native.UrlVariantType == 0x2011 && native.Pidl != null && native.Pidl.Length >= 2 &&
                        native.Pidl[native.Pidl.Length - 1] == 0 && native.Pidl[native.Pidl.Length - 2] == 0,
                        "native Navigate2 receives VT_ARRAY|VT_UI1 with complete absolute PIDL terminator");
                    Check(native.OptionalVariantTypes.SequenceEqual(new short[] { 0, 0, 0, 0 }), "optional navigation arguments are VT_EMPTY, not Type.Missing error variants");
                    Check(logs.Any(l => l.Contains("ABI=vtable")) && logs.Any(l => l.Contains("Navigate2 returned")), "navigation logs record ABI, argument types and completion stage");
                    browser.Show();
                    Check(native.VisibleValue == -1, "Visible setter marshals VARIANT_TRUE as -1 to native stub");
                    var browserRcw = browser.GetType().GetField("browser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(browser)!;
                    type.GetMethod("SetVisible")!.Invoke(browserRcw, new object[] { false });
                    Check(native.VisibleValue == 0 && native.InvokeCalls == 0, "deferred Visible=false marshals VARIANT_FALSE through native vtable without IDispatch");
                    var cancelled = new CancellationToken(true);
                    Check(Throws(() => browser.Navigate(Path.GetTempPath(), cancelled)) is OperationCanceledException && native.NavigateCalls == 1,
                        "pre-cancelled native navigation does not parse or invoke COM");
                    native.Result = unchecked((int)0x80070057);
                    Check(Throws(() => browser.Navigate(Path.GetTempPath(), CancellationToken.None)).HResult == unchecked((int)0x80070057),
                        "native failure HRESULT reaches the caller rather than being claimed successful");
                }
                finally { browser.Dispose(); }
                Check(native.References == 1, "disposing created-browser reference releases native COM ownership without Quit");
            }
        }

        private static ICreatedExplorerReference OpenBrowserStub(Assembly assembly, NativeBrowserStub native, Action<string> trace)
        {
            object rcw = Marshal.GetObjectForIUnknown(native.Pointer);
            var type = assembly.GetType("ExplorerTabs.App.ShellExplorerBackend+BrowserReference", true)!;
            try { return (ICreatedExplorerReference)Activator.CreateInstance(type, rcw, rcw, trace)!; }
            catch { Marshal.ReleaseComObject(rcw); throw; }
        }

        private sealed class NativeBrowserStub : IDisposable
        {
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Query(IntPtr self, ref Guid iid, out IntPtr pointer);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint RefCount(IntPtr self);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int TypeCount(IntPtr self, out uint count);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int TypeInfo(IntPtr self, uint index, uint locale, out IntPtr result);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Names(IntPtr self, ref Guid iid, IntPtr names, uint count, uint locale, IntPtr result);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Invoke(IntPtr self, int id, ref Guid iid, uint locale, ushort flags, IntPtr args, IntPtr result, IntPtr error, IntPtr argument);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetHandle(IntPtr self, out long value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetBool(IntPtr self, out short value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetString(IntPtr self, out IntPtr value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetBool(IntPtr self, short value);
            [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Navigate(IntPtr self, IntPtr url, IntPtr flags, IntPtr target, IntPtr post, IntPtr headers);
            private readonly List<Delegate> callbacks = new List<Delegate>();
            private readonly IntPtr vtable;
            public IntPtr Pointer { get; }
            public int References { get; private set; } = 1;
            public int NavigateCalls { get; private set; }
            public int InvokeCalls { get; private set; }
            public int Result { get; set; }
            public short UrlVariantType { get; private set; }
            public short[] OptionalVariantTypes { get; private set; } = Array.Empty<short>();
            public byte[]? Pidl { get; private set; }
            public short VisibleValue { get; private set; }
            private const int NotImplemented = unchecked((int)0x80004001);
            public NativeBrowserStub()
            {
                vtable = Marshal.AllocHGlobal(71 * IntPtr.Size);
                Pointer = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(Pointer, vtable);
                for (int i = 0; i < 71; i++) Marshal.WriteIntPtr(vtable, i * IntPtr.Size, IntPtr.Zero);
                Slot(0, new Query((IntPtr self, ref Guid iid, out IntPtr result) =>
                {
                    bool supported = iid == new Guid("00000000-0000-0000-C000-000000000046") ||
                        iid == new Guid("00020400-0000-0000-C000-000000000046") ||
                        iid == new Guid("D30C1661-CDAF-11D0-8A3E-00C04FC9E26E");
                    result = supported ? Pointer : IntPtr.Zero;
                    if (supported) { References++; return 0; }
                    return unchecked((int)0x80004002);
                }));
                Slot(1, new RefCount(_ => (uint)++References));
                Slot(2, new RefCount(_ => (uint)--References));
                Slot(3, new TypeCount((IntPtr self, out uint count) => { count = 0; return 0; }));
                Slot(4, new TypeInfo((IntPtr self, uint index, uint locale, out IntPtr result) => { result = IntPtr.Zero; return NotImplemented; }));
                Slot(5, new Names((IntPtr self, ref Guid iid, IntPtr names, uint count, uint locale, IntPtr result) => NotImplemented));
                Slot(6, new Invoke((IntPtr self, int id, ref Guid iid, uint locale, ushort flags, IntPtr args, IntPtr result, IntPtr error, IntPtr argument) => { InvokeCalls++; return NotImplemented; }));
                Slot(30, new GetString((IntPtr self, out IntPtr result) => { result = Marshal.StringToBSTR("file:///C:/stub"); return 0; }));
                Slot(31, new GetBool((IntPtr self, out short result) => { result = 0; return 0; }));
                Slot(37, new GetHandle((IntPtr self, out long result) => { result = 0x1234; return 0; }));
                Slot(41, new SetBool((self, value) => { VisibleValue = value; return 0; }));
                Slot(52, new Navigate((self, url, flags, target, post, headers) =>
                {
                    NavigateCalls++; UrlVariantType = Marshal.ReadInt16(url);
                    Pidl = Marshal.GetObjectForNativeVariant(url) as byte[];
                    OptionalVariantTypes = new[] { flags, target, post, headers }.Select(p => p == IntPtr.Zero ? (short)-1 : Marshal.ReadInt16(p)).ToArray();
                    return Result;
                }));
            }
            private void Slot(int index, Delegate callback)
            { callbacks.Add(callback); Marshal.WriteIntPtr(vtable, index * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(callback)); }
            public void Dispose()
            {
                if (References != 1) throw new InvalidOperationException("Native browser test stub still has outstanding COM references: " + References);
                Marshal.FreeHGlobal(Pointer); Marshal.FreeHGlobal(vtable); GC.KeepAlive(callbacks);
            }
        }
    }
}
