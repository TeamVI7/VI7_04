Shader "Knife/Distortion_URP"
{
	Properties
	{
		_NormalMap("NormalMap", 2D) = "bump" {}
		_NormalMap2("NormalMap2", 2D) = "bump" {}
		_DistortionAmount2("DistortionAmount2", Float) = 1
		_DistortionAmount("DistortionAmount", Float) = 0.2
		_AlphaMask("AlphaMask", 2D) = "white" {}
		[Toggle(_TWONORMALS_ON)] _TwoNormals("TwoNormals", Float) = 0
		_DistortionSpeed2("DistortionSpeed2", Vector) = (0,0,0,0)
		_DistortionSpeed("DistortionSpeed", Vector) = (0,0,0,0)
		[Toggle(_DEBUG_ON)] _Debug("Debug", Float) = 0
		[Toggle(_SCREENSPACEUV_ON)] _ScreenSpaceUV("ScreenSpaceUV", Float) = 0
		_Tiling2("Tiling2", Float) = 1
		_Tiling1("Tiling1", Float) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
		Cull Back
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

		Pass
		{
			Name "Unlit"
			Tags{ "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma shader_feature _DEBUG_ON
			#pragma shader_feature _SCREENSPACEUV_ON
			#pragma shader_feature _TWONORMALS_ON

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

			TEXTURE2D(_NormalMap);      SAMPLER(sampler_NormalMap);
			TEXTURE2D(_NormalMap2);     SAMPLER(sampler_NormalMap2);
			TEXTURE2D(_AlphaMask);      SAMPLER(sampler_AlphaMask);

			CBUFFER_START(UnityPerMaterial)
				float4 _NormalMap_ST;
				float4 _NormalMap2_ST;
				float4 _AlphaMask_ST;
				float _DistortionAmount;
				float _DistortionAmount2;
				float2 _DistortionSpeed;
				float2 _DistortionSpeed2;
				float _Tiling1;
				float _Tiling2;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 color : TEXCOORD1;
				float4 screenPos : TEXCOORD2;
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.positionOS.xyz);
				OUT.positionHCS = vertexInput.positionCS;
				OUT.uv = IN.uv;
				OUT.color = IN.color;
				OUT.screenPos = ComputeScreenPos(vertexInput.positionCS);
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				float2 screenUV = IN.screenPos.xy / IN.screenPos.w;

				float2 uv0_NormalMap = IN.uv * _NormalMap_ST.xy + _NormalMap_ST.zw;
				float2 uv0_NormalMap2 = IN.uv * _NormalMap2_ST.xy + _NormalMap2_ST.zw;
				float2 uv_AlphaMask = IN.uv * _AlphaMask_ST.xy + _AlphaMask_ST.zw;

				float4 alphaMaskTex = SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, uv_AlphaMask);

			#ifdef _SCREENSPACEUV_ON
				float2 baseUV1 = screenUV;
				float2 baseUV2 = screenUV;
			#else
				float2 baseUV1 = uv0_NormalMap;
				float2 baseUV2 = uv0_NormalMap2;
			#endif

				float2 panner1 = _Time.y * _DistortionSpeed + baseUV1 * _Tiling1;
				float2 panner2 = _Time.y * _DistortionSpeed2 + baseUV2 * _Tiling2;

				float scale1 = _DistortionAmount * IN.color.a * alphaMaskTex.r;
				float3 normal1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, panner1), scale1);

			#ifdef _TWONORMALS_ON
				float scale2 = _DistortionAmount2 * alphaMaskTex.r * IN.color.a;
				float3 normal2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap2, sampler_NormalMap2, panner2), scale2);
			#else
				float3 normal2 = float3(0, 0, 0);
			#endif

				float3 combinedNormal = normalize(normal1 + normal2);

				float2 distortedUV = screenUV + combinedNormal.xy;
				half3 screenColor = SAMPLE_TEXTURE2D_X(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, distortedUV).rgb;

			#ifdef _DEBUG_ON
				half4 result = alphaMaskTex;
			#else
				half4 result = half4(screenColor, 1);
			#endif

				return result;
			}
			ENDHLSL
		}
	}
	Fallback Off
}
