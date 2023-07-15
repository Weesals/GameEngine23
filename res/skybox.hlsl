// Unity skybox shader. Adapted from https://www.shadertoy.com/view/tltGDM

/*
licence.txt from Unity built-in shader source:

Copyright (c) 2016 Unity Technologies

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

/*
Original code was translated and adapted for ShaderToy by P.Z.
*/

static const float _Exposure = 1.0;
static const float3 _GroundColor = float3(.40, .39, .38);
static const float _SunSize = 0.04;
static const float _SunSizeConvergence = 5.0;
static const float _AtmosphereThickness = 1.0;
static const float3 _SkyTint = float3(0.5, 0.5, 0.5);
static const float3 ScatteringWavelength = float3(.65, .57, .475);
static const float3 ScatteringWavelengthRange = float3(.15, .15, .15);

#define OUTER_RADIUS 1.025
static const float kOuterRadius = OUTER_RADIUS;
static const float kOuterRadius2 = OUTER_RADIUS * OUTER_RADIUS;
static const float kInnerRadius = 1.0;
static const float kInnerRadius2 = 1.0;
static const float kCameraHeight = 0.0001;
#define kRAYLEIGH (lerp(0.0, 0.0025, pow(_AtmosphereThickness,2.5))) 
#define kMIE 0.0010 
#define kSUN_BRIGHTNESS 20.0 
#define kMAX_SCATTER 50.0 
static const float kHDSundiskIntensityFactor = 15.0;
static const float kSunScale = 400.0 * kSUN_BRIGHTNESS;
static const float kKmESun = kMIE * kSUN_BRIGHTNESS;
static const float kKm4PI = kMIE * 4.0 * 3.14159265;
static const float kScale = 1.0 / (OUTER_RADIUS - 1.0);
static const float kScaleDepth = 0.25;
static const float kScaleOverScaleDepth = (1.0 / (OUTER_RADIUS - 1.0)) / 0.25;
static const float kSamples = 2.0;

#define MIE_G (-0.990) 
#define MIE_G2 0.9801 
#define SKY_GROUND_THRESHOLD 0.02 

cbuffer ConstantBuffer : register(b0)
{
    matrix ModelViewProjection;
    matrix InvModelViewProjection;
    float2 Resolution;
    //float Time;
    //float DayTime;
    float3 _WorldSpaceLightDir0;
    float3 _LightColor0;
};

struct VSInput
{
    float4 position : POSITION;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float time : TEXCOORD0;
};

