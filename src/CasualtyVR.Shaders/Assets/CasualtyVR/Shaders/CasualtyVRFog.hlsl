#ifndef CASUALTYVR_FOG_INCLUDED
#define CASUALTYVR_FOG_INCLUDED

// CasualtyVR first-person distance fog. Globals set by the mod while a headset eye renders in first person
// (zero otherwise, so the sprite looks exactly as with URP's own shader):
//   _CvrFogColor  rgb = fog colour
//   _CvrFogParams x = start distance, y = full distance (world units), z = maximum amount (0-1), w = on (0/1)
float4 _CvrFogColor;
float4 _CvrFogParams;

float CvrFogAmount(float3 positionWS)
{
    float3 toCam = positionWS - GetCameraPositionWS();
    float d = sqrt(dot(toCam, toCam));
    return saturate((d - _CvrFogParams.x) / max(0.001, _CvrFogParams.y - _CvrFogParams.x)) * _CvrFogParams.z * _CvrFogParams.w;
}

float4 CvrFog(float4 c, float3 positionWS)
{
    float f = CvrFogAmount(positionWS);
    return float4(lerp(c.rgb, _CvrFogColor.rgb, f), c.a);
}

#endif
