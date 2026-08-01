Shader "Knife/Particle Channel Packed Unlit_URP"
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
		_DepthSoftness("DepthSoftness", Float) = 1
		[Toggle(_ALPHADISSOLVE_ON)] _AlphaDissolve("AlphaDissolve", Float) = 0
		[HDR]_Emission("Emission", Color) = (0,0,0,0)
		[Toggle(_EMISSIONDISSOLVE_ON)] _EmissionDissolve("EmissionDissolve", Float) = 0
		_EmissionTex("EmissionTex", 2D) = "white" {}
		_EmissionSpeed("EmissionSpeed", Vector) = (0,0,0,0)
		_EmissionSoftness1("EmissionSoftness1", Range( 0 , 1)) = 0
		_EmissionSoftness2("EmissionSoftness2", Range( 0 , 1)) = 0
		[Toggle(_FINALALPHASMOOTHSTEP_ON)] _FinalAlphaSmoothstep("FinalAlphaSmoothstep", Float) = 0
		_FinalAlphaSmoothstepMin("FinalAlphaSmoothstepMin", Range( 0 , 1)) = 0
		_FinalAlphaSmoothstepMax("FinalAlphaSmoothstepMax", Range( 0 , 1)) = 1
		[Toggle(_EMISSIONALPHA_ON)] _EmissionAlpha("EmissionAlpha", Float) = 0
		[Toggle(_FINALEMISSIONSMOOTHSTEP_ON)] _FinalEmissionSmoothstep("FinalEmissionSmoothstep", Float) = 0
		_FinalEmissionSmoothstepMin("FinalEmissionSmoothstepMin", Range( 0 , 1)) = 0
		_FinalEmissionSmoothstepMax("FinalEmissionSmoothstepMax", Range( 0 , 1)) = 1
		_EmissionSubValue("EmissionSubValue", Range( 0 , 1)) = 0
		[Toggle(_ALPHAEMISSIONDISSOLVESUB_ON)] _AlphaEmissionDissolveSub("Alpha Emission Dissolve Sub", Float) = 0
	}

	SubShader
	{
		Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
		LOD 100

		Blend SrcAlpha OneMinusSrcAlpha
		Cull Back
		ColorMask RGBA
		ZWrite Off
		ZTest LEqual

		Pass
		{
			Name "Unlit"
			Tags { "LightMode"="UniversalForward" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma shader_feature _EMISSIONALPHA_ON
			#pragma shader_feature _EMISSIONDISSOLVE_ON
			#pragma shader_feature _ALPHAEMISSIONDISSOLVESUB_ON
			#pragma shader_feature _ALPHADISSOLVE_ON
			#pragma shader_feature _MAINTEXSMOOTHSTEP_ON
			#pragma shader_feature _FINALEMISSIONSMOOTHSTEP_ON
			#pragma shader_feature _FINALALPHASMOOTHSTEP_ON

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
			TEXTURE2D(_EmissionTex);  SAMPLER(sampler_EmissionTex);

			CBUFFER_START(UnityPerMaterial)
				float4 _Color;
				float4 _Emission;
				float _EmissionSoftness1;
				float _EmissionSoftness2;
				float2 _EmissionSpeed;
				float4 _EmissionTex_ST;
				float _DepthSoftness;
				float _Columns;
				float _Rows;
				float _MainSoftnessMin;
				float _MainSoftnessMax;
				float _EmissionSubValue;
				float _FinalEmissionSmoothstepMin;
				float _FinalEmissionSmoothstepMax;
				float _FinalAlphaSmoothstepMin;
				float _FinalAlphaSmoothstepMax;
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
				float4 ase_color : COLOR;
				float4 ase_texcoord1 : TEXCOORD1;
				float4 ase_texcoord2 : TEXCOORD2;
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
				o.ase_color = v.color;
				o.ase_texcoord1 = v.ase_texcoord;
				return o;
			}

			half4 frag(v2f i) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(i);

				float4 uv0170 = i.ase_texcoord1;
				uv0170.xy = i.ase_texcoord1.xy * float2(1, 1) + float2(0, 0);

				float4 temp_cast_1 = (_EmissionSoftness1).xxxx;
				float4 temp_cast_2 = (_EmissionSoftness2).xxxx;
				float2 uv0_EmissionTex = i.ase_texcoord1.xy * _EmissionTex_ST.xy + _EmissionTex_ST.zw;
				float2 panner238 = _Time.y * _EmissionSpeed + uv0_EmissionTex;
				float4 smoothstepResult193 = smoothstep(temp_cast_1, temp_cast_2, SAMPLE_TEXTURE2D(_EmissionTex, sampler_EmissionTex, panner238));

				float4 screenPos = i.ase_texcoord2;
				float4 ase_screenPosNorm = screenPos / screenPos.w;
				ase_screenPosNorm.z = (UNITY_NEAR_CLIP_VALUE >= 0) ? ase_screenPosNorm.z : ase_screenPosNorm.z * 0.5 + 0.5;

				float rawDepth142 = SampleSceneDepth(ase_screenPosNorm.xy);
				float screenDepth142 = LinearEyeDepth(rawDepth142, _ZBufferParams);
				float distanceDepth142 = abs((screenDepth142 - LinearEyeDepth(ase_screenPosNorm.z, _ZBufferParams)) / (_DepthSoftness));
				float clampResult146 = clamp(distanceDepth142, 0.0, 1.0);
				float depthFadeAlpha163 = clampResult146;

				float4 uv03 = i.ase_texcoord1;
				uv03.xy = i.ase_texcoord1.xy * float2(1, 1) + float2(0, 0);
				float columns135 = _Columns;
				float rows136 = _Rows;
				float AnimFrame4 = round(uv03.z);
				float temp_output_18_0 = (columns135 * rows136);
				float ChannelFramesCount103 = temp_output_18_0;

				// Flipbook UV animation
				float fbtotaltiles98 = columns135 * rows136;
				float fbcolsoffset98 = 1.0f / columns135;
				float fbrowsoffset98 = 1.0f / rows136;
				float fbspeed98 = _Time[1] * 0.0;
				float2 fbtiling98 = float2(fbcolsoffset98, fbrowsoffset98);
				float fbcurrenttileindex98 = round(fmod(fbspeed98 + (frac((AnimFrame4 / ChannelFramesCount103)) * ChannelFramesCount103), fbtotaltiles98));
				fbcurrenttileindex98 += (fbcurrenttileindex98 < 0) ? fbtotaltiles98 : 0;
				float fblinearindextox98 = round(fmod(fbcurrenttileindex98, columns135));
				float fboffsetx98 = fblinearindextox98 * fbcolsoffset98;
				float fblinearindextoy98 = round(fmod((fbcurrenttileindex98 - fblinearindextox98) / columns135, rows136));
				fblinearindextoy98 = (int)(rows136 - 1) - fblinearindextoy98;
				float fboffsety98 = fblinearindextoy98 * fbrowsoffset98;
				float2 fboffset98 = float2(fboffsetx98, fboffsety98);
				half2 fbuv98 = (uv03).xy * fbtiling98 + fboffset98;

				float4 tex2DNode1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, fbuv98);
				float4 temp_cast_3 = (_MainSoftnessMin).xxxx;
				float4 temp_cast_4 = (_MainSoftnessMax).xxxx;
				float4 smoothstepResult233 = smoothstep(temp_cast_3, temp_cast_4, tex2DNode1);
			#ifdef _MAINTEXSMOOTHSTEP_ON
				float4 staticSwitch236 = smoothstepResult233;
			#else
				float4 staticSwitch236 = tex2DNode1;
			#endif
				float4 break152 = staticSwitch236;
				float Frames126 = temp_output_18_0;
				float temp_output_133_0 = (Frames126 - 1.0);
				float smoothstepResult23 = smoothstep(temp_output_133_0, temp_output_133_0, AnimFrame4);
				float lerp156 = smoothstepResult23;
				float lerpResult20 = lerp(break152.r, break152.g, lerp156);
				float Frames243 = (Frames126 * 2.0);
				float temp_output_123_0 = (Frames243 - 1.0);
				float smoothstepResult24 = smoothstep(temp_output_123_0, temp_output_123_0, AnimFrame4);
				float lerp257 = smoothstepResult24;
				float lerpResult21 = lerp(lerpResult20, break152.b, lerp257);
				float Frames344 = (Frames126 * 3.0);
				float temp_output_124_0 = (Frames344 - 1.0);
				float smoothstepResult25 = smoothstep(temp_output_124_0, temp_output_124_0, AnimFrame4);
				float lerp358 = smoothstepResult25;
				float lerpResult22 = lerp(lerpResult21, break152.a, lerp358);

				float clampResult166 = clamp(((depthFadeAlpha163 * lerpResult22) - (1.0 - i.ase_color.a)), 0.0, 1.0);
			#ifdef _ALPHADISSOLVE_ON
				float staticSwitch159 = (_Color.a * clampResult166);
			#else
				float staticSwitch159 = ((_Color.a * i.ase_color.a) * depthFadeAlpha163 * lerpResult22);
			#endif
				float finalAlpha248 = staticSwitch159;

			#ifdef _ALPHAEMISSIONDISSOLVESUB_ON
				float staticSwitch246 = (uv0170.w - (finalAlpha248 * _EmissionSubValue));
			#else
				float staticSwitch246 = uv0170.w;
			#endif
				float4 temp_cast_5 = (staticSwitch246).xxxx;
				float4 clampResult197 = clamp((smoothstepResult193 - temp_cast_5), float4(0, 0, 0, 0), float4(1, 1, 1, 0));
			#ifdef _EMISSIONDISSOLVE_ON
				float4 staticSwitch177 = (_Emission * clampResult197);
			#else
				float4 staticSwitch177 = (_Emission * uv0170.w);
			#endif

				float smoothstepResult258 = smoothstep(_FinalEmissionSmoothstepMin, _FinalEmissionSmoothstepMax, staticSwitch159);
			#ifdef _FINALEMISSIONSMOOTHSTEP_ON
				float staticSwitch278 = smoothstepResult258;
			#else
				float staticSwitch278 = staticSwitch159;
			#endif

			#ifdef _EMISSIONALPHA_ON
				float4 staticSwitch240 = (staticSwitch278 * staticSwitch177);
			#else
				float4 staticSwitch240 = staticSwitch177;
			#endif

				float smoothstepResult252 = smoothstep(_FinalAlphaSmoothstepMin, _FinalAlphaSmoothstepMax, staticSwitch159);
			#ifdef _FINALALPHASMOOTHSTEP_ON
				float staticSwitch277 = smoothstepResult252;
			#else
				float staticSwitch277 = finalAlpha248;
			#endif

				float4 appendResult273 = float4((staticSwitch240).rgb, staticSwitch277);

				half4 finalColor = float4(((_Color).rgb * (i.ase_color).rgb), 0.0) + appendResult273;
				return finalColor;
			}
			ENDHLSL
		}
	}
	Fallback Off
}
