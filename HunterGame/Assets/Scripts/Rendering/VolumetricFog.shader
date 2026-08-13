Shader "Hunter/VolumetricFog"
{
    // Ray-marched single-scattering fog modulated by the main light's shadow map.
    // URP ships no volumetric solution, and the god rays cutting through the gold mist
    // are the defining feature of the Aurum Mist concept frame, so this is hand written.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "VolumetricScattering"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _VolFogParams;      // x: density, y: step count, z: max distance, w: anisotropy
            float4 _VolFogColor;       // rgb: scattering tint, a: intensity
            float4 _VolFogHeight;      // x: base height, y: falloff, z: noise scale, w: noise strength
            float4 _VolFogLightBoost;  // x: sun boost, y: ambient floor, z: point light gain, w: unused

            // Henyey-Greenstein: forward scattering is what makes a beam look like a beam
            // instead of a uniform glow.
            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = 1.0 + g2 - 2.0 * g * cosTheta;
                return (1.0 - g2) / (4.0 * PI * pow(max(denom, 1e-4), 1.5));
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float2 uv = i.xy + float2(37.0, 17.0) * i.z + f.xy;
                float a = Hash(uv);
                float b = Hash(uv + float2(1.0, 0.0));
                float c = Hash(uv + float2(0.0, 1.0));
                float d = Hash(uv + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float FogDensityAt(float3 worldPos)
            {
                float height = exp(-max(worldPos.y - _VolFogHeight.x, 0.0) * _VolFogHeight.y);

                // Slow drifting billows keep the volume from looking like a uniform haze.
                float3 np = worldPos * _VolFogHeight.z + float3(_Time.y * 0.014, _Time.y * 0.006, 0.0);
                float noise = ValueNoise(np) * 0.6 + ValueNoise(np * 2.7) * 0.4;
                float billow = lerp(1.0, noise * 1.7, saturate(_VolFogHeight.w));

                return height * billow;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float rawDepth = SampleSceneDepth(uv);
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                float3 cameraPos = GetCameraPositionWS();
                float3 toSurface = worldPos - cameraPos;
                float sceneDistance = length(toSurface);

                // Skybox pixels come back at (or beyond) the far plane; clamp them so the
                // march still terminates at a sane distance.
                float maxDistance = _VolFogParams.z;
                float marchDistance = min(sceneDistance, maxDistance);
                if (rawDepth <= 1e-6) marchDistance = maxDistance;

                float3 rayDir = toSurface / max(sceneDistance, 1e-4);

                int steps = (int)_VolFogParams.y;
                float stepSize = marchDistance / steps;

                // Per-pixel jitter trades banding for noise, which the bloom pass then hides.
                float jitter = Hash(uv * _ScreenParams.xy + _Time.y);

                Light mainLight = GetMainLight();
                float3 lightDir = mainLight.direction;
                float cosTheta = dot(rayDir, lightDir);
                float phase = HenyeyGreenstein(cosTheta, _VolFogParams.w);

                float3 scattered = 0.0;
                float transmittance = 1.0;
                float extinction = _VolFogParams.x;

                UNITY_LOOP
                for (int i = 0; i < steps; i++)
                {
                    float t = (i + jitter) * stepSize;
                    float3 samplePos = cameraPos + rayDir * t;

                    float density = FogDensityAt(samplePos) * extinction;
                    if (density < 1e-5) continue;

                    float4 shadowCoord = TransformWorldToShadowCoord(samplePos);
                    float shadow = MainLightRealtimeShadow(shadowCoord);

                    float3 inScatter = _VolFogColor.rgb * mainLight.color
                                     * (shadow * phase * _VolFogLightBoost.x + _VolFogLightBoost.y);

                    // Point lights (the lantern, loot glow) bleed into the volume too;
                    // without this the lantern lights the floor but not the air around it.
                    #if defined(_ADDITIONAL_LIGHTS)
                    uint lightCount = GetAdditionalLightsCount();
                    UNITY_LOOP
                    for (uint li = 0u; li < lightCount; li++)
                    {
                        Light addLight = GetAdditionalLight(li, samplePos);
                        float addPhase = HenyeyGreenstein(dot(rayDir, addLight.direction), _VolFogParams.w * 0.5);
                        inScatter += addLight.color * addLight.distanceAttenuation
                                   * addPhase * _VolFogLightBoost.z;
                    }
                    #endif

                    float stepTransmittance = exp(-density * stepSize);
                    scattered += transmittance * inScatter * density * stepSize;
                    transmittance *= stepTransmittance;

                    if (transmittance < 0.008) break;
                }

                float3 result = sceneColor.rgb * transmittance + scattered * _VolFogColor.a;
                return half4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
