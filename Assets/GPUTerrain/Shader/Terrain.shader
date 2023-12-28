Shader "GPUTerrain/Terrain"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
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
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _HeightMap;
            uniform float3 _WorldSize;

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

                o.vertex = TransformObjectToHClip(inVertex.xyz);
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // sample the texture
                half4 col = tex2D(_MainTex, i.uv);

                return col;
            }
            ENDHLSL
        }
    }
}
