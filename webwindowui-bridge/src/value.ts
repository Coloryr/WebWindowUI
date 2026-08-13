/**
 * ModelValue ↔ JS 值互转。ModelValue 是 .NET 值在线缆上的统一容器
 * （与生成器内联进每个模型 descriptor 的基础信封对应）。
 */

/** ModelValue 消息形状（.NET 侧模型值一码归一码：number/text/flag/list/object/blob）。 */
export interface ModelValueLike {
  /** protobufjs oneof 判别器：实际命中的成员名（"number"/"text"/...），未设置时为 null。 */
  kind?: string
  number?: number
  text?: string
  flag?: boolean
  list?: { items?: ModelValueLike[] }
  object?: {
    fields?: Record<string, ModelValueLike>
    /** 序数键（typed POCO 元素）：proto 字段号 int → 值，protobufjs map<int32,…> 的键是数字字符串。 */
    ordinalFields?: Record<string, ModelValueLike>
  }
  blob?: Uint8Array
}

/**
 * ModelValue → JS 值。必须 switch 在 oneof 判别器 kind 上：未命中的标量成员访问返回 proto3 默认值
 * （number=0 / text="" / flag=false），判空会把 text/object 等合法值误判成 number 0。
 */
export function modelValueToJs(v: ModelValueLike | undefined): unknown {
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
export function jsToModelValue(value: unknown): ModelValueLike {
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
  if (value instanceof Date) return { text: value.toISOString() } // .NET DateTime 可解析
  if (Array.isArray(value)) return { list: { items: value.map(jsToModelValue) } }
  if (typeof value === 'object') {
    const fields: Record<string, ModelValueLike> = {}
    for (const [k, v] of Object.entries(value)) fields[k] = jsToModelValue(v)
    return { object: { fields } }
  }
  return {}
}
