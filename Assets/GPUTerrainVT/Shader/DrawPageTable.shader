Shader "VirtualTexture/DrawPageTable"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalRenderPipeline"}

		Cull Front
		ZTest Always

		Pass
		{
			Tags { "LightMode" = "DrawPageTable" }

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			//#pragma enable_d3d11_debug_symbols

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 texCoord0           : TEXCOORD0;
				float4 vertexOS   : POSITION;
			};

			struct Varyings
			{
				float4 uv0           : TEXCOORD0;
				float4 vertexCS	   : SV_POSITION;
			};

			struct FPageTableInfo
			{
				float4 pageData;
				float4x4 matrix_M;
			};

			StructuredBuffer<FPageTableInfo> _PageTableBuffer;

			Varyings vert(Attributes input)
			{
				Varyings output;
				FPageTableInfo PageTableInfo = _PageTableBuffer[input.InstanceId];

				float2 pos = saturate(mul(PageTableInfo.matrix_M, input.vertexOS).xy);
				pos.y = 1 - pos.y;

				output.uv0 = PageTableInfo.pageData;
				output.vertexCS = float4(pos * 2 - 1, 0.5, 1);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				return input.uv0;
			}
			ENDHLSL
		}

		Pass
		{
			Tags { "LightMode" = "DrawTileTexture" }

			 Cull Off ZWrite Off ZTest Always

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
				//#pragma enable_d3d11_debug_symbols

				#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

				struct Attributes
				{
					float2 texCoord0           : TEXCOORD0;
					float4 vertexOS   : POSITION;
				};

				struct Varyings
				{
					float2 uv0           : TEXCOORD0;
					float4 vertexCS	   : SV_POSITION;
				};

				float4 _SplatTileOffset;
				float4x4 _ImageMVP;
				Varyings vert(Attributes In)
				{
					Varyings o;
					o.uv0 = In.texCoord0 * _SplatTileOffset.xy + _SplatTileOffset.zw;
					o.vertexCS = mul(_ImageMVP, In.vertexOS);
					return o;
				}

				Texture2DArray _SplatArray, _WorldNormalArray, _AlbedoArray, _NormalArray;




				SamplerState Global_trilinear_clamp_sampler;
				SamplerState Global_trilinear_repeat_sampler;

				float _SplatOffset[16];
				float _SplatCount[16];
				float  _TileData[16];

				void SplatmapMix(int passIndex, float splatOffset, float2 splatUV, float2 surfaceUV, out half weight, out half4 mixedDiffuse, out half4 mixedNormal)
				{
					half4 splatControl = _SplatArray.Sample(Global_trilinear_clamp_sampler, float3(splatUV, splatOffset));

					weight = dot(splatControl, 1.0h);
					splatControl /= (weight + HALF_MIN);

					mixedDiffuse = 0.0f;
					mixedNormal = 0.0f;

					float index = passIndex * 4;
					mixedDiffuse += splatControl.r * _AlbedoArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index], index));
					mixedDiffuse += splatControl.g * _AlbedoArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 1], index + 1));
					mixedDiffuse += splatControl.b * _AlbedoArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 2], index + 2));
					mixedDiffuse += splatControl.a * _AlbedoArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 3], index + 3));


					mixedNormal += splatControl.r * _NormalArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index], index));
					mixedNormal += splatControl.g * _NormalArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 1], index + 1));
					mixedNormal += splatControl.b * _NormalArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 2], index + 2));
					mixedNormal += splatControl.a * _NormalArray.Sample(Global_trilinear_repeat_sampler, float3(surfaceUV * _TileData[index + 3], index + 3));
					mixedNormal.xyz = UnpackNormal(mixedNormal);
				}

				int _TerrainIndex;
				float _TerrainSize;
				void  frag(Varyings In, out float4 ColorBuffer : SV_Target0, out float4 NormalBuffer : SV_Target1)
				{
					
					float splatOffset = _SplatOffset[_TerrainIndex];
					float splatCount = _SplatCount[_TerrainIndex];

					half weight;
					half4 mixedDiffuse = 0;
					half4 normalTS = 0;
					float2 splatUV = (In.uv0.xy * (_TerrainSize - 1.0f) + 0.5f) * rcp(_TerrainSize);
					float2 surfaceUV = In.uv0.xy;
	
					[unroll(2)]
					for (int i = 0; i < splatCount; i++)
					{
						SplatmapMix(0, splatOffset + i, splatUV, surfaceUV, weight, mixedDiffuse, normalTS);
					}
					ColorBuffer = mixedDiffuse;
					NormalBuffer = half4(normalTS.xyz * 0.5 + 0.5, 1);
				}
				ENDHLSL
			}

	}
}
