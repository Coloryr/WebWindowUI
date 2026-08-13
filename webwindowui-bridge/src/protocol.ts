/**
 * 线缆协议契约类型（WebMessage 信封 / 集合补丁枚举 / 命令宿主基类）。
 * 与生成器内联进每个模型 descriptor 的基础信封、以及 .NET 侧 ModelProtocol.cs 严格一致。
 */
import type { ModelValueLike } from './value'

/** ModelValue 消息全名（生成器 descriptor 里 object 兜底字段的 type）。 */
export const MODEL_VALUE_TYPE = 'webwindowui.model.ModelValue'

/** CollectionPatchAction 枚举取值（与 .NET CollectionPatchAction / 生成器 descriptor 一致）。 */
export const PATCH_INSERT = 1
export const PATCH_REMOVE = 2
export const PATCH_REPLACE = 3
export const PATCH_MOVE = 4
export const PATCH_RESET = 5
export const PATCH_ELEMENT_SET = 6

/** WebMessage 信封形状（oneof，同时命中一个成员）。modelId/commandId 代替消息名/命令名（FNV-1a 哈希/声明序）。 */
export interface WebMessageLike {
  /** 实例唯一 ID（信封级 header，int64）：首个 full/snapshot 捕获为权威，其余消息做防串守卫。 */
  modelInstanceId?: number
  /** 页面脚本就绪（前端 → .NET）：请求补发快照。 */
  ready?: object
  /** 单属性增量（.NET → JS）：payload 是生成器为模型产出的 update 消息字节，只编码被修改字段。 */
  update?: { modelId?: number; payload?: Uint8Array }
  /** 本地修改回写（JS → .NET）；元素字段级回写带 elementInstanceId + elementProperty。 */
  set?: {
    property?: string
    value?: ModelValueLike
    elementInstanceId?: number
    elementProperty?: string
  }
  /** 通用完整快照回退（.NET → JS，无生成编码器的模型）：map<property, ModelValue>。 */
  snapshot?: { data?: Record<string, ModelValueLike> }
  /** 初始快照（.NET → JS）：payload 用生成器消息类型解码（如 MainWindowModel）。 */
  full?: { modelId?: number; payload?: Uint8Array }
  /** 命令执行（JS → .NET）：commandId = [RelayCommand] 声明序，value = 参数（可空）。 */
  invoke?: { commandId?: number; value?: ModelValueLike }
  /** 集合增删差量补丁（.NET → JS）：前端对响应式数组原地 splice；Reset 的 items 承载整列表；ElementSet 是元素级属性变更。 */
  patch?: {
    action?: number
    property?: string
    index?: number
    count?: number
    items?: ModelValueLike[]
    fromIndex?: number
    elementInstanceId?: number
    elementProperty?: string
    elementValue?: ModelValueLike
  }
}

/**
 * 命令执行宿主基类：带 [RelayCommand] 的模型生成的 TS 镜像类继承它，命令方法经 `this._commandChannel`
 * 把命令调用发给 .NET（ModelInvoke { commandId, value }）。通道本身由 bindModel 注入为不可枚举实例属性，
 * 本类只承载类型契约，让各生成模型不必重复声明同一字段。
 */
export class ModelCommandHost {
  protected _commandChannel?: (id: number, value?: unknown) => void
}
