Shader "Knife/Particle Specular_URP"
{
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("MainTex", 2D) = "white" {}
		_Cutout("Cutout", Range( 0 , 1)) = 0.5
		[NoScaleOffset]_NormalMap("NormalMap", 2D) = "bump" {}
		_NormalScale("NormalScale", Float) = 1
		[NoScaleOffset]_Specular("Specular", 2D) = "white" {}
		_Smoothness("Smoothness", Range( 0 , 1)) = 1
		_SpecularColor("SpecularColor", Color) = (0,0,0,0)
	}

	SubShader
	{
		Tags{ "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
		Cull Back

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _ADDITIONAL_LIGHTS
			#pragma multi_compile_fog
			#define _SPECULAR_SETUP 1

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
			TEXTURE2D(_NormalMap);  SAMPLER(sampler_NormalMap);
			TEXTURE2D(_Specular);   SAMPLER(sampler_Specular);

			CBUFFER_START(UnityPerMaterial)
				float _Cutout;
				float _NormalScale;
				float4 _MainTex_ST;
				float4 _Color;
				float4 _SpecularColor;
				float _Smoothness;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float4 tangentOS  : TANGENT;
				float4 color      : COLOR;
				float2 uv         : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float3 normalWS   : TEXCOORD1;
				float4 tangentWS  : TEXCOORD2;
				float4 vertexColor: TEXCOORD3;
				float2 uv         : TEXCOORD4;
			};

			Varyings vert(Attributes IN)
			{
				Varyings OUT;
				VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
				VertexNormalInputs normInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

				OUT.positionCS = posInputs.positionCS;
				OUT.positionWS = posInputs.positionWS;
				OUT.normalWS = normInputs.normalWS;
				OUT.tangentWS = float4(normInputs.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
				OUT.vertexColor = IN.color;
				OUT.uv = IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, IN.uv), _NormalScale);
				float3 bitangentWS = cross(IN.normalWS, IN.tangentWS.xyz) * IN.tangentWS.w;
				half3 normalWS = normalize(normalTS.x * IN.tangentWS.xyz + normalTS.y * bitangentWS + normalTS.z * IN.normalWS);

				half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
				half4 albedoColor = IN.vertexColor * _Color * mainTex;
				clip(albedoColor.a - _Cutout);

				half4 specTex = SAMPLE_TEXTURE2D(_Specular, sampler_Specular, IN.uv);
				half3 specular = (specTex + _SpecularColor).rgb;
				half smoothness = specTex.a * _Smoothness;

				InputData inputData = (InputData)0;
				inputData.positionWS = IN.positionWS;
				inputData.normalWS = normalWS;
				inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
				inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
				inputData.fogCoord = 0;
				inputData.vertexLighting = half3(0, 0, 0);
				inputData.bakedGI = SampleSH(normalWS);
				inputData.shadowMask = half4(1, 1, 1, 1);

				SurfaceData surfaceData = (SurfaceData)0;
				surfaceData.albedo = albedoColor.rgb;
				surfaceData.specular = specular;
				surfaceData.metallic = 0;
				surfaceData.smoothness = smoothness;
				surfaceData.normalTS = normalTS;
				surfaceData.emission = half3(0, 0, 0);
				surfaceData.occlusion = 1;
				surfaceData.alpha = 1;
				surfaceData.clearCoatMask = 0;
				surfaceData.clearCoatSmoothness = 1;

				return UniversalFragmentPBR(inputData, surfaceData);
			}
			ENDHLSL
		}

		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }
			ZWrite On
			ZTest LEqual
			ColorMask 0
			Cull Back

			HLSLPROGRAM
			#pragma vertex ShadowVert
			#pragma fragment ShadowFrag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

			CBUFFER_START(UnityPerMaterial)
				float _Cutout;
				float _NormalScale;
				float4 _MainTex_ST;
				float4 _Color;
				float4 _SpecularColor;
				float _Smoothness;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float4 color      : COLOR;
				float2 uv         : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float4 vertexColor: TEXCOORD0;
				float2 uv         : TEXCOORD1;
			};

			Varyings ShadowVert(Attributes IN)
			{
				Varyings OUT;
				VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
				OUT.positionCS = posInputs.positionCS;
				OUT.vertexColor = IN.color;
				OUT.uv = IN.uv * _MainTex_ST.xy + _MainTex_ST.zw;
				return OUT;
			}

			half4 ShadowFrag(Varyings IN) : SV_Target
			{
				half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
				half4 albedoColor = IN.vertexColor * _Color * mainTex;
				clip(albedoColor.a - _Cutout);
				return 0;
			}
			ENDHLSL
		}
	}
	Fallback Off
}
