/**
 * 传输编解码与命名互转：NUL 转义字符串 ↔ Uint8Array、camelCase ↔ PascalCase、
 * 64 位 Long 归一化、元素实例 ID 注入。纯函数、平台无关。
 */

/**
 * Uint8Array → 不含 NUL 的 Latin-1 字符串（与 .NET WebView2StringCodec.Encode 同算法）。
 * WebView2 消息通道在 NUL 处截断，故 0x00 → '\0'、0x5C（转义符自身）→ '\\'，其余字节原样一个字符。
 */
export function bytesToEscaped(bytes: Uint8Array): string {
  let s = ''
  for (let i = 0; i < bytes.length; i++) {
    const b = bytes[i]
    if (b === 0x00) s += '\\0'
    else if (b === 0x5c) s += '\\\\'
    else s += String.fromCharCode(b)
  }
  return s
}

/** bytesToEscaped 的逆操作；结尾孤立转义符（畸形输入）丢弃。 */
export function escapedToBytes(s: string): Uint8Array {
  const out: number[] = []
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i)
    if (c === 0x5c) {
      if (i + 1 >= s.length) break
      const n = s.charCodeAt(++i)
      out.push(n === 0x30 ? 0x00 : n) // '\0'→0x00，'\\'→0x5C
    } else {
      out.push(c)
    }
  }
  return new Uint8Array(out)
}

/** .NET 属性名（PascalCase）→ 前端模型键（camelCase）。 */
export function toCamelCase(key: string): string {
  return key.charAt(0).toLowerCase() + key.slice(1)
}

/** 前端模型键（camelCase）→ .NET 属性名（PascalCase）。 */
export function toPascalCase(key: string): string {
  return key.charAt(0).toUpperCase() + key.slice(1)
}

/** protobufjs 对 int64/uint64 等字段解码返回 Long 对象，统一转 number（超过 2^53 丢精度，桥约定如此）。 */
export function normalize(raw: unknown): unknown {
  if (typeof raw === 'object' && raw !== null && typeof (raw as { toNumber?: unknown }).toNumber === 'function') {
    return (raw as { toNumber: () => number }).toNumber()
  }
  return raw
}

/** 给元素对象注入不可枚举 _modelInstanceId（元素级寻址用，不进 Object.keys watch 循环）。id 缺省/非正数 = 旧端未携带，不注入。 */
export function defineElementId(target: Record<string, unknown>, id: unknown): void {
  const num = typeof id === 'number'
    ? id
    : typeof id === 'object' && id !== null && typeof (id as { toNumber?: unknown }).toNumber === 'function'
      ? (id as { toNumber: () => number }).toNumber()
      : 0
  if (num > 0) {
    Object.defineProperty(target, '_modelInstanceId', {
      enumerable: false,
      configurable: false,
      writable: true,
      value: num,
    })
  }
}
