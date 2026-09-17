Shader "PaperTrails/Turf" {
 SubShader {
  Tags { "RenderType"="Opaque" }
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct appdata { float4 vertex:POSITION; float4 color:COLOR; float3 normal:NORMAL; };
   struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; };
   v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex);o.color=v.color*(.8+.2*saturate(dot(v.normal,normalize(float3(-.3,1,-.4)))));return o; }
   fixed4 frag(v2f i):SV_Target { return i.color; }
   ENDCG
  }
 }
}
