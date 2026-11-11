uint _HeadLightEnabled;
float4 _HeadLightCenter, _HeadLightRadii, _HeadLightDirection, _HeadLightColour;

float3 HeadSRGBToLinear(float3 c)
{
    return lerp(c/12.92,pow((c+0.055)/1.055,2.4),step(0.04045,c));
}
float3 HeadLinearToSRGB(float3 c)
{
    return lerp(c*12.92,1.055*pow(c,1.0/2.4)-0.055,step(0.0031308,c));
}
float3 ApplyHeadLighting(float3 captured, float3 localPosition)
{
    if (!_HeadLightEnabled) return captured;
    // A smooth ellipsoid is deliberately used for broad shading. Individual
    // Gaussian axes are not reliable skin normals and produce speckled lighting.
    float3 normalOS=(localPosition-_HeadLightCenter.xyz)/max(_HeadLightRadii.xyz*_HeadLightRadii.xyz,1e-6);
    float3 normalWS=mul(normalOS,(float3x3)_MatrixWorldToObject);
    normalWS*=rsqrt(max(dot(normalWS,normalWS),1e-12));
    float wrapped=saturate(0.5+0.5*dot(normalWS,_HeadLightDirection.xyz));
    float diffuse=lerp(1.0,wrapped,_HeadLightDirection.w);
    // Captured colours are sRGB in this renderer; exposure and tint act in linear light.
    float3 linearColour=HeadSRGBToLinear(max(captured,0));
    return HeadLinearToSRGB(max(linearColour*_HeadLightColour.rgb*(_HeadLightColour.w*diffuse),0));
}
