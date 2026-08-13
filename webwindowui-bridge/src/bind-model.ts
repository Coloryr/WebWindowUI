/**
 * .NET 模型 ↔ Vue 双向绑定桥（protobuf 版）：把模型实例变成响应式，打通与 .NET 的双向绑定。
 *
 * 传输：所有载荷是 NUL 转义 Latin-1 字符串（各平台共用 codec）——WebView2 消息通道在 NUL 处截断，
 * protobuf 字节普遍含 0x00，须转义才能无损通过（WebKitGTK/WKWebView 一并统一走同一转义）。
 *
 * 消息（webwindowui.model.WebMessage 信封，oneof 命中一个成员）：
 *   .NET → JS：full（初始快照）/ snapshot（通用回退）/ update（单属性增量）/ patch（集合差量）
 *   JS → .NET：ready（就绪）/ set（回写，含元素级）/ invoke（命令）
 * modelId/commandId 代替消息名/命令名：modelId 是完整消息名 FNV-1a 哈希，commandId 是 [RelayCommand] 声明序。
 * modelInstanceId（信封级 int64）：实例唯一 ID，桥从首个 full/snapshot 捕获为 model._modelInstanceId，
 * 对后续消息做防串守卫（窗口换绑后旧实例在途消息丢弃）。
 *
 * 用法：应用把生成器产出的完整模型 descriptor（如 main_window_model.json）传入——基础信封已内联、
 * descriptor 自包含。每个模型实例调用一次。
 */
import { reactive, watch, nextTick } from 'vue'
import * as protobuf from 'protobufjs'
import { postMessage, onReceive } from './channel'
import { bytesToEscaped, escapedToBytes, toCamelCase, toPascalCase, normalize, defineElementId } from './codec'
import { modelValueToJs, jsToModelValue, type ModelValueLike } from './value'
import {
  MODEL_VALUE_TYPE,
  PATCH_INSERT,
  PATCH_REMOVE,
  PATCH_REPLACE,
  PATCH_MOVE,
  PATCH_RESET,
  PATCH_ELEMENT_SET,
  type WebMessageLike,
} from './protocol'

