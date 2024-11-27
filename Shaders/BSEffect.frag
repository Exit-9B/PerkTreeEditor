// SPDX-License-Identifier: GPL-3.0-or-later
#version 100
precision mediump float;

varying vec4 vTexCoord0;
varying vec4 vColor;
varying vec4 vWorldPosition;

uniform vec4 BaseColor;
uniform vec4 BaseColorScale;
uniform vec4 LightingInfluence;
uniform vec4 PropertyColor;

uniform sampler2D BaseSampler;
uniform sampler2D GrayscaleSampler;

uniform bool VC;
uniform bool UseTexture;
uniform bool GrayscaleToAlpha;
uniform bool GrayscaleToColor;

void main()
{
    float lightingInfluence = LightingInfluence.x;
    vec3 propertyColor = PropertyColor.xyz;

    vec4 baseTexColor = vec4(1, 1, 1, 1);
    vec4 baseColor = vec4(1, 1, 1, 1);
    if (UseTexture)
    {
        baseTexColor = texture2D(BaseSampler, vTexCoord0.xy);
        baseColor *= baseTexColor;
        if (GrayscaleToAlpha)
        {
            baseColor.w = 1.0;
        }
    }

    vec4 baseColorMul = BaseColor;
    if (VC)
    {
        baseColorMul *= vColor;
    }

    baseColor.w *= vTexCoord0.z;

    baseColor = baseColorMul * baseColor;

    float alpha = baseColor.w;
    float baseColorScale = BaseColorScale.x;
    if (GrayscaleToAlpha)
    {
        alpha = texture2D(GrayscaleSampler, vec2(baseTexColor.w, alpha)).w;
    }

    if (GrayscaleToColor)
    {
        vec2 grayscaleToColorUv = vec2(baseTexColor.y, baseColorMul.x);
        baseColor.xyz = baseColorScale * texture2D(GrayscaleSampler, grayscaleToColorUv).xyz;
    }

    vec3 lightColor = mix(baseColor.xyz, propertyColor * baseColor.xyz, vec3(lightingInfluence));

    vec3 blendedColor = lightColor.xyz;

    vec4 finalColor = vec4(blendedColor, alpha);
    gl_FragColor = finalColor;
}