Shader "Knife/Particle Channel Packed_URP"
{
	Properties
	{
		_Rows("Rows", Float) = 4
		_Columns("Columns", Float) = 4
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("MainTex", 2D) = "white" {}
		[Toggle(_MAINTEXSMOOTHSTEP_ON)] _MainTexSmoothstep("MainTexSmoothstep", Float) = 0
		_MainSoftnessMin("MainSoftnessMin", Range( 0 , 1)) = 0
		_MainSoftnessMax("MainSoftnessMax", Range( 0 , 1)) = 1
		_AlphaSoftness("AlphaSoftness", Range( 0 , 1)) = 0
		_DepthSoftness("DepthSoftness", Float) = 1
		[Toggle(_ALPHADISSOLVE_ON)] _AlphaDissolve("AlphaDissolve", Float) = 0
		[HDR]_Emission("Emission", Color) = (0,0,0,0)
		[Toggle(_EMISSIONDISSOLVE_ON)] _EmissionDissolve("EmissionDissolve", Float) = 0
		_EmissionTex("EmissionTex", 2D) = "white" {}
		_EmissionSoftness1("EmissionSoftness1", Range( 0 , 1)) = 0
		_EmissionSoftness2("EmissionSoftness2", Range( 0 , 1)) = 0
		[Toggle(_FINALALPHASMOOTHSTEP_ON)] _FinalAlphaSmoothstep("FinalAlphaSmoothstep", Float) = 0
		_FinalAlphaSmoothstepMin("FinalAlphaSmoothstepMin", Range( 0 , 1)) = 0
		_FinalAlphaSmoothstepMax("FinalAlphaSmoothstepMax", Range( 0 , 1)) = 1
		[Toggle(_EMISSIONALPHA_ON)] _EmissionAlpha("EmissionAlpha", Float) = 0
		[Toggle(_FINALEMISSIONSMOOTHSTEP_ON)] _FinalEmissionSmoothstep("FinalEmissionSmoothstep", Float) = 0
		_FinalEmissionSmoothstepMin("FinalEmissionSmoothstepMin", Range( 0 , 1)) = 0
		_FinalEmissionSmoothstepMax("FinalEmissionSmoothstepMax", Range( 0 , 1)) = 1
		[Toggle(_NORMALMAPENABLED_ON)] _NormalMapEnabled("Normal Map Enabled", Float) = 0
		_NormalMap("NormalMap", 2D) = "bump" {}
		_NormalScale("NormalScale", Float) = 0
		_EmissionSubValue("EmissionSubValue", Range( 0 , 1)) = 0
		[Toggle(_ALPHAEMISSIONDISSOLVESUB_ON)] _AlphaEmissionDissolveSub("Alpha Emission Dissolve Sub", Float) = 0
		_EmissionSpeed("EmissionSpeed", Vector) = (0,0,0,0)
		[Toggle(_ELIMINATEEMISSIONROTATION_ON)] _EliminateEmissionRotation("EliminateEmissionRotation", Float) = 0
		[Enum(UnityEngine.Rendering.CullMode)]_CullMode("Cull Mode", Float) = 2
	}

	SubShader
	{
		Tags{ "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
		Cull [_CullMode]
		Blend SrcAlpha OneMinusSrcAlpha
		ZWrite Off

		Pass
		{
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma shader_feature _NORMALMAPENABLED_ON
			#pragma shader_feature _EMISSIONALPHA_ON
			#pragma shader_feature _EMISSIONDISSOLVE_ON
			#pragma shader_feature _ELIMINATEEMISSIONROTATION_ON
			#pragma shader_feature _ALPHAEMISSIONDISSOLVESUB_ON
			#pragma shader_feature _ALPHADISSOLVE_ON
			#pragma shader_feature _MAINTEXSMOOTHSTEP_ON
			#pragma shader_feature _FINALEMISSIONSMOOTHSTEP_ON
			#pragma shader_feature _FINALALPHASMOOTHSTEP_ON
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _ADDITIONAL_LIGHTS
			#pragma multi_compile_fog

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
			TEXTURE2D(_EmissionTex);  SAMPLER(sampler_EmissionTex);
			TEXTURE2D(_NormalMap);    SAMPLER(sampler_NormalMap);

			CBUFFER_START(UnityPerMaterial)
				float _NormalScale;
				float _Columns;
				float _Rows;
				float4 _Color;
				float4 _Emission;
				float _EmissionSoftness1;
				float _EmissionSoftness2;
				float2 _EmissionSpeed;
				float4 _EmissionTex_ST;
				float _DepthSoftness;
				float _AlphaSoftness;
				float _MainSoftnessMin;
				float _MainSoftnessMax;
				float _EmissionSubValue;
				float _FinalEmissionSmoothstepMin;
				float _FinalEmissionSmoothstepMax;
				float _FinalAlphaSmoothstepMin;
				float _FinalAlphaSmoothstepMax;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS   : NORMAL;
				float4 tangentOS  : TANGENT;
				float4 color      : COLOR;
				float2 uv0        : TEXCOORD0;
				float2 uv1        : TEXCOORD1;
				float4 uv3        : TEXCOORD3; // tex4coord: xy=uv, z=AnimFrame, w=dissolve
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float3 normalWS   : TEXCOORD1;
				float4 tangentWS  : TEXCOORD2; // xyz tangent, w sign
				float4 vertexColor: TEXCOORD3;
				float2 uv0        : TEXCOORD4;
				float2 uv1        : TEXCOORD5;
				float4 uv3        : TEXCOORD6;
				float4 screenPos  : TEXCOORD7;
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
				OUT.uv0 = IN.uv0;
				OUT.uv1 = IN.uv1;
				OUT.uv3 = IN.uv3;
				OUT.screenPos = ComputeScreenPos(posInputs.positionCS);
				return OUT;
			}

			half4 frag(Varyings IN) : SV_Target
			{
				float columns135 = _Columns;
				float rows136 = _Rows;
				float AnimFrame4 = round(IN.uv3.z);

				float fbtotaltiles98 = columns135 * rows136;
				float fbcolsoffset98 = 1.0f / columns135;
				float fbrowsoffset98 = 1.0f / rows136;
				float fbspeed98 = _Time[1] * 0.0;
				float2 fbtiling98 = float2(fbcolsoffset98, fbrowsoffset98);
				float ChannelFramesCount103 = columns135 * rows136;
				float fbcurrenttileindex98 = round(fmod(fbspeed98 + (frac((AnimFrame4 / ChannelFramesCount103)) * ChannelFramesCount103), fbtotaltiles98));
				fbcurrenttileindex98 += (fbcurrenttileindex98 < 0) ? fbtotaltiles98 : 0;
				float fblinearindextox98 = round(fmod(fbcurrenttileindex98, columns135));
				float fboffsetx98 = fblinearindextox98 * fbcolsoffset98;
				float fblinearindextoy98 = round(fmod((fbcurrenttileindex98 - fblinearindextox98) / columns135, rows136));
				fblinearindextoy98 = (int)(rows136 - 1) - fblinearindextoy98;
				float fboffsety98 = fblinearindextoy98 * fbrowsoffset98;
				float2 fboffset98 = float2(fboffsetx98, fboffsety98);
				half2 fbuv98 = IN.uv3.xy * fbtiling98 + fboffset98;

			#ifdef _NORMALMAPENABLED_ON
				half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, fbuv98), _NormalScale);
			#else
				half3 normalTS = half3(0, 0, 1);
			#endif
				float3 bitangentWS = cross(IN.normalWS, IN.tangentWS.xyz) * IN.tangentWS.w;
				half3 normalWS = normalize(normalTS.x * IN.tangentWS.xyz + normalTS.y * bitangentWS + normalTS.z * IN.normalWS);

				float4 tex2DNode1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, fbuv98);
				float4 temp_cast_2 = (_MainSoftnessMin).xxxx;
				float4 temp_cast_3 = (_MainSoftnessMax).xxxx;
				float4 smoothstepResult233 = smoothstep(temp_cast_2, temp_cast_3, tex2DNode1);
			#ifdef _MAINTEXSMOOTHSTEP_ON
				float4 staticSwitch236 = smoothstepResult233;
			#else
				float4 staticSwitch236 = tex2DNode1;
			#endif
				float4 break152 = staticSwitch236;
				float Frames126 = columns135 * rows136;
				float temp_output_133_0 = (Frames126 - 1.0);
				float smoothstepResult23 = smoothstep(temp_output_133_0, temp_output_133_0, AnimFrame4);
				float lerpResult20 = lerp(break152.r, break152.g, smoothstepResult23);
				float Frames243 = (Frames126 * 2.0);
				float temp_output_123_0 = (Frames243 - 1.0);
				float smoothstepResult24 = smoothstep(temp_output_123_0, temp_output_123_0, AnimFrame4);
				float lerpResult21 = lerp(lerpResult20, break152.b, smoothstepResult24);
				float Frames344 = (Frames126 * 3.0);
				float temp_output_124_0 = (Frames344 - 1.0);
				float smoothstepResult25 = smoothstep(temp_output_124_0, temp_output_124_0, AnimFrame4);
				float lerpResult22 = lerp(lerpResult21, break152.a, smoothstepResult25);

				float smoothstepResult173 = smoothstep(0.0, _AlphaSoftness, lerpResult22);

				float4 ase_screenPosNorm = IN.screenPos / IN.screenPos.w;
				ase_screenPosNorm.z = (UNITY_NEAR_CLIP_VALUE >= 0) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;
				float rawDepth142 = SampleSceneDepth(ase_screenPosNorm.xy);
				float screenDepth142 = LinearEyeDepth(rawDepth142, _ZBufferParams);
				float distanceDepth142 = abs((screenDepth142 - LinearEyeDepth(ase_screenPosNorm.z, _ZBufferParams)) / (_DepthSoftness));
				float clampResult146 = clamp(distanceDepth142, 0.0, 1.0);
				float depthFadeAlpha163 = clampResult146;

				float clampResult166 = clamp(((depthFadeAlpha163 * smoothstepResult173) - (1.0 - IN.vertexColor.a)), 0.0, 1.0);
			#ifdef _ALPHADISSOLVE_ON
				float staticSwitch159 = (_Color.a * clampResult166);
			#else
				float staticSwitch159 = ((_Color.a * IN.vertexColor.a) * depthFadeAlpha163 * smoothstepResult173);
			#endif
				float finalAlpha248 = staticSwitch159;

			#ifdef _ALPHAEMISSIONDISSOLVESUB_ON
				float staticSwitch246 = (IN.uv3.w - (finalAlpha248 * _EmissionSubValue));
			#else
				float staticSwitch246 = IN.uv3.w;
			#endif

				float2 uv0_EmissionTex = IN.uv0 * _EmissionTex_ST.xy + _EmissionTex_ST.zw;
				float2 appendResult283 = float2(IN.uv1.y, IN.uv1.y);
				float cos277 = cos(IN.uv1.x);
				float sin277 = sin(IN.uv1.x);
				float2 rotIn = (uv0_EmissionTex + appendResult283) - float2(0.5, 0.5);
				float2 rotator277 = float2(
					rotIn.x * cos277 - rotIn.y * sin277,
					rotIn.x * sin277 + rotIn.y * cos277
				) + float2(0.5, 0.5);
			#ifdef _ELIMINATEEMISSIONROTATION_ON
				float2 staticSwitch279 = rotator277;
			#else
				float2 staticSwitch279 = uv0_EmissionTex;
			#endif
				float2 panner238 = _Time.y * _EmissionSpeed + staticSwitch279;
				float4 temp_cast_0 = (_EmissionSoftness1).xxxx;
				float4 temp_cast_1 = (_EmissionSoftness2).xxxx;
				float4 smoothstepResult193 = smoothstep(temp_cast_0, temp_cast_1, SAMPLE_TEXTURE2D(_EmissionTex, sampler_EmissionTex, panner238));

				float4 temp_cast_4 = (staticSwitch246).xxxx;
				float4 clampResult197 = clamp((smoothstepResult193 - temp_cast_4), float4(0, 0, 0, 0), float4(1, 1, 1, 0));
			#ifdef _EMISSIONDISSOLVE_ON
				float4 staticSwitch177 = (_Emission * clampResult197);
			#else
				float4 staticSwitch177 = (_Emission * IN.uv3.w);
			#endif

				float smoothstepResult258 = smoothstep(_FinalEmissionSmoothstepMin, _FinalEmissionSmoothstepMax, staticSwitch159);
			#ifdef _FINALEMISSIONSMOOTHSTEP_ON
				float staticSwitch276 = smoothstepResult258;
			#else
				float staticSwitch276 = staticSwitch159;
			#endif

			#ifdef _EMISSIONALPHA_ON
				float4 staticSwitch240 = (staticSwitch276 * staticSwitch177);
			#else
				float4 staticSwitch240 = staticSwitch177;
			#endif

				float smoothstepResult252 = smoothstep(_FinalAlphaSmoothstepMin, _FinalAlphaSmoothstepMax, staticSwitch159);
			#ifdef _FINALALPHASMOOTHSTEP_ON
				float staticSwitch275 = smoothstepResult252;
			#else
				float staticSwitch275 = finalAlpha248;
			#endif

				half3 albedo = (_Color.rgb * IN.vertexColor.rgb);
				half3 emission = staticSwitch240.rgb;
				half alpha = staticSwitch275;

				InputData inputData = (InputData)0;
				inputData.positionWS = IN.positionWS;
				inputData.normalWS = normalWS;
				inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
				inputData.shadowCoord = float4(0, 0, 0, 0);
				inputData.fogCoord = 0;
				inputData.vertexLighting = half3(0, 0, 0);
				inputData.bakedGI = SampleSH(normalWS);
				inputData.normalizedScreenSpaceUV = ase_screenPosNorm.xy;
				inputData.shadowMask = half4(1, 1, 1, 1);

				SurfaceData surfaceData = (SurfaceData)0;
				surfaceData.albedo = albedo;
				surfaceData.specular = half3(0, 0, 0);
				surfaceData.metallic = 0;
				surfaceData.smoothness = 0;
				surfaceData.normalTS = normalTS;
				surfaceData.emission = emission;
				surfaceData.occlusion = 1;
				surfaceData.alpha = alpha;
				surfaceData.clearCoatMask = 0;
				surfaceData.clearCoatSmoothness = 1;

				half4 color = UniversalFragmentPBR(inputData, surfaceData);
				color.a = alpha;
				return color;
			}
			ENDHLSL
		}
	}
	Fallback Off
}
