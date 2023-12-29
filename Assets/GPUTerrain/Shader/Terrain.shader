Shader "GPUTerrain/Terrain"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "LightMode" = "UniversalForward"}
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature ENABLE_MIP_DEBUG
            #pragma shader_feature ENABLE_PATCH_DEBUG
            #pragma shader_feature ENABLE_LOD_SEAMLESS
            #pragma shader_feature ENABLE_NODE_DEBUG

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "./CommonInput.hlsl"

 

            StructuredBuffer<RenderPatch> PatchList;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };


            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                half3 color: TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _HeightMap;
            sampler2D _NormalMap;
            uniform float3 _WorldSize;
            float4x4 _WorldToNormalMapMatrix;

            static half3 debugColorForMip[6]={
                half3(0, 1, 0),
                half3(0, 0, 1),
                half3(1, 0, 0),
                half3(1, 1, 0),
                half3(0, 1, 1),
                half3(1, 0, 1),              
            };

            float3 TransformNormalToWorldSpace(float3 normal){
                return SafeNormalize(mul(normal,(float3x3)_WorldToNormalMapMatrix));
            }

            float3 SampleNormal(float2 uv){
                float3 normal;
                normal.xz = tex2Dlod(_NormalMap, float4(uv, 0, 0)).xy * 2 - 1;
                normal.y = sqrt(max(0, 1 - dot(normal.xz, normal.xz)));
                normal = TransformNormalToWorldSpace(normal);
                return normal;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.uv = v.uv;
                float4 inVertex = v.vertex;
                RenderPatch patch = PatchList[v.instanceID];
                uint lod = patch.lod;
                float scale = pow(2,lod);
                inVertex.xz *= scale;
                inVertex.xz += patch.position;

                float2 heightUV = (inVertex.xz + (_WorldSize.xz * 0.5) + 0.5) / (_WorldSize.xz + 1);
                float height = tex2Dlod(_HeightMap,float4(heightUV,0,0)).r;
                inVertex.y = height * _WorldSize.y;


                float3 normal = SampleNormal(heightUV);
                Light light =GetMainLight();
                o.color = max(0.05,dot(light.direction, normal));


                float4 vertex = TransformObjectToHClip(inVertex.xyz);
                o.vertex = vertex;

                o.uv = o.uv * scale * 8;

                #if ENABLE_MIP_DEBUG
                
                uint lodColorIndex = lod;
                o.color *= debugColorForMip[lodColorIndex]; 

                #endif
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // sample the texture
                half4 col = tex2D(_MainTex, i.uv);
                
                col.rgb *= i.color;


                return col;
            }
            ENDHLSL
        }
    }
}
