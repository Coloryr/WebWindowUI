import { reactive, watch, nextTick } from 'vue'
import * as protobuf from 'protobufjs'

/**
 * .NET Model ↔ Vue 双向绑定桥（protobuf 版）。
 *
 * 传输：所有 postMessage 载荷都是 NUL 转义的 Latin-1 字符串（各平台共用同一 codec）。
 *   WebView2 的字符串消息通道会在第一个 NUL（char code 0）处截断字符串，
 *   protobuf 字节普遍含 0x00（varint 零值、double 的 fixed64、长度前缀等），
 *   因此原始 Latin-1 字节串无法无损通过该通道（WebKitGTK/WKWebView 一并统一走同一转义）。
 *   本桥对字节做 1:1 Latin-1 映射，仅把 0x00 → '\0'、0x5C（转义符自身）→ '\\' 转义，
 *   其余字节原样一个字符（无 NUL 载荷零膨胀，NUL 多的载荷每个 NUL 只多 1 字符）。
 *   .NET → JS：bytes → 转义串 → 平台通道下发；本桥逆操作还原回 Uint8Array 再 protobufjs 解码。
 *   JS → .NET：protobufjs 编码 → Uint8Array → 转义串 → 平台通道 postMessage。
 *
 * 消息（webwindowui.model.WebMessage 信封，oneof 同时命中一个成员）：
 *   .NET → JS：
 *     full     初始快照：GeneratedModel{ messageName, payload }，payload 用生成器消息类型解码（如 MainWindowModel）
 *     snapshot 通用完整快照回退（无生成编码器的模型）：map<property, ModelValue>
 *     update   单属性增量：ModelUpdate{ messageName, payload }，payload 用生成器为模型单独产出的
 *              update 消息类型解码（如 MainWindowModelUpdate），只编码被修改的字段
 *     patch    集合增删差量：CollectionPatch{ action, property, index, count, items, fromIndex }，
 *              对响应式数组原地 splice（Insert/Remove/Replace/Move）；Reset 时 items 承载整列表整体替换
 *   JS → .NET：
 *     ready    ModelReady：页面脚本就绪，请求补发快照
 *     set      ModelSet{ property, value }：本地修改回写
 *     invoke   ModelInvoke{ command, value }：执行 .NET 命令（[RelayCommand] 生成的 ICommand），
 *              command = 命令方法名（如 "OpenWindow"），value 为参数（可空）
 *
 * 命名：.NET 属性名 PascalCase，前端模型 camelCase（首字母小写），本桥负责互转。
 * 类型：.NET 值经 ModelValue 一码归一码（number/text/flag/list/object/blob），
 *       生成器消息的标量字段直通（string/int32/...），object 兜底字段转 ModelValue。
 * 键约定：typed repeated 属性（List<模型>，如 todos）的 ModelValue 对象 map 用 proto 字段号键
 *       （"1"/"2"…）——字段号是固定协议序号，与 .NET 生成器 ConvertFromModelValue/ConvertToModelValue
 *       对称，不依赖前后端命名一致；generic object/Dictionary 属性仍用 name 键。
 *
 * 用法：应用把生成器产出的完整模型 descriptor（如 main_window_model.json）传入——生成器已把
 *       基础信封（WebMessage/ModelValue）内联进每个模型 descriptor，descriptor 自包含、无需额外合并。
 */

/**
 * 平台自适应的消息通道：同一桥代码跑在 WebView2（Windows）、WebKitGTK（Linux）、
 * WKWebView（macOS）上。各平台的 host 对象/注入函数不同，按可用性挑选：
 *   1. WebView2：window.chrome.webview——postMessage + addEventListener('message')；
 *   2. WebKitGTK / WKWebView：JS 经 window.webkit.messageHandlers.wwui.postMessage 回传，
 *      native 侧用 evaluateJavascript 调用注入的 window.wwuiReceive("...") 下发（幂等设置）。
 * 两者都拿不到时（纯浏览器/调试）返回 null，sendEnvelope 静默跳过。
 */
