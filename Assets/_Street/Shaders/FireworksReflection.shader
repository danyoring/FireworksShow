// 강물에 비친 불꽃(가짜 반사)을 그리는 셰이더
// 더하기 합성(Blend One One)이라 검은 부분은 투명하고, 불꽃 빛만 물 위에 얹혀요.
Shader "Hidden/FireworksReflection"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Intensity ("Intensity", Float) = 0.55
        _Horizon ("Horizon", Float) = 0.2
        _Range ("Range", Float) = 0.6
        _Tint ("Tint", Color) = (0.85, 0.9, 1, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Intensity, _Horizon, _Range;
            fixed4 _Tint;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // d: 수평선에서 0, 화면 아래쪽으로 갈수록 1
                float d = saturate((i.uv.y - _Horizon) / max(_Range, 1e-4));
                float2 uv = i.uv;
                // 물결 일렁임 (아래로 갈수록 조금 더 흔들림)
                uv.x += sin(uv.y * 220.0 + _Time.y * 3.0) * 0.004 * (0.3 + d);
                // 세로로 살짝 번지게
                float3 c = tex2D(_MainTex, uv).rgb * 0.4
                         + tex2D(_MainTex, uv + float2(0, 0.004)).rgb * 0.3
                         + tex2D(_MainTex, uv - float2(0, 0.004)).rgb * 0.3;
                float fade = 1.0 - d;
                fade *= fade;
                return fixed4(c * _Tint.rgb * _Intensity * fade * i.color.a, 1);
            }
            ENDCG
        }
    }
}
