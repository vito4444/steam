# 素材包接入契约（给 CERE-10）

应用侧已经按这份契约实现完毕：素材包放进来就能用，**不需要改任何代码**。
应用不再自带手绘占位单品，衣橱里只会出现你交付的真实照片素材。

## 1. 素材包长什么样

```
pack/
  index.json
  navy_stripe.png      透明底 PNG，原样保留照片纹理
  white_jeans.png
  ...
```

`index.json`：

```jsonc
{
  "pack": "cere10-real-v1",
  "items": [
    {
      "name": "藏青条纹长袖",
      "category": "top",              // top/bottom/dress/outer/shoe/bag/headwear/
                                      // eyewear/neckwear/belt/gloves/legwear/underlayer
      "cutout": "navy_stripe.png",
      "subcategory": "针织",
      "attributes": {
        "sleeve": "long",             // none/cap/short/half/long
        "length": "hip",              // crop/waist/hip/thigh/knee/midi/ankle/floor
        "pattern": "stripe",          // solid/stripe/check/floral/dot/print/logo/other
        "fit": "regular"              // slim/regular/loose/oversize
      },
      "palette": { "dominant": "#2C3E58", "color_family": "blue" },
      "tags": ["条纹", "秋"],
      "source_photo": "orig_0012.jpg",
      "landmarks": {                  // 可选，见 §3
        "shoulder_l": { "x": 120, "y": 60 },
        "shoulder_r": { "x": 640, "y": 62 },
        "hem":        { "x": 380, "y": 900 }
      }
    }
  ]
}
```

导入方式二选一：

- 应用内：设置 → 导入素材包 → 选目录
- 命令行：`electron . --ingest <pack 目录>`（截图 / 联调脚本用）

## 2. 导入时应用做什么、不做什么

**做**（都是几何上必需的）：

1. 裁到 alpha 包围盒 —— 素材周围的透明留白会让「按身体宽度贴合」整体失准。
2. 生成 256px 缩略图。
3. 量出上沿 / 下沿 / 肩宽跨度 / 腰宽跨度（`landmarks` 缺省时）。

**不做**：不描边、不量化、不改色、不做任何风格化。原图纹理原样落库。

## 3. 你能提供 landmarks 的话，贴合会准一档

坐标是**素材位图自身的像素坐标**（裁剪前的原图坐标即可，应用会跟着平移）：

| 字段 | 含义 | 谁用 |
| --- | --- | --- |
| `shoulder_l` / `shoulder_r` | 肩点 | 上装 / 外套 / 连衣裙的宽度基准 |
| `waist_l` / `waist_r` | 腰头两端 | 下装的宽度基准 |
| `top_edge` | 衣服真正的上沿（不含碎屑） | 贴到肩线 / 腰线 |
| `hem` | 下摆 | 鞋贴地面、衣长校正 |

不给也能跑：应用会按行统计不透明像素跨度自己量一遍（只认跨度 ≥ 最大跨度 25%
的行，抠图碎屑不算数）。但**你量的一定比我猜的准**，尤其是摊平拍的裤子
（包围盒比腰宽得多）和袖子摊开的外套。

## 4. 抠图质量直接决定成品

应用只能羽化边缘，不能替你修图。以下问题会**原样出现在模特身上**：

- 领口 / 肩部残留的头发、背景碎片 → 变成飘在模特肩上的黑块
- 衣服中间的破洞 → 透出模特皮肤
- 边缘残留的白边 → 羽化能削掉一线，削不掉一片

如果一件素材抠不干净，宁可不放进包里。

## 5. 模特底图包

底图走另一条路：解压到 `%APPDATA%/PixelFit/library/base/<s|m|l>/`，应用启动即接管。
`manifest.json`：

```jsonc
{
  "pack": "cere10-real-model-v1",
  "body": "m",
  "canvas": { "w": 1152, "h": 2304 },   // 画布尺寸由你定，应用不再假设固定值
  "anchors": {                          // 缺字段会按画布比例回落，但请给全
    "head_top": { "x": 576, "y": 96 },
    "eye_line": { "x": 576, "y": 250 },
    "chin": {}, "neck": {}, "shoulder_line": {},
    "shoulder_l": {}, "shoulder_r": {},
    "chest": {}, "waist": {}, "hip": {}, "crotch": {},
    "wrist_l": {}, "wrist_r": {}, "knee": {},
    "ankle_l": {}, "ankle_r": {}, "foot_base": {}
  },
  "tones": [                            // 肤色档位，只有一档也合法
    { "id": "t1", "name": "瓷白", "swatch": "#F2D7C6",
      "layers": [ { "file": "model_m_t1.png", "z": 20, "mode": "normal" } ] }
  ],
  "layers": [ { "file": "underlayer_m.png", "z": 24.5, "mode": "normal" } ],
  "hair": {}
}
```

- `mode`：`normal` 原样画（真人照片底图用这个）/ `fill` 当掩膜填色 / `multiply`
  灰度明暗图叠乘。
- **三种体型必须共用同一套锚点坐标**（体型只改宽度）。锚点一动，所有衣物的贴合
  都要重算。
- 衣物的缩放不读 anchors，而是**实测底图 alpha 轮廓**在锚点那一行的宽度。所以
  底图的轮廓必须干净：躯干与手臂在腰胯高度要分得开，否则量到的「腰宽」会把手臂
  算进去，衣服会整体偏大。

当前正式内置底图是 CERE-13 的 `base_f02`（默认）与 `base_m02`；CERE-6 的
`s` / `m` / `l` 只剩兼容性遗留且未完成再分发权利清理，处理建议见
`ATTRIBUTION.md`。

## 6. 来源与许可证是发布闸门

每个衣物条目和每套底图 manifest 都必须带 `provenance`；现有素材的完整示例与署名文案见仓库根目录 `ATTRIBUTION.md`。最低字段如下：

```jsonc
{
  "provenance": {
    "source": "来源站或生成流程",
    "page_url": "https://...",          // 来源页；确实不存在时写 null 并说明原因
    "original_url": "https://...",      // 原图；确实不存在时写 null
    "title": "原作标题",
    "creator": "作者名",
    "creator_url": "https://...",
    "license": "CC BY 2.0",
    "license_url": "https://...",       // 许可证或合同权利条款的正式页面
    "attribution": "可直接使用的完整署名文案；包含修改说明",
    "attribution_required": true,
    "commercial_use": true,
    "likeness_release": "not_applicable_no_identifiable_person_in_distributed_cutout",
    "transformation": "做过的全部实质变换",
    "sha256": "分发文件的 SHA-256",
    "reviewed_at": "YYYY-MM-DD"
  }
}
```

底图还必须明确 `synthetic`、`real_person` 和肖像授权依据。来源不明、许可证页面缺失、没有明确允许商业再分发、必需署名无法满足、或真人肖像授权依据缺失时，统一标 `unverified`，**不得进入发布包**；不要用推测值补齐字段。网页会变，发布证据中还要保存带日期的来源页与许可证页快照。
