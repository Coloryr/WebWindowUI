using System.Runtime.InteropServices;

namespace WebWindowUI.Cef;

/// <summary>cef_base_ref_counted_t 前缀（所有 ref-counted 结构体的头 5 个指针）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefRefBase
{
    public ulong Size;
    public IntPtr AddRef;
    public IntPtr Release;
    public IntPtr HasOneRef;
    public IntPtr HasAtLeastOneRef;
}

/// <summary>cef_base_scoped_t 前缀（scoped 结构体：scheme_registrar 等）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefScopedBase
{
    public ulong Size;
    public IntPtr Del;
}

// 每个结构体的方法槽 = 钉版 capi 头文件的 CEF_CALLBACK 全序。未用槽也占位（顺序即偏移）。

/// <summary>cef_app_t：base + 5 方法（capi/cef_app_capi.h:66）。客户端分配。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefAppPtr
{
    public CefRefBase Base;
    public IntPtr OnBeforeCommandLineProcessing;
    public IntPtr OnRegisterCustomSchemes;
    public IntPtr GetResourceBundleHandler;
    public IntPtr GetBrowserProcessHandler;
    public IntPtr GetRenderProcessHandler;
}

/// <summary>cef_scheme_registrar_t：scoped base + 1 方法。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefSchemeRegistrarPtr
{
    public CefScopedBase Base;
    public IntPtr AddCustomScheme;
}

/// <summary>cef_scheme_handler_factory_t：base + create。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefSchemeHandlerFactoryPtr
{
    public CefRefBase Base;
    public IntPtr Create;
}

/// <summary>cef_callback_t：base + cont/cancel。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefCallbackPtr
{
    public CefRefBase Base;
    public IntPtr Cont;
    public IntPtr Cancel;
}

/// <summary>cef_resource_handler_t：base + 7 方法。同步路径实现 open→get_response_headers→read。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefResourceHandlerPtr
{
    public CefRefBase Base;
    public IntPtr Open;
    public IntPtr ProcessRequest;
    public IntPtr GetResponseHeaders;
    public IntPtr Skip;
    public IntPtr Read;
    public IntPtr ReadResponse;
    public IntPtr Cancel;
}

/// <summary>cef_client_t：base + 19 方法（capi/cef_client_capi.h:77）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefClientPtr
{
    public CefRefBase Base;
    public IntPtr GetAudioHandler;
    public IntPtr GetCommandHandler;
    public IntPtr GetContextMenuHandler;
    public IntPtr GetDialogHandler;
    public IntPtr GetDisplayHandler;
    public IntPtr GetDownloadHandler;
    public IntPtr GetDragHandler;
    public IntPtr GetFindHandler;
    public IntPtr GetFocusHandler;
    public IntPtr GetFrameHandler;
    public IntPtr GetPermissionHandler;
    public IntPtr GetJsdialogHandler;
    public IntPtr GetKeyboardHandler;
    public IntPtr GetLifeSpanHandler;
    public IntPtr GetLoadHandler;
    public IntPtr GetPrintHandler;
    public IntPtr GetRenderHandler;
    public IntPtr GetRequestHandler;
    public IntPtr OnProcessMessageReceived;
}

/// <summary>cef_life_span_handler_t：base + 6 方法。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefLifeSpanHandlerPtr
{
    public CefRefBase Base;
    public IntPtr OnBeforePopup;
    public IntPtr OnBeforePopupAborted;
    public IntPtr OnBeforeDevToolsPopup;
    public IntPtr OnAfterCreated;
    public IntPtr DoClose;
    public IntPtr OnBeforeClose;
}

/// <summary>cef_load_handler_t：base + 4 方法。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefLoadHandlerPtr
{
    public CefRefBase Base;
    public IntPtr OnLoadingStateChange;
    public IntPtr OnLoadStart;
    public IntPtr OnLoadEnd;
    public IntPtr OnLoadError;
}