const HANDLER_NAME = 'wwui' // 与 .NET 侧 RegisterScriptMessageHandler("wwui") / AddScriptMessageHandler 一致

/**
 * 下行（native → JS）回调入口。WebKitGTK / WKWebView 的 native 侧经 evaluateJavascript 调
 * window.wwuiReceive("...") 下发消息；WebView2 走 window.chrome.webview.postMessage → 'message' 事件。
 * 下行入口与「发送通道」解耦：wwuiReceive 在模块加载时即定义（不依赖 messageHandlers 是否已同步
 * 可见），页面早期就能接收 native 的快照/增量下发。
 */
let receiveHandler: ((data: string) => void) | undefined
;(window as unknown as Record<string, unknown>).wwuiReceive = (raw: unknown): void => {
  if (typeof raw === 'string' && receiveHandler) receiveHandler(raw)
}

/** WebView2 的下行事件：chrome.webview 的 'message' 事件 → receiveHandler（每页只挂一次）。 */
let webView2ReceiveWired = false
function wireWebView2Receive(): void {
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

/** 发送时惰性解析发送通道：WebView2 chrome.webview / WebKit messageHandlers.wwui。
 *  WebKitGTK 把 script message handler 同步进 web 进程有延迟，页面早期脚本运行时
 *  messageHandlers.wwui 可能尚未可见——若在模块作用域一次性解析并缓存，通道会永远是 null
 *  （wwuiReceive 不设、Ready 不发、命令发不出，本桥即告失效）。故每次发送都重新探测。 */
function resolveSendChannel(): { postMessage(data: string): void } | null {
  const chrome = (window as unknown as {
    chrome?: { webview?: { postMessage(data: string): void } }
  }).chrome?.webview
  if (chrome?.postMessage) return { postMessage: (data) => chrome.postMessage(data) }

  const webkit = (window as unknown as {
    webkit?: { messageHandlers?: Record<string, { postMessage(data: string): void }> }
  }).webkit
  const handler = webkit?.messageHandlers?.[HANDLER_NAME]
  if (handler?.postMessage) return { postMessage: (data) => handler.postMessage(data) }

  return null
}

// ---- descriptor：生成器产出的模型 descriptor 自包含（基础信封 WebMessage/ModelValue 已内联） ----

const MODEL_VALUE_TYPE = 'webwindowui.model.ModelValue'

// ---- 轻量类型（与生成器内联的基础信封对应） ----

interface ModelValueLike {
  /** protobufjs oneof 判别器：值为实际命中的成员名（"number"/"text"/...），未设置时为 null。 */
  kind?: string
  number?: number
  text?: string
  flag?: boolean
  list?: { items?: ModelValueLike[] }
  object?: {
    fields?: Record<string, ModelValueLike>
    /** 序数键（typed POCO 元素）：proto 字段号 int → 值，protobufjs map<int32,…> 的 JS 对象键是数字字符串。 */
    ordinalFields?: Record<string, ModelValueLike>
  }
  blob?: Uint8Array
}

interface WebMessageLike {
  ready?: object
  /** 增量更新：payload 是生成器为模型产出的 update 消息（如 MainWindowModelUpdate）字节。 */
  update?: { messageName?: string; payload?: Uint8Array }
  set?: { property?: string; value?: ModelValueLike }
  snapshot?: { data?: Record<string, ModelValueLike> }
  full?: { messageName?: string; payload?: Uint8Array }
  /** 命令执行（前端 → .NET）：command = 命令方法名，value = 参数（可空）。 */
  invoke?: { command?: string; value?: ModelValueLike }
  /** 集合增删差量补丁（.NET → 前端）：前端对响应式数组原地 splice。action 为枚举数值。 */
  patch?: {
    action?: number
    property?: string
    index?: number
    count?: number
    items?: ModelValueLike[]
    fromIndex?: number
  }
}

/** CollectionPatchAction 枚举取值（与 .NET CollectionPatchAction / 生成器 descriptor 严格一致）。 */
const PATCH_INSERT = 1
const PATCH_REMOVE = 2
const PATCH_REPLACE = 3
const PATCH_MOVE = 4
const PATCH_RESET = 5

// ---- ModelValue ↔ JS ----

/**
 * ModelValue → JS 值。
 * 注意：protobufjs decode 用 oneof 判别器属性（ModelValue 的 oneof 名叫 "kind"）指示实际命中的成员，
 * 未命中的标量成员访问时返回 proto3 默认值（number=0 / text="" / flag=false），而不是 null/undefined。
 * 所以绝不能靠判空判断哪个成员生效——0 是合法值且恰好是未命中 number 的默认值，
 * text/object 等值会被误判成 0。必须 switch 在 kind 上。
 */
function modelValueToJs(v: ModelValueLike): unknown {
  if (!v || typeof v !== 'object') return null
  switch (v.kind) {
    case 'number':
      return v.number
    case 'text':
      return v.text
    case 'flag':
      return v.flag
    case 'blob':
      return v.blob
    case 'list':
      return (v.list?.items ?? []).map(modelValueToJs)
    case 'object': {
      const out: Record<string, unknown> = {}
      for (const [k, fv] of Object.entries(v.object?.fields ?? {})) out[k] = modelValueToJs(fv)
      for (const [k, fv] of Object.entries(v.object?.ordinalFields ?? {})) out[k] = modelValueToJs(fv)
      return out
    }
    default:
      return null // kind 为 null = 空 ModelValue（null 值）
  }
}

/** JS 值 → ModelValue。 */
function jsToModelValue(value: unknown): ModelValueLike {
  if (value === null || value === undefined) return {}
  switch (typeof value) {
    case 'number':
      return { number: value }
    case 'string':
      return { text: value }
    case 'boolean':
      return { flag: value }
  }
  if (value instanceof Uint8Array) return { blob: value }
  if (value instanceof ArrayBuffer) return { blob: new Uint8Array(value) }
  if (value instanceof Date) return { text: value.toISOString() } // Date → ISO 文本（.NET DateTime 可解析）
  if (Array.isArray(value)) return { list: { items: value.map(jsToModelValue) } }
  if (typeof value === 'object') {
    const fields: Record<string, ModelValueLike> = {}
    for (const [k, v] of Object.entries(value)) fields[k] = jsToModelValue(v)
    return { object: { fields } }
  }
  return {}
}

/**
 * protobufjs 对 64 位整数字段（int64/uint64/sint64/fixed64）解码返回 Long 对象而非 number，
 * 统一转成 number（超过 2^53 会丢失精度，桥文档约定如此）。
 */
function normalize(raw: unknown): unknown {
  if (typeof raw === 'object' && raw !== null && typeof (raw as { toNumber?: unknown }).toNumber === 'function') {
    return (raw as { toNumber: () => number }).toNumber()
  }
  return raw
}

// ---- 传输 ----

/**
 * Uint8Array → 不含 NUL 的 Latin-1 字符串（与 .NET WebView2StringCodec.Encode 同算法）：
 * 0x00 → '\0'，0x5C（转义符自身）→ '\\'，其余字节原样一个字符。
 * WebView2 消息通道在 NUL 处截断，故转义后字符串才可无损通过。
 */
function bytesToEscaped(bytes: Uint8Array): string {
  let s = ''
  for (let i = 0; i < bytes.length; i++) {
    const b = bytes[i]
    if (b === 0x00) s += '\\0'
    else if (b === 0x5c) s += '\\\\'
    else s += String.fromCharCode(b)
  }
  return s
}

/** bytesToEscaped 的逆操作：还原回 Uint8Array。畸形输入（结尾孤立转义符）静默丢弃该字符。 */
function escapedToBytes(s: string): Uint8Array {
  const out: number[] = []
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i)
    if (c === 0x5c) {
      if (i + 1 >= s.length) break // 结尾孤立转义符：畸形，丢弃
      const n = s.charCodeAt(++i)
      out.push(n === 0x30 ? 0x00 : n) // '\0'→0x00，'\\'→0x5C
    } else {
      out.push(c)
    }
  }
  return new Uint8Array(out)
}

