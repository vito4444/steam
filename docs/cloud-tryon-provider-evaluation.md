# PixelFit 云试穿供应商评估

核对日期：2026-08-19。价格会变化；美元折算统一用 **US$1 = ¥7.00**，只为横向比较，不是实时汇率。

## 结论

**中国大陆自备 Key 首选阿里云百炼 aitryon-plus；FASHN 保留为鞋/包/配饰等完整品类方案。在完成同一人物、同一服装的真实 A/B 前，不自动替换生产默认。**

理由：阿里是候选中少数公开提供专用试衣契约的大陆平台，支持一次组合上装+下装、restore_face、人民币按成功图片计费和北京区临时上传；相对腾讯 MPS，不需要 SecretId/SecretKey、服务角色和 COS/MPS 权限链。它的明确短板是只覆盖上装、下装、连衣裙/连体衣。

## 能力、价格与接入判断

| 平台 / 模型 | 专用试穿 | 公开输入与品类 | 单次价格 | 大陆用户 / 支付 / 资质 | PixelFit 判断 |
|---|---|---|---:|---|---|
| **FASHN Try-On Max** | 是 | 人物 + 单件 product；衣物、鞋、帽、首饰、包和 wearable | fast 1K：1 credit，**US$0.075/步（约 ¥0.525）**；100 credits 起购 US$7.50 | 自助 API；公开 onboarding 未写企业资质要求。人民币/大陆支付与大陆网络稳定性无官方承诺 | 品类最全；两件上/下装需顺序调用 2 步，约 US$0.15 |
| **阿里百炼 OutfitAnyone Plus** | 是 | 人物 + 上装、下装；上+下可一次组合；连衣裙/连体衣放上装字段；无鞋/包/配饰承诺 | **¥0.50/成功图（约 US$0.071）**；符合资格的新开通账号 90 天 400 张 | 北京区 API Key、阿里云主账号、人民币按量；公开接入前提未写企业资质，个人账号可行性是基于自助账号流程的推断 | **首个国内适配器**；2 件上/下装仍只生成/计费 1 张 |
| **腾讯云 MPS AI Try-On / WAND-tryon-1.0** | 是 | 人物 + 最多 13 张参考；garment/background/identity/pose/accessories/shoes | 产品页“¥0.25/张起”，具体规格需控制台复核 | 国内账号/人民币；需开通 MPS、服务角色、SecretId/SecretKey 和 URL/COS 链路 | 能力很强，作为第二国内适配器；首期授权与存储复杂度更高 |
| **SiliconFlow Qwen-Image-Edit-2509** | 否，通用编辑 | 最多 3 张参考图，可用人物+衣物 prompt 模拟 | **¥0.30/图（约 US$0.043）**；Kolors 文生图免费但不接参考衣物 | 支持个人实名，支付宝人脸；未实名不能充值/开票 | 价格低、国内友好，但无身份/服装保持契约，不能当生产 VTO |
| **智谱 CogView-4 / GLM-Image** | 否 | 当前公开生图接口是文本输入，没有人物+衣物参考图试穿契约 | CogView-4 **¥0.06/次**；GLM-Image **¥0.10/次** | 个人余额、人民币、支付宝/微信；充值需实名 | 无法满足“同一人物/同一衣物”，排除 |
| **OpenRouter Image API** | 否，聚合通用生图/编辑 | 可选 reference images；实际能力、参考图数量和价格按 endpoint 动态发现 | 官方示例约 **US$0.011**，endpoint 可显示 US$0.05 等；另有购币手续费 | 美元 credits；接受信用卡、Alipay、USDC；通常转发境外/第三方 provider | 适合快速试不同通用编辑模型，不适合作为确定性 VTO 契约 |

## 质量与品类评价方法

真实比较固定同一张正面全身人物图、同一张平铺上装、同一张平铺下装；不允许用供应商官方示例或 CERE-8 旧输出代替。每项人工 1–5 分：

1. 人物身份与脸部保持；
2. 服装版型、logo/文字和边缘保持；
3. 颜色与纹理相似度；
4. 手部、头发、遮挡与身体结构；
5. 上装+下装兼容性与叠穿关系；
6. 整体可用于衣橱预览的程度。

npm run benchmark:tryon 会对 FASHN/阿里使用完全相同输入，记录输入 SHA-256、耗时、provider request id、输出 SHA-256，并生成待打分 JSON。脚本必须显式设置 PIXELFIT_BENCHMARK_ACK=I_ACCEPT_PROVIDER_CHARGES，否则在任何网络请求前拒绝执行。

本轮环境没有 FASHN_API_KEY 或 DASHSCOPE_API_KEY，任务又禁止未确认充值，因此**没有产生真实 FASHN/阿里输出，也没有质量分数**。这不是把官方样例包装成实测的理由；真实 A/B 是唯一未满足的硬验收项。