/// <summary>cef_browser_t：base + 21 方法（capi/cef_browser_capi.h:71）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefBrowserPtr
{
    public CefRefBase Base;
    public IntPtr IsValid;
    public IntPtr GetHost;
    public IntPtr CanGoBack;
    public IntPtr GoBack;
    public IntPtr CanGoForward;
    public IntPtr GoForward;
    public IntPtr IsLoading;
    public IntPtr Reload;
    public IntPtr ReloadIgnoreCache;
    public IntPtr StopLoad;
    public IntPtr GetIdentifier;
    public IntPtr IsSame;
    public IntPtr IsPopup;
    public IntPtr HasDocument;
    public IntPtr GetMainFrame;
    public IntPtr GetFocusedFrame;
    public IntPtr GetFrameByIdentifier;
    public IntPtr GetFrameByName;
    public IntPtr GetFrameCount;
    public IntPtr GetFrameIdentifiers;
    public IntPtr GetFrameNames;
}

/// <summary>cef_browser_host_t：base + 69 方法（capi/cef_browser_capi.h:307）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefBrowserHostPtr
{
    public CefRefBase Base;
    public IntPtr GetBrowser;
    public IntPtr CloseBrowser;
    public IntPtr TryCloseBrowser;
    public IntPtr IsReadyToBeClosed;
    public IntPtr SetFocus;
    public IntPtr GetWindowHandle;
    public IntPtr GetOpenerWindowHandle;
    public IntPtr GetOpenerIdentifier;
    public IntPtr HasView;
    public IntPtr GetClient;
    public IntPtr GetRequestContext;
    public IntPtr CanZoom;
    public IntPtr Zoom;
    public IntPtr GetDefaultZoomLevel;
    public IntPtr GetZoomLevel;
    public IntPtr SetZoomLevel;
    public IntPtr RunFileDialog;
    public IntPtr StartDownload;
    public IntPtr DownloadImage;
    public IntPtr Print;
    public IntPtr PrintToPdf;
    public IntPtr Find;
    public IntPtr StopFinding;
    public IntPtr ShowDevTools;
    public IntPtr CloseDevTools;
    public IntPtr HasDevTools;
    public IntPtr SendDevToolsMessage;
    public IntPtr ExecuteDevToolsMethod;
    public IntPtr AddDevToolsMessageObserver;
    public IntPtr GetNavigationEntries;
    public IntPtr ReplaceMisspelling;
    public IntPtr AddWordToDictionary;
    public IntPtr IsWindowRenderingDisabled;
    public IntPtr WasResized;
    public IntPtr WasHidden;
    public IntPtr NotifyScreenInfoChanged;
    public IntPtr Invalidate;
    public IntPtr SendExternalBeginFrame;
    public IntPtr SendKeyEvent;
    public IntPtr SendMouseClickEvent;
    public IntPtr SendMouseMoveEvent;
    public IntPtr SendMouseWheelEvent;
    public IntPtr SendTouchEvent;
    public IntPtr SendCaptureLostEvent;
    public IntPtr NotifyMoveOrResizeStarted;
    public IntPtr GetWindowlessFrameRate;
    public IntPtr SetWindowlessFrameRate;
    public IntPtr ImeSetComposition;
    public IntPtr ImeCommitText;
    public IntPtr ImeFinishComposingText;
    public IntPtr ImeCancelComposition;
    public IntPtr DragTargetDragEnter;
    public IntPtr DragTargetDragOver;
    public IntPtr DragTargetDragLeave;
    public IntPtr DragTargetDrop;
    public IntPtr DragSourceEndedAt;
    public IntPtr DragSourceSystemDragEnded;
    public IntPtr GetVisibleNavigationEntry;
    public IntPtr SetAccessibilityState;
    public IntPtr SetAutoResizeEnabled;
    public IntPtr SetAudioMuted;
    public IntPtr IsAudioMuted;
    public IntPtr IsFullscreen;
    public IntPtr ExitFullscreen;
    public IntPtr CanExecuteChromeCommand;
    public IntPtr ExecuteChromeCommand;
    public IntPtr IsRenderProcessUnresponsive;
    public IntPtr GetRuntimeStyle;
    public IntPtr SetAxViewportCollapse;
}