PSInput VSMain(VSInput input)
{
    PSInput result;

    result.position = input.position;
    result.position.z = 0.9999;
    result.time = 0.0;

#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}


float Scale(float inCos)
{
    float x = 1.0 - inCos;
    return 0.25 * exp(-0.00287 + x * (0.459 + x * (3.83 + x * (-6.80 + x * 5.25))));
}

float SunAttenuation(float3 lightPos, float3 ray)
{
    float EyeCos = pow(clamp(dot(lightPos, ray), 0.0, 1.0), _SunSizeConvergence);
    float temp = pow(1.0 + MIE_G2 - 2.0 * MIE_G * (-EyeCos), pow(_SunSize, 0.65) * 10.);
    return (1.5 * ((1.0 - MIE_G2) / (2.0 + MIE_G2)) * (1.0 + EyeCos * EyeCos) / max(temp, 1.0e-4));
}

float4 ProceduralSkybox(float3 ro, float3 rd)
{
    float3 kSkyTintInGammaSpace = _SkyTint;
    float3 kScatteringWavelength = lerp(ScatteringWavelength - ScatteringWavelengthRange, ScatteringWavelength + ScatteringWavelengthRange, float3(1, 1, 1) - kSkyTintInGammaSpace);
    float3 kInvWavelength = 1.0 / (pow(kScatteringWavelength, 4.0));
    float kKrESun = kRAYLEIGH * kSUN_BRIGHTNESS;
    float kKr4PI = kRAYLEIGH * 4.0 * 3.14159265;
    float3 cameraPos = float3(0, kInnerRadius + kCameraHeight, 0);
    float3 eyeRay = rd;
    float far = 0.0;
    float3 cIn, cOut;
    if (eyeRay.y >= 0.0)
    {
        far = sqrt(kOuterRadius2 + kInnerRadius2 * eyeRay.y * eyeRay.y - kInnerRadius2) - kInnerRadius * eyeRay.y;
        float3 pos = cameraPos + far * eyeRay;
        float height = kInnerRadius + kCameraHeight;
        float depth = exp(kScaleOverScaleDepth * (-kCameraHeight));
        float startAngle = dot(eyeRay, cameraPos) / height;
        float startOffset = depth * Scale(startAngle);
        float sampleLength = far / kSamples;
        float scaledLength = sampleLength * kScale;
        float3 sampleRay = eyeRay * sampleLength;
        float3 samplePoint = cameraPos + sampleRay * 0.5;
        float3 frontColor = float3(0.0, 0.0, 0.0);
        for (int i = 0; i < 2; i++)
        {
            float height = length(samplePoint);
            float depth = exp(kScaleOverScaleDepth * (kInnerRadius - height));
            float lightAngle = dot(normalize(_WorldSpaceLightDir0.xyz), samplePoint) / height;
            float cameraAngle = dot(eyeRay, samplePoint) / height;
            float scatter = (startOffset + depth * (Scale(lightAngle) - Scale(cameraAngle)));
            float3 attenuate = exp(-clamp(scatter, 0.0, kMAX_SCATTER) * (kInvWavelength * kKr4PI + kKm4PI));
            frontColor += attenuate * (depth * scaledLength);
            samplePoint += sampleRay;
        }
        cIn = frontColor * (kInvWavelength * kKrESun);
        cOut = frontColor * kKmESun;
    }
    else
    {
        far = (-kCameraHeight) / (min(-0.001, eyeRay.y));
        float3 pos = cameraPos + far * eyeRay;
        float cameraScale = Scale(dot(-eyeRay, pos));
        float lightScale = Scale(dot(normalize(_WorldSpaceLightDir0.xyz), pos));
        float sampleLength = far / kSamples;
        float scaledLength = sampleLength * kScale;
        float3 sampleRay = eyeRay * sampleLength;
        float3 samplePoint = cameraPos + sampleRay * 0.5;
        float3 frontColor = float3(0.0, 0.0, 0.0);
        float height = length(samplePoint);
        float d = exp(kScaleOverScaleDepth * (kInnerRadius - height));
        float scatter = d * (lightScale + cameraScale) - exp((-kCameraHeight) * (1.0 / kScaleDepth)) * cameraScale;
        float3 attenuate = exp(-clamp(scatter, 0.0, kMAX_SCATTER) * (kInvWavelength * kKr4PI + kKm4PI));
        frontColor += attenuate * (d * scaledLength);
        samplePoint += sampleRay;
        cIn = frontColor * (kInvWavelength * kKrESun + kKmESun);
        cOut = clamp(attenuate, 0.0, 1.0);
    }
    float3 groundColor = _Exposure * (cIn + _GroundColor * _GroundColor * cOut);
    float3 skyColor = _Exposure * (cIn * (0.75 + 0.75 * dot(normalize(_WorldSpaceLightDir0.xyz), -eyeRay) * dot(normalize(_WorldSpaceLightDir0.xyz), -eyeRay)));
    float lightColorIntensity = clamp(length(_LightColor0.xyz), 0.25, 1.0);
    float3 sunColor = kHDSundiskIntensityFactor * clamp(cOut, 0.0, 1.0) * _LightColor0.xyz / lightColorIntensity;
    float3 ray = -rd;
    float y = ray.y / SKY_GROUND_THRESHOLD;
    float3 color = lerp(skyColor, groundColor, clamp(y, 0.0, 1.0));
    if (y < 0.0)
        color += sunColor * SunAttenuation(normalize(_WorldSpaceLightDir0.xyz), -ray);
    return float4(sqrt(color), 1.0);
}

//////////////////////////////////////////////////////////////////////////////////////////////

float4 PSMain(PSInput input) : SV_TARGET
{
    float2 vpos = input.position.xy / Resolution.xy * 2 - 1;
    vpos.y = -vpos.y;
        
    float4 clipSpacePosition = float4(vpos, 1.0f, 1.0f);
    float4 viewSpacePosition = mul(clipSpacePosition, InvModelViewProjection);
    viewSpacePosition.xyz /= viewSpacePosition.w;
    float3 directionVector = normalize(viewSpacePosition.xyz);
    //return float4(frac(directionVector * 5), 0.0f);
    
    float3 ro = float3(0., 0., 0.);
    float3 rd = directionVector.xyz;
    return ProceduralSkybox(ro, rd);
}