## 数据、隐私与训练

| 平台 | 输入/输出保留与训练结论 | 风险判断 |
|---|---|---|
| FASHN | return_base64 模式下完整图片不写入请求历史，输出最多可取 60 分钟；官方称不使用 Customer Content 训练/微调，除非明确 opt-in；元数据仍保留 | 对人像较明确，但仍是境外服务与可能的受约束第三方处理 |
| 阿里百炼 | PixelFit 临时 OSS 输入 48 小时自动清理；任务数据和输出链接 24 小时保留；官方隐私声明称模型调用数据不用于训练 | 临时资产时限明确；一般账号/合规日志的具体删除期限未公开，不把 48 小时扩大解释成所有数据 |
| SiliconFlow | 最新隐私政策称交互数据归用户，未授权不用于训练、披露或存储；个人信息在中国境内按最短必要期限存储 | 国内数据路径友好，但具体图像 endpoint 的运行缓存期限未单列 |
| 智谱 | 用户协议允许匿名化数据用于产品改进/机器学习，未找到图片调用的明确零训练、零保留开关 | 对可识别人像不够确定，不推荐 |
| OpenRouter | 默认不存 prompt/response，保留请求元数据；可按 endpoint 强制 ZDR，ZDR provider 不能保留或训练；下游政策按 endpoint 变化 | 只有启用 ZDR 并固定符合条件的 endpoint 才可接受，不能把聚合器声明等同于所有下游 |
| 腾讯云 MPS | 输出 RMS 签名链接约 1 小时；输入 URL/COS 的生命周期由调用方控制；公开接口页未给训练用途承诺 | 需要在接入前补充产品级数据处理条款或合同确认 |

## 稳定性与工程风险

- 阿里 Plus 官方典型约 90 秒，提交 10 RPS、同一账号 5 个并发；临时上传凭证 100 QPS，但官方明确只建议开发/测试，生产需自有 OSS。
- FASHN fast 1K 官方典型约 10 秒/步；多件顺序调用会线性增加延迟和后一步改动前一步的风险。
- SiliconFlow 免费用户生图限流低，且通用编辑模型变化不能视为试穿 SLA。
- OpenRouter 的价格、能力和数据策略按实际 endpoint 动态变化，必须在每次选型时锁 provider tag 并重新检查。
- 上述公开页均没有足够的端到端可用性 SLA 供 PixelFit 承诺；应用必须保留超时、取消、缓存和本地分层回退。

## 官方来源

- [FASHN Try-On Max](https://docs.fashn.ai/api-reference/tryon-max)
- [FASHN API pricing](https://help.fashn.ai/plans-and-pricing/api-pricing)
- [FASHN data retention](https://docs.fashn.ai/api-overview/data-retention-privacy)
- [阿里百炼 OutfitAnyone Plus API](https://help.aliyun.com/zh/model-studio/aitryon-plus-api)
- [阿里百炼 AI 试衣计费、免费额度与限流](https://help.aliyun.com/zh/model-studio/billing-for-outfitanyone)
- [阿里百炼临时文件上传与 48 小时清理](https://help.aliyun.com/zh/model-studio/get-temporary-file-url)
- [阿里百炼隐私声明](https://help.aliyun.com/zh/model-studio/privacy-notice)
- [腾讯云 MPS AI 试穿 API](https://cloud.tencent.com/document/product/862/132594)
- [腾讯云 AI 创作产品价格入口](https://cloud.tencent.com/product/aiart)
- [SiliconFlow 图片生成 API](https://api-docs.siliconflow.cn/docs/api/images-generations-post)
- [SiliconFlow 当前生图价格](https://siliconflow.cn/pricing)
- [SiliconFlow 个人实名与充值限制](https://docs.siliconflow.com/en/faqs/authentication)
- [SiliconFlow 隐私政策](https://api-docs.siliconflow.cn/docs/legals/privacy-policy)
- [智谱 CogView-4](https://docs.bigmodel.cn/cn/guide/models/image-generation/cogview-4)
- [智谱 GLM-Image](https://docs.bigmodel.cn/cn/guide/models/image-generation/glm-image)
- [智谱充值协议](https://docs.bigmodel.cn/cn/terms/recharge-agreement)
- [OpenRouter Image API](https://openrouter.ai/docs/guides/overview/multimodal/image-generation)
- [OpenRouter 数据收集](https://openrouter.ai/docs/guides/privacy/data-collection)
- [OpenRouter Zero Data Retention](https://openrouter.ai/docs/guides/features/zdr)
- [OpenRouter 支付 FAQ](https://openrouter.ai/docs/faq)
