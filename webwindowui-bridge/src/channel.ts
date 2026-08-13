/**
 * 平台自适应消息通道：WebView2（chrome.webview）/ WebKitGTK、WKWebView（webkit.messageHandlers.wwui）/
 * CEF（同源 app://<host>/__wwui 的 fetch POST）。所有平台共用同一 NUL 转义字符串载荷（见 codec）。
 */

const HANDLER_NAME = 'wwui'

/** 下行（native → JS）回调：WebKit/CEF 经 window.wwuiReceive 调用；WebView2 走 chrome.webview 'message' 事件。 */
let receiveHandler: ((data: string) => void) | undefined
;(window as unknown as Record<string, unknown>).wwuiReceive = (raw: unknown): void => {
  if (typeof raw === 'string' && receiveHandler) receiveHandler(raw)
}

/** WebView2 下行事件每页只挂一次。 */
let webView2ReceiveWired = false

/** 注册下行回调。 */
export function onReceive(handler: (data: string) => void): void {
  receiveHandler = handler
  const chrome = (window as unknown as {
    chrome?: { webview?: { addEventListener(type: 'message', listener: (event: MessageEvent) => void): void } }
  }).chrome?.webview
  if (chrome?.addEventListener && !webView2ReceiveWired) {
    webView2ReceiveWired = true
    chrome.addEventListener('message', (event) => {
      if (typeof event.data === 'string') receiveHandler?.(event.data)
    })
  }
}

/**
 * 发送载荷，返回是否发出（纯浏览器调试场景无通道 → false）。
 * 发送通道每次重新探测：WebKitGTK 把 script message handler 同步进 web 进程有延迟，模块作用域一次性
 * 解析并缓存会永远拿到 null（Ready 不发、命令发不出）。CEF 无 chrome.webview/webkit，走同源 fetch POST。
 */
export function postMessage(data: string): boolean {
  const chrome = (window as unknown as {
    chrome?: { webview?: { postMessage(data: string): void } }
  }).chrome?.webview
  if (chrome?.postMessage) {
    chrome.postMessage(data)
    return true
  }

  const webkit = (window as unknown as {
    webkit?: { messageHandlers?: Record<string, { postMessage(data: string): void }> }
  }).webkit
  const handler = webkit?.messageHandlers?.[HANDLER_NAME]
  if (handler?.postMessage) {
    handler.postMessage(data)
    return true
  }

  if (typeof fetch === 'function') {
    void fetch(`${location.origin}/__wwui`, { method: 'POST', body: data }).catch(() => {})
    return true
  }

  return false
}
