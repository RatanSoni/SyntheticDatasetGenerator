Shader "Hidden/SyntheticDatasetGenerator/ImageAugmentation"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    
    HLSLINCLUDE
    #include "UnityCG.cginc"
    
    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    
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
    
    v2f vert(appdata v)
    {
        v2f o;
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.uv = v.uv;
        return o;
    }
    
    float hash11(float p)
    {
        p = frac(p * 0.1031);
        p *= p + 33.33;
        p *= p + p;
        return frac(p);
    }
    
    float hash21(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }
    
    // Box-Muller transform for Gaussian distribution
    float gaussianNoise(float2 uv, float seed, float seedOffset)
    {
        float2 randomUV = uv * 1000.0 + float2(seed, seedOffset);
        float u1 = hash21(randomUV);
        float u2 = hash21(randomUV + float2(127.1, 311.7));
        
        // Avoid log(0)
        u1 = max(u1, 0.0001);
        
        // Box-Muller transform
        float mag = sqrt(-2.0 * log(u1));
        float angle = 6.28318530718 * u2;
        return mag * cos(angle);
    }
    
    // RGB to HSV
    float3 rgb2hsv(float3 c)
    {
        float4 K = float4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
        float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
        float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
        float d = q.x - min(q.w, q.y);
        float e = 1.0e-10;
        return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
    }
    
    // HSV to RGB
    float3 hsv2rgb(float3 c)
    {
        float4 K = float4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
        float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
        return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
    }
    ENDHLSL
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        
        // Pass 0: Horizontal Blur
        Pass
        {
            Name "BlurHorizontal"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            int _BlurRadius;
            
            float4 frag(v2f i) : SV_Target
            {
                float4 col = float4(0, 0, 0, 0);
                int samples = _BlurRadius * 2 + 1;
                float weight = 1.0 / samples;
                
                for (int x = -_BlurRadius; x <= _BlurRadius; x++)
                {
                    float2 offset = float2(x * _MainTex_TexelSize.x, 0);
                    col += tex2D(_MainTex, saturate(i.uv + offset)) * weight;
                }
                return col;
            }
            ENDHLSL
        }
        
        // Pass 1: Vertical Blur
        Pass
        {
            Name "BlurVertical"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            int _BlurRadius;
            
            float4 frag(v2f i) : SV_Target
            {
                float4 col = float4(0, 0, 0, 0);
                int samples = _BlurRadius * 2 + 1;
                float weight = 1.0 / samples;
                
                for (int y = -_BlurRadius; y <= _BlurRadius; y++)
                {
                    float2 offset = float2(0, y * _MainTex_TexelSize.y);
                    col += tex2D(_MainTex, saturate(i.uv + offset)) * weight;
                }
                return col;
            }
            ENDHLSL
        }
        
        // Pass 2: Combined (Noise + Chromatic Aberration + Color Grading)
        Pass
        {
            Name "Combined"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            float _NoiseSigma;
            float _Seed;
            float _SeedOffset;
            int _ApplyNoise;
            
            float _AberrationOffset;
            int _ApplyAberration;
            
            float _HueShift;
            float _Saturation;
            float _Contrast;
            float _Exposure;
            int _ApplyColorGrading;
            
            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float4 col;
                
                // Chromatic Aberration
                if (_ApplyAberration > 0)
                {
                    float2 center = float2(0.5, 0.5);
                    float2 dir = uv - center;
                    float dist = length(dir);
                    float2 normDir = dist > 0.0001 ? dir / dist : float2(0, 0);
                    float offset = _AberrationOffset * _MainTex_TexelSize.x * dist * dist * 4.0;
                    
                    float2 uvR = saturate(uv + normDir * offset);
                    float2 uvB = saturate(uv - normDir * offset);
                    
                    float r = tex2D(_MainTex, uvR).r;
                    float g = tex2D(_MainTex, uv).g;
                    float b = tex2D(_MainTex, uvB).b;
                    col = float4(r, g, b, 1.0);
                }
                else
                {
                    col = tex2D(_MainTex, uv);
                }
                
                // Color Grading
                if (_ApplyColorGrading > 0)
                {
                    // Exposure
                    col.rgb *= pow(2.0, _Exposure);
                    
                    // HSV adjustments
                    float3 hsv = rgb2hsv(saturate(col.rgb));
                    hsv.x = frac(hsv.x + _HueShift + 1.0);
                    hsv.y = saturate(hsv.y + _Saturation);
                    col.rgb = hsv2rgb(hsv);
                    
                    // Contrast
                    col.rgb = saturate((col.rgb - 0.5) * (1.0 + _Contrast) + 0.5);
                }
                
                // Gaussian Noise (applied last)
                if (_ApplyNoise > 0)
                {
                    float noise = gaussianNoise(uv, _Seed, _SeedOffset) * _NoiseSigma;
                    col.rgb = saturate(col.rgb + noise);
                }
                
                col.a = 1.0;
                return col;
            }
            ENDHLSL
        }
    }
    
    Fallback Off
}
