// Separable 9-tap Gaussian blur used for the frosted-glass UI. _BlurDirection and _Frost are set as globals
// by BlurCapture's command buffer.
Shader "Hidden/Monopoly/Blur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    float2 _BlurDirection;
    float _Frost;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    fixed4 frag(v2f i) : SV_Target
    {
        float2 d = _BlurDirection * _MainTex_TexelSize.xy;
        fixed3 c = tex2D(_MainTex, i.uv).rgb * 0.227027;
        c += (tex2D(_MainTex, i.uv + d * 1.3846).rgb + tex2D(_MainTex, i.uv - d * 1.3846).rgb) * 0.3162162;
        c += (tex2D(_MainTex, i.uv + d * 3.2308).rgb + tex2D(_MainTex, i.uv - d * 3.2308).rgb) * 0.0702703;
        // Frost: lift towards white so dark text stays readable on top.
        c = lerp(c, fixed3(1, 1, 1), _Frost);
        return fixed4(c, 1);
    }
    ENDCG

    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
}