/** .NET 属性名（PascalCase）→ 前端模型键（camelCase）。 */
function toCamelCase(key: string): string {
  return key.charAt(0).toLowerCase() + key.slice(1)
}

/** 前端模型键（camelCase）→ .NET 属性名（PascalCase）。 */
function toPascalCase(key: string): string {
  return key.charAt(0).toUpperCase() + key.slice(1)
}

/**
 * 命令执行宿主基类：带 [RelayCommand] 的模型生成的 TS 类继承它（如
 * `class LauncherModel extends ModelCommandHost`），命令方法经 `this._commandChannel`
 * 把命令调用发给 .NET（ModelInvoke { command, value }）。
 * 通道本身由 {@link bindModel} 注入为不可枚举实例属性（不进响应式 watch、不落快照键），
 * 本类只承载类型契约，让各生成模型不必重复声明同一字段。
 */
export class ModelCommandHost {
  /** 命令执行通道：bindModel 注入，调用即触发 .NET 侧 ICommand 执行。 */
  protected _commandChannel?: (name: string, value?: unknown) => void
}

/**
 * 把模型实例变成响应式并打通双向绑定，返回可直接绑定到模板的 typed reactive 代理。
 *
 * @param generatedJson 生成器产出的完整模型 descriptor（如 main_window_model.json）。
 *                      生成器已把基础信封（WebMessage/ModelValue）内联进该 descriptor，自包含可直接解析。
 * @returns 绑定了 .NET 双向绑定的 reactive 模型实例。每个模型实例调用一次。
 */
