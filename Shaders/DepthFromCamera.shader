Shader "Hidden/SyntheticDatasetGenerator/DepthFromCamera"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _CameraDepthTexture;
            float _MaxDistance;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Sample the depth texture
                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv);
                
                // Convert to linear depth (distance from camera along view direction)
                float linearDepth = LinearEyeDepth(depth);
                
                // Normalize to 0-1 range based on max distance
                // 0 = near, 1 = far
                float normalizedDepth = saturate(linearDepth / _MaxDistance);
                
                // Invert so NEAR = bright (1.0), FAR = dark (0.0)
                normalizedDepth = 1.0 - normalizedDepth;
                
                // Output as grayscale - no gamma or contrast adjustments
                return fixed4(normalizedDepth, normalizedDepth, normalizedDepth, 1.0);
            }
            ENDCG
        }
    }
}
