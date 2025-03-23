Shader "Custom/ProceduralShader"
{
    SubShader
    {
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            
            struct appdata
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct v2f {
                float4 positionCS : SV_POSITION;
                float4 positionWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            StructuredBuffer<float4x4> TransformMatrices;
            ByteAddressBuffer Vertices;
            int stride;
            int positionOffset;
            int uvOffset;

            float3 LoadPosition(uint index) {
                return asfloat(Vertices.Load3(index * stride + positionOffset));
            }
            float2 LoadUV(uint index) {
                return asfloat(Vertices.Load2(index * stride + uvOffset));
            }

            v2f vert(appdata v)
            {
                v2f o;
                
                float4 positionOS = float4(LoadPosition(v.vertexID), 1.0);
                float4x4 objectToWorld = TransformMatrices[v.instanceID];

                o.positionWS = mul(objectToWorld, positionOS);
                o.positionCS = mul(UNITY_MATRIX_VP, o.positionWS);
                o.uv = LoadUV(v.vertexID);

                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                //return tex2D(_MainTex, i.uv);
                return float4(1,0,0,1);
            }


            ENDCG
        }
    }
}
