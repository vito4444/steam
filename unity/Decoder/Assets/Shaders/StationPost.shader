// 工位后处理。一遍过，全部效果在同一个片元着色器里完成，
// 因为多遍 Blit 在软件渲染下的带宽开销远大于算术开销。
//
// 效果的先后顺序不是随意排的，它模拟的是一条真实的成像链路：
// 先是镜头的几何畸变与色散，然后是镜头的渐晕，接着是感光介质的响应曲线，
// 最后才是胶片颗粒。顺序错了会得到很怪的结果，
// 比如把颗粒放在渐晕之前，画面四角的噪点会跟着一起被压暗，看上去像脏了而不是像胶片。
Shader "Decoder/StationPost"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float _Vignette;        // 渐晕强度
            float _VignetteSoft;    // 渐晕过渡的软硬
            float _Grain;           // 颗粒强度
            float _GrainTime;       // 每帧变化的种子
            float _Aberration;      // 色散强度，单位是纹素
            float _Scanline;        // 扫描线强度
            float _ScanlineCount;   // 扫描线条数
            float _Barrel;          // 桶形畸变
            float _Lift;            // 暗部抬升
            float _Gain;            // 亮部增益
            float _Saturation;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }

            // 桶形畸变。玩家是在看一台 CRT 显示器，画面本身该是微微鼓起的。
            //
            // 必须配一个 overscan 补偿：畸变把采样点往外推，画面四角会落到源图之外，
            // 结果是四个黑角。补偿系数按角落处的最大形变量算，
            // 角落的 r2 是 2，所以缩放 1/(1+2a) 恰好把最外的采样点拉回边界内。
            float2 BarrelDistort(float2 uv, float amount)
            {
                float2 centered = uv * 2.0 - 1.0;
                float r2 = dot(centered, centered);
                centered *= (1.0 + amount * r2) / (1.0 + 2.0 * amount);
                return centered * 0.5 + 0.5;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = BarrelDistort(i.uv, _Barrel);

                // 畸变会把采样点推到画面外，直接返回黑色而不是钳制，
                // 钳制会在边缘拉出一圈拖影。
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                {
                    return fixed4(0, 0, 0, 1);
                }

                // 横向色散。真实镜头的色散随离轴距离增大，中心几乎没有，
                // 所以要乘上到中心的距离，否则整幅画面都发虚。
                float2 fromCenter = uv - 0.5;
                float offAxis = length(fromCenter) * 2.0;
                float2 shift = normalize(fromCenter + 1e-6) * _Aberration
                               * _MainTex_TexelSize.xy * offAxis;

                fixed4 col;
                col.r = tex2D(_MainTex, uv + shift).r;
                col.g = tex2D(_MainTex, uv).g;
                col.b = tex2D(_MainTex, uv - shift).b;
                col.a = 1.0;

                // 扫描线。用余弦而不是方波，方波在缩放时会产生严重摩尔纹。
                float scan = cos(uv.y * _ScanlineCount * 6.28318) * 0.5 + 0.5;
                col.rgb *= lerp(1.0, scan, _Scanline);

                // 渐晕
                float vig = 1.0 - smoothstep(_VignetteSoft, 1.4, offAxis);
                col.rgb *= lerp(1.0, vig, _Vignette);

                // 影调曲线：抬暗部、压亮部，模拟胶片的肩部与趾部。
                // 这一步在渐晕之后，因为渐晕压出来的暗部也该走同一条曲线。
                col.rgb = col.rgb * _Gain + _Lift;
                col.rgb = col.rgb / (1.0 + col.rgb * 0.35);

                float luma = dot(col.rgb, float3(0.2126, 0.7152, 0.0722));
                col.rgb = lerp(luma.xxx, col.rgb, _Saturation);

                // 颗粒。放在最后，并且在暗部更明显——
                // 真实胶片的颗粒噪声在欠曝区域最突出，高光区几乎看不见。
                // 颗粒在暗部更明显，但不能让暗部满屏噪点——这个房间本来就大半是暗的。
                // 保留一个常数底，让高光区也有一点颗粒，整体才像同一张底片。
                float n = Hash21(uv * _ScreenParams.xy + _GrainTime);
                float grainWeight = _Grain * (0.35 + 0.65 * (1.0 - smoothstep(0.02, 0.45, luma)));
                col.rgb += (n - 0.5) * grainWeight;

                return saturate(col);
            }
            ENDCG
        }
    }

    Fallback Off
}