export function bindModel<T extends object>(model: T, generatedJson: unknown): T {
  const root = protobuf.Root.fromJSON(generatedJson as unknown as protobuf.INamespace)
  const webMessageType = root.lookupType('webwindowui.model.WebMessage')

  /** 编码 WebMessage 信封并发送（字节 → NUL 转义字符串 → 平台通道）。返回是否发出（通道未就绪为 false）。 */
  const sendEnvelope = (payload: Record<string, unknown>): boolean => {
    const ch = resolveSendChannel()
    if (!ch) return false
    const bytes = webMessageType.encode(payload as unknown as never).finish()
    ch.postMessage(bytesToEscaped(bytes as Uint8Array))
    return true
  }

  // 正在应用来自 .NET 的变更时置 true，让本地 watch 跳过回写，避免回声循环
  let suppressEcho = false

  const m = reactive(model) as T & Record<string, unknown>
  const values = m as unknown as Record<string, unknown> // 泛型 T 不能做写索引，这里用非泛型引用

  // typed-repeated 元素字段表：根属性若是 typed repeated（List<模型>，如 todos），元素对象 map 用
  // proto 字段号键（"1"/"2"…）而非属性名——协议序号是固定契约，与 .NET 生成器
  // ConvertFromModelValue/ConvertToModelValue 对称，不依赖前后端命名一致；generic object/Dictionary
  // 属性仍是 name 键。全量快照（typed protobuf）本就走字段号、经 fullModelEntries 产出命名键对象，
  // 此处只管 ModelValue 兜底的增量推送（.NET→前端，updateEntries）、差量补丁（applyPatch）与整列表
  // 回写（前端→.NET，watch）。
  /** root 属性键 → 该属性 typed-repeated 的元素字段号→字段名表（序数键编码/解码用）。
      由生成器烘焙进模型镜像类的静态字符串键契约 ['__repeatedFields']（构建期定死、随类声明发布），
      桥直接读取——不做运行时 constructor.name → lookupType 反射：class 名会被 JS 压缩器改名
      （如 class TodoListModel → g=class{...}），反射必失真（Release 下 typed-repeated 补丁挂的根因）。
      空 → 无 typed-repeated 特判，退回通用 name 键。 */
  const typedElemFields = new Map<string, Record<number, string>>()
  const baked = (model as unknown as { constructor: { readonly ['__repeatedFields']?: Record<string, Record<number, string>> } })
    .constructor['__repeatedFields']
  if (baked && typeof baked === 'object') {
    for (const [prop, byNumber] of Object.entries(baked)) typedElemFields.set(prop, byNumber)
  }

  // 命令通道：生成器为带 [RelayCommand] 方法的模型产出的命令方法（openWindow()/commandWithArg(arg)）
  // 通过它把命令调用发给 .NET 执行（ModelInvoke { command, value }）。定义为不可枚举属性 →
  // 不进响应式 watch 循环、不落快照键；无命令的模型此通道永不使用（生成器不产出命令方法）。
  Object.defineProperty(model, '_commandChannel', {
    enumerable: false,
    configurable: false,
    writable: true,
    value: (name: string, value?: unknown): void => {
      sendEnvelope({ invoke: { command: name, ...(value === undefined ? {} : { value: jsToModelValue(value) }) } })
    },
  })

  /** 批量应用来自 .NET 的远程值（快照 / 增量），不触发本地回写。 */
  const applyRemote = (entries: Iterable<[string, unknown]>): void => {
    suppressEcho = true
    for (const [name, value] of entries) {
      const local = toCamelCase(name)
      if (!(local in values)) continue // 前端模型类没声明的属性不接收（类型以类为准）
      values[local] = value
    }
    nextTick(() => {
      suppressEcho = false
    })
  }

  /** 解码完整模型消息，整理成 [属性名, JS 值] 序列（ModelValue 兜底字段转 JS）。 */
  const fullModelEntries = (full: WebMessageLike['full']): Array<[string, unknown]> => {
    if (!full?.messageName) return []
    let type: protobuf.Type
    try {
      type = root.lookupType(full.messageName)
    } catch {
      return [] // 消息名未注册（descriptor 缺失/漂移）：无法解码，忽略本条
    }
    const decoded = type.decode(full.payload as Uint8Array) as unknown as Record<string, unknown>
    const entries: Array<[string, unknown]> = []
    for (const fieldName of Object.keys(type.fields)) {
      const raw = decoded[fieldName]
      if (raw === undefined) continue
      const f = type.fields[fieldName]
      const isModelValue = f.type === MODEL_VALUE_TYPE
      let value: unknown
      if (isModelValue) {
        value = Array.isArray(raw) ? raw.map(modelValueToJs) : modelValueToJs(raw as ModelValueLike)
      } else if (f.repeated) {
        // typed repeated 消息字段（List<模型>）：把每个元素收敛成「按元素消息声明字段键」的纯对象，
        // 去掉 protobufjs message 实例的内部键（$type 等），保证响应式数据干净、jsToModelValue 回写可控。
        // 元素消息类型挂在字段的 resolvedType 上（resolve 后填充）；仅当它是 Type（已注册消息）才收敛，
        // repeated 标量（List<string> 等）resolvedType 为 undefined → 透传原值。
        const elemFields = f.resolve().resolvedType
        if (elemFields instanceof protobuf.Type) {
          const elemFieldNames = Object.keys(elemFields.fields)
          value = (raw as unknown[]).map((el) => {
            const out: Record<string, unknown> = {}
            const e = el as Record<string, unknown>
            for (const k of elemFieldNames) if (e[k] !== undefined) out[k] = normalize(e[k])
            return out
          })
        } else {
          value = normalize(raw) // 元素类型未注册或非消息：透传
        }
      } else {
        value = normalize(raw)
      }
      entries.push([fieldName, value])
    }
    return entries
  }

  /**
   * 解码增量 update 消息，整理成 [属性名, JS 值] 序列。
   * 增量载荷只编码被修改的字段：protobufjs 对出现在 wire 的字段建立 own property
   * （即使值是 0/空串），未出现的字段不建（访问返回 proto3 默认值 0/""，但 hasOwnProperty
   * 为 false），据此判断哪些字段真正被修改、哪些该跳过。ModelValue 兜底字段（message 类型）
   * 天然有 presence，同样按 own property 判断。
   */
  const updateEntries = (update: WebMessageLike['update']): Array<[string, unknown]> => {
    if (!update?.messageName) return []
    let type: protobuf.Type
    try {
      type = root.lookupType(update.messageName)
    } catch {
      return [] // 消息名未注册：无法解码，忽略本条
    }
    const decoded = type.decode(update.payload as Uint8Array) as unknown as Record<string, unknown>
    const entries: Array<[string, unknown]> = []
    for (const fieldName of Object.keys(type.fields)) {
      if (!Object.prototype.hasOwnProperty.call(decoded, fieldName)) continue
      const f = type.fields[fieldName]
      const isModelValue = f.type === MODEL_VALUE_TYPE
      const elemFields = typedElemFields.get(fieldName)
      if (isModelValue && elemFields) {
        // typed-repeated：.NET 用序数键（proto 字段号）序列化元素（ConvertToModelValue），
        // 解码后把 { "1": v } 翻译回 { title: v } 命名键模型对象（Vue 模板按模型属性名访问）。
        const raw = modelValueToJs(decoded[fieldName] as ModelValueLike)
        entries.push([fieldName, Array.isArray(raw) ? raw.map((el) => {
          const src = el as Record<string, unknown>
          const out: Record<string, unknown> = {}
          for (const [num, name] of Object.entries(elemFields)) if (src[num] !== undefined) out[name] = src[num]
          return out
        }) : raw])
      } else {
        entries.push([fieldName, isModelValue ? modelValueToJs(decoded[fieldName] as ModelValueLike) : normalize(decoded[fieldName])])
      }
    }
    return entries
  }

  /**
   * 应用集合差量补丁（#3）：对响应式数组原地 splice，不整体重建。
   * typed-repeated 元素（ModelValue 序数键 map）经 typedElemFields 翻译回命名键对象；Reset 时
   * Items 承载整列表 → 整体替换。变更期间 suppressEcho，避免本地 deep watch 回写回声。
   */
  const applyPatch = (patch: WebMessageLike['patch']): void => {
    const local = toCamelCase(patch?.property ?? '')
    if (!(local in values)) return // 前端模型类没声明的属性不接收（类型以类为准）
    const elemFields = typedElemFields.get(local)
    const decodeItems = (items: ModelValueLike[] = []): unknown[] =>
      items.map((iv) => {
        const raw = modelValueToJs(iv)
        if (elemFields && raw && typeof raw === 'object' && !Array.isArray(raw)) {
          // typed-repeated 元素：{ "1": v } → { title: v } 命名键（Vue 模板按模型属性名访问）
          const src = raw as Record<string, unknown>
          const out: Record<string, unknown> = {}
          for (const [num, name] of Object.entries(elemFields)) if (src[num] !== undefined) out[name] = src[num]
          return out
        }
        return raw
      })
    const arr = values[local]
    suppressEcho = true
    try {
      if (patch?.action === PATCH_RESET || !Array.isArray(arr)) {
        // Reset（或集合属性被替换成非数组，防御）：整列表替换
        values[local] = decodeItems(patch?.items)
      } else {
        const index = patch?.index ?? 0
        const count = patch?.count ?? 0
        const items = decodeItems(patch?.items)
        switch (patch?.action) {
          case PATCH_INSERT: // .NET Add：Index 处插入 Items
            arr.splice(index, 0, ...items)
            break
          case PATCH_REMOVE: // .NET Remove：删除 Index 起 Count 个元素
            arr.splice(index, count)
            break
          case PATCH_REPLACE: // .NET Replace：以 Items 替换 Index 起 Count 个元素
            arr.splice(index, count, ...items)
            break
          case PATCH_MOVE: // .NET Move：把 fromIndex 起 Count 个元素移到 Index
            arr.splice(index, 0, ...arr.splice(patch?.fromIndex ?? 0, count))
            break
          // 未知 action：忽略（防御，不破坏数组）
        }
      }
    } finally {
      nextTick(() => {
        suppressEcho = false
      })
    }
  }

  const onMessage = (data: string): void => {
    // NUL 转义字符串 → 字节（与 .NET WebView2StringCodec.Decode 对齐）
    const bytes = escapedToBytes(data)

    let msg: WebMessageLike
    try {
      msg = webMessageType.decode(bytes) as unknown as WebMessageLike
    } catch {
      return // 非协议消息，忽略
    }

    // protobufjs decode 未设置的 oneof 成员是 null 而非 undefined，分支必须用 truthy 判断
    if (msg.update && msg.update.messageName) {
      applyRemote(updateEntries(msg.update))
    } else if (msg.patch) {
      applyPatch(msg.patch)
    } else if (msg.snapshot) {
      const map = msg.snapshot.data ?? {}
      const entries: Array<[string, unknown]> = Object.keys(map).map((k) => [k, modelValueToJs(map[k])])
      applyRemote(entries)
    } else if (msg.full) {
      applyRemote(fullModelEntries(msg.full))
    }
    // msg.ready：.NET 不会主动发，忽略
  }

  // 每个根属性一条 watch：本地变更（如 v-model 输入）自动回写 .NET。
  // deep:true —— object/数组属性原地修改（model.extra.a=1、model.tags.push(x)）也要触发回写；
  // 浅 watch 只在引用替换时触发，嵌套变更会静默丢失。echo 已由 suppressEcho 挡住。
  for (const key of Object.keys(values)) {
    watch(
      () => values[key],
      (value) => {
        if (suppressEcho) return
        const elemFields = typedElemFields.get(key)
        if (elemFields && Array.isArray(value)) {
          // typed-repeated 整列表回写：每个元素用序数键（proto 字段号 int）序列化进 ordinalFields，
          // 与 .NET ConvertFromModelValue 的 switch (kv.Key) case 1: 匹配；非 typed 属性走通用 jsToModelValue（name 键）。
          const items = value.map((elem) => {
            const fields: Record<string, unknown> = {}
            const el = elem as Record<string, unknown>
            for (const [num, fieldName] of Object.entries(elemFields)) fields[num] = jsToModelValue(el[fieldName])
            return { object: { ordinalFields: fields } }
          })
          sendEnvelope({ set: { property: toPascalCase(key), value: { list: { items } } } })
        } else {
          sendEnvelope({ set: { property: toPascalCase(key), value: jsToModelValue(value) } })
        }
      },
      { deep: true },
    )
  }

  // 下行回调：WebKit 走已定义的 window.wwuiReceive（见模块顶部）；WebView2 挂 chrome.webview 'message' 事件（每页一次）
  receiveHandler = onMessage
  wireWebView2Receive()

  // 握手：通知 .NET 页面脚本已就绪，请推送初始快照（防止快照先于监听器到达而丢失）。
  // 发送通道（messageHandlers.wwui）在 WebKitGTK 上可能尚未同步可见，重试直到可发（上限 ~5s）；
  // native 侧 load-finished 也会直接推快照，Ready 是防丢补发，重试失败不阻塞。
  let readyAttempts = 0
  const trySendReady = (): void => {
    if (sendEnvelope({ ready: {} })) return
    if (++readyAttempts < 50) setTimeout(trySendReady, 100)
  }
  trySendReady()

  return m
}
