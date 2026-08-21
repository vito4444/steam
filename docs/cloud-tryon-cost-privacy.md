# 云端试穿：成本、耗时与隐私对照

核对日期：2026-08-19。价格与条款会变，正式上线前必须再次核对官方页面。完整候选矩阵见 cloud-tryon-provider-evaluation.md。

## 一句话推荐

**中国大陆自备 Key 首选阿里百炼 OutfitAnyone Plus；需要鞋、包、配饰时保留 FASHN Try-On Max。在真实同输入 A/B 前不自动切换默认。**

## 当前公开价格与耗时

| Provider / 模型 | 当前单价 | 官方典型耗时 | 公开支持范围 | PixelFit 四件搭配估算 |
|---|---:|---:|---|---:|
| 阿里百炼 OutfitAnyone Plus | **¥0.50/成功图（约 US$0.071）** | 约 **90 秒/图** | 上装、下装、上+下组合、连衣裙/连体衣 | 上+下 1 张约 **¥0.50**；鞋/包不上传 |
| FASHN Try-On Max，fast + 1k | 1 credit = **US$0.075/步** | 约 **10 秒/步** | 衣物、鞋、帽、首饰、包及其他 wearable | 4 步约 **US$0.30 / 40 秒** |
| FASHN Try-On Max，balanced + 1k | 2 credits = **US$0.15/步** | 官方只给 `balanced + 2k` 约 25 秒，1k 未单列 | 同上 | 4 步 **US$0.60**；耗时待 key 实测 |
| FASHN Try-On v1.6 | 1 credit = **US$0.075/步** | performance 约 5 秒；balanced 约 8 秒；quality 约 12–17 秒 | tops / bottoms / one-pieces | 只适合 2–3 件衣物；不承诺鞋/包/饰品 |
| fal Image Apps V2 Virtual Try-On | **US$0.04/图** | 官方模型页未给稳定 SLA，待 key 实测 | person + clothing image | 4 件受支持衣物约 **US$0.16**；鞋/包/饰品跳过 |

一次完整搭配通常建议只生成 1 张最终图；质量不稳时再由用户明确点“重试”，不自动批量出 4 张。按默认 FASHN fast + 1k，1,000 套四件搭配的纯生成费约 **US$300**，未含税、失败重试和汇率成本。FASHN 失败预测不消耗 credits；按需 API credits 最低购买 100 credits / US$7.50，不要求月订阅。

## 照片传到哪里、留多久

| 项目 | FASHN | 阿里百炼 | fal |
|---|---|---|---|
| 接收方 | FASHN LTD 的 api.fashn.ai；条款说明可能使用受约束的第三方 AI 服务 | 百炼北京区 DashScope + 临时 OSS | fal 的 queue/model API 及其模型运行基础设施 |
| PixelFit 输入方式 | data URI（人物 + 衣物） | data URI 先上传为 48 小时 oss URI | data URI（人物 + 衣物） |
| 请求历史 | 完整 base64 图片不写入历史；时间、状态、模型、参数等元数据仍在历史中 | 临时输入 48 小时；一般合规日志删除期未公开 | 默认 JSON payload 保存 30 天；PixelFit 强制 X-Fal-Store-IO: 0 |
| 输出保留 | return_base64，状态接口最多可取 60 分钟 | 任务和输出 URL 24 小时 | PixelFit 请求 1 小时过期；下载后结果仅保存在本机缓存 |
| 模型训练 | 官方说明不使用 Customer Content 训练或微调，除非客户明确 opt-in | 官方隐私声明称调用数据不用于训练 | 需以 fal 账户/合同和具体模型条款为准 |
| 商用 | 可集成；输入必须有权利/同意 | 按阿里云服务条款；输入必须有权利/同意 | endpoint 标为 Commercial use；输入必须有权利/同意 |

FASHN 的公开 API 面向企业和开发者，但公开 onboarding 没有要求企业资质或中国境内备案；可直接采用**用户自备 key**。如果 PixelFit 以后替用户统一提供账号/key，就必须再审查 DPA、subprocessor list、数据地域、未成年人和跨境传输义务。

## 应用内控制

- 默认引擎为“即时预览”，不会上传。
- 切到“AI 高清”仍不会上传；首次生成必须勾选一次会话授权。
- 弹窗明确列出上传对象、服务商、保留摘要、输入权利和生成误差。
- 没 key、断网、失败、超时或取消时继续显示本地预览。
- 成功结果按内容哈希缓存在本机，同一套不重复调用。
- fal 不支持的鞋/包/饰品在生成前列出并跳过，不上传、不计费。

## 官方来源

- FASHN Try-On Max：https://docs.fashn.ai/api-reference/tryon-max
- FASHN Try-On v1.6：https://docs.fashn.ai/api-reference/tryon-v1-6
- FASHN API 价格：https://help.fashn.ai/plans-and-pricing/api-pricing
- FASHN 数据保留：https://docs.fashn.ai/api-overview/data-retention-privacy
- FASHN 服务条款：https://fashn.ai/legal/terms-of-service
- fal Image Apps V2：https://fal.ai/models/fal-ai/image-apps-v2/virtual-try-on
- fal 平台 headers：https://fal.ai/docs/documentation/model-apis/common-parameters
- fal 数据保留：https://fal.ai/docs/documentation/model-apis/media-expiration
- 阿里 AI 试衣 API：https://help.aliyun.com/zh/model-studio/aitryon-plus-api
- 阿里计费与限流：https://help.aliyun.com/zh/model-studio/billing-for-outfitanyone
- 阿里临时文件：https://help.aliyun.com/zh/model-studio/get-temporary-file-url
