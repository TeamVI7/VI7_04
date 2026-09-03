// Datamosh — the compression artefact you get when a video's keyframes are stripped out
// and only the motion data survives. Blocks stop being refreshed with new pixels and just
// keep sliding along whatever motion vector they were handed, so the image bleeds and
// smears instead of updating.
//
// Pass 0 does the accumulation (previous mosh buffer + this frame's motion vectors),
// pass 1 copies the result back over the camera colour.
Shader "Hidden/VI7/Datamosh"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        HLSLINCLUDE
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // _BlitTexture is the previous frame's mosh buffer — Blitter binds it for us.
            TEXTURE2D_X(_MoshSource);   // this frame's camera colour
            TEXTURE2D_X(_MoshMotion);   // URP motion vectors

            float4 _MoshParams;   // x = intensity, y = smear, z = reset, w = frame seed
            float4 _MoshBlocks;   // xy = block count, zw = 1 / block count

            float Hash21(float2 p)
            {
                p  = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }
        ENDHLSL

        Pass
        {
            Name "DatamoshAdvect"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragAdvect

            float4 FragAdvect(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv      = input.texcoord;
                float3 current = SAMPLE_TEXTURE2D_X(_MoshSource, sampler_LinearClamp, uv).rgb;

                // First frame of the effect: seed the buffer with a clean image, otherwise
                // the mosh smears whatever stale garbage the buffer happened to hold.
                if (_MoshParams.z > 0.5)
                    return float4(current, 1.0);

                float2 block   = floor(uv * _MoshBlocks.xy);
                float2 blockUV = (block + 0.5) * _MoshBlocks.zw;

                // One motion vector per macroblock, sampled at the block centre. Sampling
                // per-pixel would give a smooth motion blur; the whole block having to move
                // together is exactly what makes this read as a codec falling apart.
                float2 mv = SAMPLE_TEXTURE2D_X(_MoshMotion, sampler_PointClamp, blockUV).xy * _MoshParams.y;

                float2 prevUV = clamp(uv - mv, 0.0, 1.0);
                float3 prev   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, prevUV).rgb;

                // Every block rolls against the intensity each frame. At low intensity most
                // blocks win and take the fresh frame (an I-block); as it climbs, fewer and
                // fewer do, so the corruption spreads across the screen instead of snapping
                // on all at once.
                float roll  = Hash21(block + _MoshParams.w);
                float stale = step(roll, _MoshParams.x);

                float3 col = lerp(current, prev, stale);

                // Push the residual back in on frozen blocks. Without this the stale regions
                // settle into a flat still frame; the codec they are imitating keeps applying
                // difference data it can no longer resolve, which is what makes the colours
                // slide and bruise.
                col += (prev - current) * stale * _MoshParams.x * 0.15;

                return float4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DatamoshComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            float4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return float4(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord).rgb, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
