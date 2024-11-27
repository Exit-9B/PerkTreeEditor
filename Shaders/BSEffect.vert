// SPDX-License-Identifier: GPL-3.0-or-later
#version 100
precision mediump float;

attribute vec3 aPosition;
attribute vec2 aTexCoord0;
attribute vec3 aNormal;
attribute vec4 aBitangent;
attribute vec4 aColor;

varying vec4 vTexCoord0;
varying vec4 vColor;
varying vec4 vWorldPosition;

uniform mat4 CameraViewProj;
uniform vec4 TexcoordOffset;
uniform mat4 World;

void main()
{
    vec4 inputPosition = vec4(aPosition.xyz, 1.0);
    mat4 world4x4 = World;
    mat3 world3x3 = mat3(World);
    mat4 viewProj = CameraViewProj;

    vec4 worldPosition = vec4((World * inputPosition).xyz, 1);
    mat4 modelView = viewProj * world4x4;
    vec4 viewPos = modelView * inputPosition;

    gl_Position = viewPos;

    vColor = aColor;

    vec4 texCoord = vec4(0, 0, 1, 0);
    vec4 texCoordOffset = TexcoordOffset;

    float u = aTexCoord0.x;
    texCoord.x = u * texCoordOffset.z + texCoordOffset.x;
    float v = aTexCoord0.y;
    texCoord.y = v * texCoordOffset.w + texCoordOffset.y;

    vTexCoord0 = texCoord;

    vWorldPosition = worldPosition;
}
