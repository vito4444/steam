# 云端 AI 试穿接入说明

## 启动配置

PixelFit 0.3.0 默认使用本地分层渲染。云端 provider 可在设置页选择并录入用户自备 Key；Key 由 Electron safeStorage（Windows DPAPI）加密，永不通过 preload 回显。环境变量继续作为兼容后备。

| 环境变量 | 默认值 | 可选值 / 含义 |
|---|---|---|
| PIXELFIT_VTON_PROVIDER | fashn | fashn / aliyun / fal |
| `FASHN_API_KEY` | 无 | 使用 FASHN 时必填，用户自备 |
| DASHSCOPE_API_KEY | 无 | 使用阿里百炼时必填，北京区 Key |
| `FAL_KEY` | 无 | 使用 fal 时必填，用户自备 |
| `PIXELFIT_VTON_MODEL` | `tryon-max` | FASHN 可切 `tryon-v1.6` |
| `PIXELFIT_VTON_MODE` | `fast` | FASHN：`fast` / `performance` / `balanced` / `quality` |
| `PIXELFIT_VTON_RESOLUTION` | `1k` | Try-On Max：`1k` / `2k` / `4k` |
| `PIXELFIT_VTON_TIMEOUT_MS` | `180000` | 全套生成超时，限制在 10 秒至 10 分钟 |

```powershell
$env:PIXELFIT_VTON_PROVIDER='fashn'
$env:FASHN_API_KEY='your-key'
$env:PIXELFIT_VTON_MODEL='tryon-max'
$env:PIXELFIT_VTON_MODE='fast'
$env:PIXELFIT_VTON_RESOLUTION='1k'
npm run dev
```

切换到 fal 只需要替换配置：

```powershell
$env:PIXELFIT_VTON_PROVIDER='fal'
$env:FAL_KEY='your-key'
npm run dev
```

## 数据流

在生产应用中推荐直接从“设置 → 换装渲染引擎 → 云端供应商”保存。设置文件只包含 DPAPI 密文和供应商 ID；系统安全存储不可用时拒绝保存，不降级为明文。环境变量配置仍兼容。

1. 本地 `TryOnEngine` 始终先画出分层预览。
2. 用户切到“AI 高清”不会上传；首次点击“生成高清试穿”时出现未勾选的授权确认。
3. 渲染进程把不含衣物的人物底图和所选衣物转成 data URI，经最小 IPC 契约交给主进程。
4. `TryOnService` 验证 `consent === true`、provider/key、支持品类与缓存。
5. FASHN/fal 多件按连衣裙 → 下装 → 上装 → 外套 → 鞋 → 包 → 饰品顺序生成；阿里把上装+下装组合成一个 OutfitAnyone Plus 任务。
6. 成功图片（PNG / JPEG / WebP）缓存在素材库的 `cache/tryon/`。缓存键包含人物图、排序后的衣物图、provider 与配置；相同搭配不再调用云端。
7. 失败、没网、超时、取消或没 key 都返回 `fallback: true`，人物画布继续显示分层预览。

阿里百炼环境变量启动示例：

    $env:PIXELFIT_VTON_PROVIDER='aliyun'
    $env:DASHSCOPE_API_KEY='your-beijing-key'
    npm run dev

## Provider 边界

业务层只依赖 CloudTryOnProvider。provider-registry 根据保存设置或环境变量选择 FashnProvider、AliyunProvider 或 FalProvider，IPC、缓存和 UI 不知道具体 HTTP schema。

FASHN Try-On Max 的公开文档覆盖 clothing、shoes、hats、jewelry、bags 与其他 wearable products；PixelFit 仍按单品逐步提交。fal Image Apps V2 的公开 schema 只接受 person + clothing image，因此本实现仅提交连衣裙、下装、上装、外套，鞋、包、饰品会在界面明确列出并跳过。

阿里 OutfitAnyone Plus 支持上装、下装、上+下组合与连衣裙/连体衣，启用 restore_face。人物与衣物先上传到官方临时 OSS（48 小时），任务/输出 URL 保留 24 小时；鞋、包、配饰和外套不会上传或计费。官方临时存储只适合开发/低并发，生产部署应替换为自有北京区 OSS。

## 已知限制

- 逐步生成不是原生“多件联合约束”。后一步扩散可能改动前一步的文字、logo、褶皱或叠穿边界，最终图必须人工复核。
- `tryon-v1.6` 只正式支持 tops / bottoms / one-pieces；需要鞋、包、饰品时用 `tryon-max`。
- FASHN `fast + 1k` 是默认成本档；提高分辨率或质量会提高每步 credits 与耗时。
- fal 对鞋/包/饰品没有公开支持承诺，本实现不把它们偷偷当 clothing 送出。
- 阿里只传单件上装或下装时，服务可能随机生成另一半搭配；需要可控整套时同时提供上装和下装。
- 取消和本地超时能立即恢复 UI；服务端已经开始的任务可能仍由服务商完成并按其计费规则结算。

## 真实 A/B

设置同一人物、上装和下装路径，以及两家 Key，再显式确认可能计费：

    $env:PIXELFIT_BENCHMARK_PERSON='C:\images\person.png'
    $env:PIXELFIT_BENCHMARK_TOP='C:\images\top.png'
    $env:PIXELFIT_BENCHMARK_BOTTOM='C:\images\bottom.png'
    $env:FASHN_API_KEY='...'
    $env:DASHSCOPE_API_KEY='...'
    $env:PIXELFIT_BENCHMARK_ACK='I_ACCEPT_PROVIDER_CHARGES'
    npm run benchmark:tryon

结果写入 benchmark-out，包含两张真实输出和待人工评分的 benchmark.json。未设置确认值时脚本在发出任何请求前失败。

## 验证命令

```powershell
npm test
npm run typecheck
npm run build
npm run shots
```

`npm run shots` 不调用付费 API，只验证未配置状态、模式开关和上传授权弹窗。
