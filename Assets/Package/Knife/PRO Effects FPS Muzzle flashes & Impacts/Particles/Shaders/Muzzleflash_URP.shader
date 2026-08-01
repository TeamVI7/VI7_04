Shader "Knife/MuzzleFlash_URP"
{
	Properties
	{
		_Noise("Noise", 2D) = "white" {}
		_Noise1("Noise1", 2D) = "white" {}
		_Alpha("Alpha", 2D) = "white" {}
		[HDR]_Color0("Color 0", Color) = (1,1,1,1)
		[HDR]_Color1("Color 1", Color) = (1,1,1,1)
		_Opacity("Opacity", Range( 0 , 1)) = 1
		_NoiseSoftness1("NoiseSoftness1", Range( 0 , 1)) = 0
		_NoiseSoftness2("NoiseSoftness2", Range( 0 , 1)) = 0
		_NoiseSpeed1("NoiseSpeed1", Vector) = (0,1,0,0)
		_NoiseSpeed("NoiseSpeed", Vector) = (0,1,0,0)
		_DepthFade("DepthFade", Float) = 0
		_AlphaSoftness("AlphaSoftness", Range( 0 , 1)) = 1
		[Normal]_Distortion("Distortion", 2D) = "bump" {}
		_DistortionAmount("DistortionAmount", Range( 0 , 1)) = 0
		_DistortionDiff("DistortionDiff", Float) = 0
		_DistortionSpeed1("DistortionSpeed1", Vector) = (0,0,0,0)
		_DistortionSpeed2("DistortionSpeed2", Vector) = (0,0,0,0)
		_CenterFadeSize("CenterFadeSize", Range( -1 , 1)) = 0
		_CenterNoiseFadeSize("CenterNoiseFadeSize", Range( -1 , 1)) = 0
		_CenterNoiseFadeSoftness("CenterNoiseFadeSoftness", Range( 0 , 1)) = 0
		_CenterFadeSoftness("CenterFadeSoftness", Range( 0 , 1)) = 0
		_DissolveSoftness("DissolveSoftness", Range( 0 , 1)) = 0
	}

	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
		LOD 0

		Blend SrcAlpha OneMinusSrcAlpha
		Cull Back
		ColorMask RGBA
		ZWrite Off
		ZTest LEqual
		Offset 0 , 0

		Pass
		{
			Name "Unlit"
			Tags { "LightMode"="UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			TEXTURE2D(_Noise);        SAMPLER(sampler_Noise);
			TEXTURE2D(_Noise1);       SAMPLER(sampler_Noise1);
			TEXTURE2D(_Alpha);        SAMPLER(sampler_Alpha);
			TEXTURE2D(_Distortion);   SAMPLER(sampler_Distortion);

			CBUFFER_START(UnityPerMaterial)
				float4 _Color0;
				float4 _Color1;
				float _NoiseSoftness1;
				float _NoiseSoftness2;
				float2 _NoiseSpeed1;
				float4 _Noise1_ST;
				float2 _NoiseSpeed;
				float4 _Noise_ST;
				float _CenterNoiseFadeSize;
				float _CenterNoiseFadeSoftness;
				float _AlphaSoftness;
				float4 _Alpha_ST;
				float _DistortionAmount;
				float _CenterFadeSize;
				float _CenterFadeSoftness;
				float4 _Distortion_ST;
				float2 _DistortionSpeed1;
				float2 _DistortionSpeed2;
				float _DistortionDiff;
				float _Opacity;
				float _DepthFade;
				float _DissolveSoftness;
			CBUFFER_END

			struct appdata
			{
				float4 vertex : POSITION;
				float4 color : COLOR;
				float4 ase_texcoord : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
				float4 ase_color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata v)
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				UNITY_TRANSFER_INSTANCE_ID(v, o);

				VertexPositionInputs vertexInput = GetVertexPositionInputs(v.vertex.xyz);
				o.vertex = vertexInput.positionCS;
				o.ase_texcoord2 = ComputeScreenPos(vertexInput.positionCS);
				o.ase_texcoord1 = v.ase_texcoord;
				o.ase_color = v.color;
				return o;
			}

			half4 frag(v2f i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);

				float2 uv0_Noise1 = i.ase_texcoord1.xy * _Noise1_ST.xy + _Noise1_ST.zw;
				float2 panner80 = _Time.y * _NoiseSpeed1 + uv0_Noise1;
				float2 uv0_Noise = i.ase_texcoord1.xy * _Noise_ST.xy + _Noise_ST.zw;
				float2 panner24 = _Time.y * _NoiseSpeed + uv0_Noise;

				float2 uv0180 = i.ase_texcoord1.xy * float2(1, 1) + float2(-0.5, -0.5);
				float smoothstepResult178 = smoothstep(_CenterNoiseFadeSize, (_CenterNoiseFadeSize + _CenterNoiseFadeSoftness), length((uv0180 * float2(2, 2))));
				float CenterNoiseFade179 = smoothstepResult178;

				float noiseSample1 = SAMPLE_TEXTURE2D(_Noise1, sampler_Noise1, panner80).r;
				float noiseSample = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, panner24).r;
				float lerpResult173 = lerp(0.0, ((noiseSample1 + noiseSample) / 2.0), CenterNoiseFade179);
				float smoothstepResult11 = smoothstep(_NoiseSoftness1, _NoiseSoftness2, lerpResult173);
				float4 lerpResult9 = lerp(_Color0, _Color1, smoothstepResult11);

				float2 uv0_Alpha = i.ase_texcoord1.xy * _Alpha_ST.xy + _Alpha_ST.zw;
				float2 uv0116 = i.ase_texcoord1.xy * float2(1, 1) + float2(-0.5, -0.5);
				float smoothstepResult120 = smoothstep(_CenterFadeSize, (_CenterFadeSize + _CenterFadeSoftness), length((uv0116 * float2(2, 2))));
				float CenterFade126 = smoothstepResult120;
				float DistortionAmount115 = (_DistortionAmount * CenterFade126);

				float2 uv0_Distortion = i.ase_texcoord1.xy * _Distortion_ST.xy + _Distortion_ST.zw;
				float2 panner107 = _Time.y * _DistortionSpeed1 + uv0_Distortion;
				float2 panner108 = _Time.y * _DistortionSpeed2 + (uv0_Distortion * _DistortionDiff);

				float3 distTex1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_Distortion, sampler_Distortion, panner107), DistortionAmount115);
				float3 distTex2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_Distortion, sampler_Distortion, panner108), DistortionAmount115);
				float2 DistortionOffset113 = distTex1.xy + distTex2.xy;

				float smoothstepResult69 = smoothstep(0.0, _AlphaSoftness, SAMPLE_TEXTURE2D(_Alpha, sampler_Alpha, (uv0_Alpha + DistortionOffset113)).r);

				float4 screenPos = i.ase_texcoord2;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = (UNITY_NEAR_CLIP_VALUE >= 0) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;

				float rawDepth50 = SampleSceneDepth(ase_screenPosNorm.xy);
				float screenDepth50 = LinearEyeDepth(rawDepth50, _ZBufferParams);
				float distanceDepth50 = abs((screenDepth50 - LinearEyeDepth(ase_screenPosNorm.z, _ZBufferParams)) / (_DepthFade));
				float clampResult52 = clamp(distanceDepth50, 0.0, 1.0);

				float smoothstepResult58 = smoothstep(_NoiseSoftness1, _NoiseSoftness2, lerpResult173);
				float clampResult86 = clamp((smoothstepResult69 - smoothstepResult58), 0.0, 1.0);

				float4 temp_output_13_0 = (lerpResult9 * (smoothstepResult69 * _Opacity * clampResult52 * clampResult86) * i.ase_color);

				float4 uv0130 = i.ase_texcoord1;
				uv0130.xy = i.ase_texcoord1.xy * float2(1, 1) + float2(-0.5, -0.5);
				float temp_output_143_0 = (1.0 - (length((uv0130).xy) * 2.0));
				float DissolveHide156 = uv0130.w;
				float smoothstepResult150 = smoothstep(0.0, _DissolveSoftness, (temp_output_143_0 + DissolveHide156));
				float DissolveShow139 = uv0130.z;
				float smoothstepResult154 = smoothstep(0.0, _DissolveSoftness, (temp_output_143_0 + DissolveShow139));
				float clampResult148 = clamp((smoothstepResult150 + (1.0 - smoothstepResult154)), 0.0, 1.0);
				float FinalDissolve146 = clampResult148;

				float clampResult134 = clamp(((temp_output_13_0).a - FinalDissolve146), 0.0, 1.0);
				float4 appendResult132 = float4((temp_output_13_0).rgb, clampResult134);

				return appendResult132;
			}
			ENDHLSL
		}
	}
	Fallback Off
}