/// <summary>cef_frame_t：base + 26 方法（capi/cef_frame_capi.h:71）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefFramePtr
{
    public CefRefBase Base;
    public IntPtr IsValid;
    public IntPtr Undo;
    public IntPtr Redo;
    public IntPtr Cut;
    public IntPtr Copy;
    public IntPtr Paste;
    public IntPtr PasteAndMatchStyle;
    public IntPtr Del;
    public IntPtr SelectAll;
    public IntPtr ViewSource;
    public IntPtr GetSource;
    public IntPtr GetText;
    public IntPtr LoadRequest;
    public IntPtr LoadUrl;
    public IntPtr ExecuteJavaScript;
    public IntPtr IsMain;
    public IntPtr IsFocused;
    public IntPtr GetName;
    public IntPtr GetIdentifier;
    public IntPtr GetParent;
    public IntPtr GetUrl;
    public IntPtr GetBrowser;
    public IntPtr GetV8Context;
    public IntPtr VisitDom;
    public IntPtr CreateUrlRequest;
    public IntPtr SendProcessMessage;
}

/// <summary>cef_request_t：base + 22 方法（capi/cef_request_capi.h:62）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefRequestPtr
{
    public CefRefBase Base;
    public IntPtr IsReadOnly;
    public IntPtr GetUrl;
    public IntPtr SetUrl;
    public IntPtr GetMethod;
    public IntPtr SetMethod;
    public IntPtr SetReferrer;
    public IntPtr GetReferrerUrl;
    public IntPtr GetReferrerPolicy;
    public IntPtr GetPostData;
    public IntPtr SetPostData;
    public IntPtr GetHeaderMap;
    public IntPtr SetHeaderMap;
    public IntPtr GetHeaderByName;
    public IntPtr SetHeaderByName;
    public IntPtr Set;
    public IntPtr GetFlags;
    public IntPtr SetFlags;
    public IntPtr GetFirstPartyForCookies;
    public IntPtr SetFirstPartyForCookies;
    public IntPtr GetResourceType;
    public IntPtr GetTransitionType;
    public IntPtr GetIdentifier;
}

/// <summary>cef_response_t：base + 17 方法（capi/cef_response_capi.h:59）。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefResponsePtr
{
    public CefRefBase Base;
    public IntPtr IsReadOnly;
    public IntPtr GetError;
    public IntPtr SetError;
    public IntPtr GetStatus;
    public IntPtr SetStatus;
    public IntPtr GetStatusText;
    public IntPtr SetStatusText;
    public IntPtr GetMimeType;
    public IntPtr SetMimeType;
    public IntPtr GetCharset;
    public IntPtr SetCharset;
    public IntPtr GetHeaderByName;
    public IntPtr SetHeaderByName;
    public IntPtr GetHeaderMap;
    public IntPtr SetHeaderMap;
    public IntPtr GetUrl;
    public IntPtr SetUrl;
}

/// <summary>cef_post_data_t：base + 7 方法。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefPostDataPtr
{
    public CefRefBase Base;
    public IntPtr IsReadOnly;
    public IntPtr HasExcludedElements;
    public IntPtr GetElementCount;
    public IntPtr GetElements;
    public IntPtr RemoveElement;
    public IntPtr AddElement;
    public IntPtr RemoveElements;
}

/// <summary>cef_post_data_element_t：base + 8 方法。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CefPostDataElementPtr
{
    public CefRefBase Base;
    public IntPtr IsReadOnly;
    public IntPtr SetToEmpty;
    public IntPtr SetToFile;
    public IntPtr SetToBytes;
    public IntPtr GetType;
    public IntPtr GetFile;
    public IntPtr GetBytesCount;
    public IntPtr GetBytes;
}