export function bindModel<T extends object>(model: T, generatedJson: unknown): T {
  const root = protobuf.Root.fromJSON(generatedJson as unknown as protobuf.INamespace)
  const webMessageType = root.lookupType('webwindowui.model.WebMessage')

  /** 编码 WebMessage 信封并发送（字节 → NUL 转义串 → 平台通道），返回是否发出。已捕获实例 ID 时并入信封。 */
  const sendEnvelope = (payload: Record<string, unknown>): boolean => {
    const body = boundModelInstanceId !== undefined && boundModelInstanceId !== 0
      ? { modelInstanceId: boundModelInstanceId, ...payload }
      : payload
    const bytes = webMessageType.encode(body as unknown as never).finish()
    return postMessage(bytesToEscaped(bytes as Uint8Array))
  }

  // 正在应用来自 .NET 的变更时置 true，让本地 watch 跳过回写，避免回声循环
  let suppressEcho = false

  /** 当前绑定实例的唯一 ID（.NET 侧进程内单调自增 int64）。0/缺省 = 旧端无实例信息，容忍。 */
  let boundModelInstanceId: number | undefined

  const m = reactive(model) as T & Record<string, unknown>
  const values = m as unknown as Record<string, unknown> // 泛型 T 不能做写索引，用非泛型引用

  // typed-repeated 元素字段表（根属性 → { proto 字段号: 元素属性名 }）：构建期烘焙进镜像类静态键
  // ['__repeatedFields']（字符串字面量键，压缩器不改写）——不做运行时 constructor.name → lookupType 反射，
  // 类名会被 minifier 改名（Release 下 typed-repeated 补丁挂的根因）。空 → 无 typed-repeated 特判。
  const typedElemFields = new Map<string, Record<number, string>>()
  const baked = (model as unknown as { constructor: { readonly ['__repeatedFields']?: Record<string, Record<number, string>> } })
    .constructor['__repeatedFields']
  if (baked && typeof baked === 'object') {
    for (const [prop, byNumber] of Object.entries(baked)) typedElemFields.set(prop, byNumber)
  }

  // 线缆协议契约（modelId + full/update 解码类型名）：同 '__repeatedFields'，构建期字面量键直接读。
  const protocol = (model as unknown as { constructor: { readonly ['__protocol']?: { modelId?: number; full?: string; update?: string } } })
    .constructor['__protocol']

  // 实例 ID 与命令通道：都不可枚举注入（不进 Object.keys watch 循环、不落快照键）。Vue 模板可读 model._modelInstanceId。
  Object.defineProperty(model, '_modelInstanceId', {
    enumerable: false,
    configurable: false,
    writable: true,
    value: 0,
  })
  Object.defineProperty(model, '_commandChannel', {
    enumerable: false,
    configurable: false,
    writable: true,
    value: (id: number, value?: unknown): void => {
      sendEnvelope({ invoke: { commandId: id, ...(value === undefined ? {} : { value: jsToModelValue(value) }) } })
    },
  })

  /** typed-repeated 整列表回写（结构变化 / 元素未带 ID 的兜底）：元素用序数键（proto 字段号）序列化，
      附 _modelInstanceId name 键，.NET 按 id 复用既有实例（保 ModelInstanceId/引用，避免结构回写后元素 id 漂移）。 */
  const sendElementList = (key: string, arr: unknown[]): void => {
    const elemFields = typedElemFields.get(key)
    if (!elemFields || !Array.isArray(arr)) return
    const items = arr.map((elem) => {
      const fields: Record<string, unknown> = {}
      const el = elem as Record<string, unknown>
      for (const [num, fieldName] of Object.entries(elemFields)) fields[num] = jsToModelValue(el[fieldName])
      const id = el._modelInstanceId
      const idKey: Record<string, unknown> = {}
      if (typeof id === 'number' && id > 0) idKey['_modelInstanceId'] = jsToModelValue(id)
      return { object: { ordinalFields: fields, ...(Object.keys(idKey).length > 0 ? { fields: idKey } : {}) } }
    })
    sendEnvelope({ set: { property: toPascalCase(key), value: { list: { items } } } })
  }

  const elementWatchRefs = new Map<string, Array<() => void>>()

  /** 给 typed-repeated 每个元素的每个字段挂 watch：字段变化只回写该元素该属性（ModelSet{ elementInstanceId, elementProperty }）。
      元素未带 ID（旧端）→ 退回整列表回写；元素被结构替换（splice/整体替换）由结构 watch 处理，此处守卫放行。 */
  const armElementWatches = (key: string, arr: unknown[]): void => {
    const elemFields = typedElemFields.get(key)
    const prev = elementWatchRefs.get(key)
    if (prev) {
      for (const stop of prev) stop()
      elementWatchRefs.delete(key)
    }
    if (!elemFields || !Array.isArray(arr)) return
    const refs: Array<() => void> = []
    arr.forEach((el, i) => {
      if (!el || typeof el !== 'object') return
      for (const fieldName of Object.values(elemFields)) {
        const stop = watch(
          () => (arr[i] as Record<string, unknown>)[fieldName],
          (nv) => {
            if (suppressEcho) return
            if (arr[i] !== el) return // 元素被结构替换：结构 watch 处理
            const elementId = (el as Record<string, unknown>)._modelInstanceId
            if (typeof elementId !== 'number' || elementId <= 0) {
              sendElementList(key, arr)
              return
            }
            sendEnvelope({
              set: {
                property: toPascalCase(key),
                elementInstanceId: elementId,
                elementProperty: fieldName,
                value: jsToModelValue(nv),
              },
            })
          },
          { deep: true },
        )
        refs.push(stop)
      }
    })
    elementWatchRefs.set(key, refs)
  }

  /** 批量应用 .NET 远程值（快照/增量），不触发本地回写；整列表替换后重挂元素级 watch。 */
  const applyRemote = (entries: Iterable<[string, unknown]>): void => {
    suppressEcho = true
    let touchedTyped = false
    for (const [name, value] of entries) {
      const local = toCamelCase(name)
      if (!(local in values)) continue // 前端类未声明的属性不接收（类型以类为准）
      if (typedElemFields.has(local)) touchedTyped = true
      values[local] = value
    }
    nextTick(() => {
      suppressEcho = false
      if (touchedTyped) for (const key of typedElemFields.keys()) armElementWatches(key, values[key] as unknown[])
    })
  }

  /** 解码 full 消息成 [属性名, JS 值]：ModelValue 字段转 JS；typed repeated 收敛成按元素消息字段键的纯对象
      （去 protobufjs 实例内部键），元素 modelInstanceId 抽为不可枚举 _modelInstanceId；repeated 标量/未注册元素透传。 */
  const fullModelEntries = (full: WebMessageLike['full']): Array<[string, unknown]> => {
    if (!full?.modelId || !protocol || full.modelId !== protocol.modelId || !protocol.full) return []
    let type: protobuf.Type
    try {
      type = root.lookupType(protocol.full)
    } catch {
      return [] // 解码类型未注册（descriptor 缺失/漂移）
    }
    const decoded = type.decode(full.payload as Uint8Array) as unknown as Record<string, unknown>
    const entries: Array<[string, unknown]> = []
    for (const fieldName of Object.keys(type.fields)) {
      if (fieldName === 'modelInstanceId') continue // 框架保留字段：根级实例 ID 走信封
      const raw = decoded[fieldName]
      if (raw === undefined) continue
      const f = type.fields[fieldName]
      const isModelValue = f.type === MODEL_VALUE_TYPE
      let value: unknown
      if (isModelValue) {
        value = Array.isArray(raw) ? raw.map(modelValueToJs) : modelValueToJs(raw as ModelValueLike)
      } else if (f.repeated) {
        const elemFields = f.resolve().resolvedType
        if (elemFields instanceof protobuf.Type) {
          const elemFieldNames = Object.keys(elemFields.fields)
          value = (raw as unknown[]).map((el) => {
            const out: Record<string, unknown> = {}
            const e = el as Record<string, unknown>
            for (const k of elemFieldNames) {
              if (k === 'modelInstanceId') continue
              if (e[k] !== undefined) out[k] = normalize(e[k])
            }
            defineElementId(out, normalize(e.modelInstanceId))
            return out
          })
        } else {
          value = normalize(raw) // 元素类型未注册或 repeated 标量：透传
        }
      } else {
        value = normalize(raw)
      }
      entries.push([fieldName, value])
    }
    return entries
  }

  /** 解码 update 消息成 [属性名, JS 值]：增量只编码被修改字段，以 own property 判断（proto3 默认值 0/"" 不可判空）；
      typed-repeated 元素序数键（.NET ConvertToModelValue）翻译回命名键，_modelInstanceId 抽为不可枚举。 */
  const updateEntries = (update: WebMessageLike['update']): Array<[string, unknown]> => {
    if (!update?.modelId || !protocol || update.modelId !== protocol.modelId || !protocol.update) return []
    let type: protobuf.Type
    try {
      type = root.lookupType(protocol.update)
    } catch {
      return []
    }
    const decoded = type.decode(update.payload as Uint8Array) as unknown as Record<string, unknown>
    const entries: Array<[string, unknown]> = []
    for (const fieldName of Object.keys(type.fields)) {
      if (!Object.prototype.hasOwnProperty.call(decoded, fieldName)) continue
      if (fieldName === 'modelInstanceId') continue // 生成器已排除，防御
      const f = type.fields[fieldName]
      const isModelValue = f.type === MODEL_VALUE_TYPE
      const elemFields = typedElemFields.get(fieldName)
      if (isModelValue && elemFields) {
        const raw = modelValueToJs(decoded[fieldName] as ModelValueLike)
        entries.push([fieldName, Array.isArray(raw) ? raw.map((el) => {
          const src = el as Record<string, unknown>
          const out: Record<string, unknown> = {}
          for (const [num, name] of Object.entries(elemFields)) if (src[num] !== undefined) out[name] = src[num]
          defineElementId(out, src['_modelInstanceId'])
          return out
        }) : raw])
      } else {
        entries.push([fieldName, isModelValue ? modelValueToJs(decoded[fieldName] as ModelValueLike) : normalize(decoded[fieldName])])
      }
    }
    return entries
  }

  /** 应用集合差量补丁：对响应式数组原地 splice；Reset/非数组整列替换；ElementSet 按 ModelInstanceId 定位元素只改单属性。 */
  const applyPatch = (patch: WebMessageLike['patch']): void => {
    const local = toCamelCase(patch?.property ?? '')
    if (!(local in values)) return
    const elemFields = typedElemFields.get(local)
    const decodeItems = (items: ModelValueLike[] = []): unknown[] =>
      items.map((iv) => {
        const raw = modelValueToJs(iv)
        if (elemFields && raw && typeof raw === 'object' && !Array.isArray(raw)) {
          // typed-repeated 元素：序数键 → 命名键，_modelInstanceId 抽为不可枚举
          const src = raw as Record<string, unknown>
          const out: Record<string, unknown> = {}
          for (const [num, name] of Object.entries(elemFields)) if (src[num] !== undefined) out[name] = src[num]
          defineElementId(out, src['_modelInstanceId'])
          return out
        }
        return raw
      })
    const arr = values[local]
    suppressEcho = true
    try {
      if (patch?.action === PATCH_ELEMENT_SET && Array.isArray(arr)) {
        // elementInstanceId 是 int64 → Long，必须 normalize 成 number 才能与 _modelInstanceId（number）比较
        const id = normalize(patch?.elementInstanceId) ?? 0
        const el = arr.find((e) => (e as Record<string, unknown>)._modelInstanceId === id)
        if (el && typeof patch?.elementProperty === 'string') {
          ;(el as Record<string, unknown>)[toCamelCase(patch.elementProperty)] = modelValueToJs(patch.elementValue)
        }
      } else if (patch?.action === PATCH_RESET || !Array.isArray(arr)) {
        values[local] = decodeItems(patch?.items)
      } else {
        const index = patch?.index ?? 0
        const count = patch?.count ?? 0
        const items = decodeItems(patch?.items)
        switch (patch?.action) {
          case PATCH_INSERT: // .NET Add
            arr.splice(index, 0, ...items)
            break
          case PATCH_REMOVE: // .NET Remove
            arr.splice(index, count)
            break
          case PATCH_REPLACE: // .NET Replace
            arr.splice(index, count, ...items)
            break
          case PATCH_MOVE: // .NET Move
            arr.splice(index, 0, ...arr.splice(patch?.fromIndex ?? 0, count))
            break
          // 未知 action：忽略（防御，不破坏数组）
        }
      }
    } finally {
      nextTick(() => {
        suppressEcho = false
        if (elemFields) armElementWatches(local, values[local] as unknown[])
      })
    }
  }

  const onMessage = (data: string): void => {
    let msg: WebMessageLike
    try {
      msg = webMessageType.decode(escapedToBytes(data)) as unknown as WebMessageLike
    } catch {
      return // 非协议消息，忽略
    }

    // full/snapshot 捕获权威实例 ID（更新暴露的 _modelInstanceId）；其余消息校验——已绑定且携带不同 ID → 旧实例在途消息丢弃。
    const inst = (normalize(msg.modelInstanceId) as number) ?? 0
    if (msg.full || msg.snapshot) {
      if (inst !== 0) {
        boundModelInstanceId = inst
        ;(model as Record<string, unknown>)._modelInstanceId = inst
      }
    } else if (boundModelInstanceId !== undefined && inst !== 0 && inst !== boundModelInstanceId) {
      return
    }

    // protobufjs decode 未设置的 oneof 成员是 null 而非 undefined，分支必须用 truthy 判断
    if (msg.update && msg.update.modelId) {
      applyRemote(updateEntries(msg.update))
    } else if (msg.patch) {
      applyPatch(msg.patch)
    } else if (msg.snapshot) {
      const map = msg.snapshot.data ?? {}
      applyRemote(Object.keys(map).map((k) => [k, modelValueToJs(map[k])]))
    } else if (msg.full) {
      applyRemote(fullModelEntries(msg.full))
    }
  }

  // 每根属性一条 watch：本地变更（v-model 输入、object/数组原地修改）自动回写 .NET。typed-repeated 例外——
  // 元素字段变化由 armElementWatches 逐元素回写，这里只挂结构 watch（浅拷贝只读 length+下标），push/splice/整体替换整列回写 + 重挂。
  for (const key of Object.keys(values)) {
    if (typedElemFields.has(key)) {
      watch(
        () => [...((values[key] as unknown[]) ?? [])],
        () => {
          if (suppressEcho) return
          const arr = values[key] as unknown[]
          armElementWatches(key, arr)
          sendElementList(key, arr)
        },
      )
    } else {
      watch(
        () => values[key],
        (value) => {
          if (suppressEcho) return
          sendEnvelope({ set: { property: toPascalCase(key), value: jsToModelValue(value) } })
        },
        { deep: true },
      )
    }
  }

  // 初始挂元素级 watch（默认数组字段为空；首张快照到达后由 applyRemote 重挂）
  for (const key of typedElemFields.keys()) armElementWatches(key, values[key] as unknown[])

  onReceive(onMessage)

  // 握手：通知 .NET 页面脚本已就绪、补发快照（防快照先于监听器到达而丢失）。WebKitGTK 的 messageHandlers 同步
  // 有延迟，重试直到可发（上限 ~5s）；native 侧 load-finished 也会直接推快照，重试失败不阻塞。
  let readyAttempts = 0
  const trySendReady = (): void => {
    if (sendEnvelope({ ready: {} })) return
    if (++readyAttempts < 50) setTimeout(trySendReady, 100)
  }
  trySendReady()

  return m
}
