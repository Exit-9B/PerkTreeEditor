// SPDX-License-Identifier: GPL-3.0-or-later
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Enums;
using NiflySharp.Structs;
using Silk.NET.OpenGL;
using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PerkTreeEditor;

internal class OpenGlShape
{
    [StructLayout(LayoutKind.Explicit)]
    struct Vertex
    {
        [FieldOffset(0)] public Vector3 Position;
        [FieldOffset(12)] public Vector2 TexCoord;
        [FieldOffset(20)] public Vector3 Normal;
        [FieldOffset(32)] public Vector3 Bitangent;
        [FieldOffset(44)] public ByteColor4 Color;

        public static void SetupVertexArrayAttribs(GL gl)
        {
            uint stride = (uint)Marshal.SizeOf<Vertex>();
            gl.VertexAttribPointer((int)AttribLocation.Position, 3, GLEnum.Float, false, stride, 0);
            gl.VertexAttribPointer((int)AttribLocation.TexCoord, 2, GLEnum.Float, false, stride, 12);
            gl.VertexAttribPointer((int)AttribLocation.Normal, 3, GLEnum.Float, false, stride, 20);
            gl.VertexAttribPointer((int)AttribLocation.Bitangent, 3, GLEnum.Float, false, stride, 32);
            gl.VertexAttribPointer((int)AttribLocation.Color, 4, GLEnum.UnsignedByte, true, stride, 44);

            gl.EnableVertexAttribArray((int)AttribLocation.Position);
            gl.EnableVertexAttribArray((int)AttribLocation.TexCoord);
            gl.EnableVertexAttribArray((int)AttribLocation.Normal);
            gl.EnableVertexAttribArray((int)AttribLocation.Bitangent);
            gl.EnableVertexAttribArray((int)AttribLocation.Color);
        }
    }

    private readonly uint _vertexArray;
    private readonly uint _vertexBuffer;
    private readonly uint _elementBuffer;
    private readonly uint _elementCount;

    private Transform _worldTransform;

    private BSEffectShaderProperty? _shaderProperty;
    private NiAlphaProperty? _alphaProperty;
    private Texture? _sourceTexture;
    private Texture? _grayscaleTexture;

    public bool HasAlpha => _alphaProperty is not null;

    internal delegate Texture? TextureResolver(string path);

    public OpenGlShape(
        GL gl,
        BSTriShape shape,
        NifFile nif,
        TextureResolver resolveTexture)
    {
        _worldTransform = shape.WorldTransform(nif);

        _vertexArray = gl.GenVertexArray();
        _vertexBuffer = gl.GenBuffer();
        _elementBuffer = gl.GenBuffer();

        gl.BindVertexArray(_vertexArray);
        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);

        var vertexData = CreateVertexData(shape);
        gl.BufferData(GLEnum.ArrayBuffer, new ReadOnlySpan<Vertex>(vertexData), GLEnum.StaticDraw);

        Vertex.SetupVertexArrayAttribs(gl);

        gl.BindBuffer(GLEnum.ElementArrayBuffer, _elementBuffer);

        var elementData = CreateElementData(shape);
        gl.BufferData(GLEnum.ElementArrayBuffer, new ReadOnlySpan<ushort>(elementData), GLEnum.StaticDraw);

        _elementCount = (uint)elementData.Length;

        gl.BindVertexArray(0);

        _alphaProperty = nif.GetBlock(shape.AlphaPropertyRef);

        _shaderProperty = nif.GetBlock<BSEffectShaderProperty>(shape.ShaderPropertyRef);
        if (_shaderProperty is not null)
        {
            var sourceTexture = _shaderProperty.GetSourceTexture().Content;
            if (sourceTexture.Length > 0)
            {
                _sourceTexture = resolveTexture(sourceTexture);
                _sourceTexture?.Init(
                    gl,
                    TextureWrapMode.Repeat,
                    TextureWrapMode.Repeat);
            }

            var grayscaleTexture = _shaderProperty.GetGrayscaleTexture().Content;
            if (grayscaleTexture.Length > 0)
            {
                _grayscaleTexture = resolveTexture(grayscaleTexture);
                _grayscaleTexture?.Init(
                    gl,
                    TextureWrapMode.MirroredRepeat,
                    TextureWrapMode.MirroredRepeat);
            }
        }
    }

    private static Vertex[] CreateVertexData(BSTriShape shape)
        => shape.VertexDataSSE
        .Select(data =>
        new Vertex
        {
            Position = data.Vertex,
            TexCoord = data.UV.ToVector2(),
            Normal = data.Normal.ToVector3(),
            Bitangent = new(
                data.BitangentX,
                (byte)data.BitangentY / 255f * 2f - 1f,
                (byte)data.BitangentZ / 255f * 2f - 1f),
            Color = data.VertexColors,
        })
        .ToArray();

    private static ushort[] CreateElementData(BSTriShape shape)
        => shape.Triangles
        .SelectMany(triangle => new ushort[] { triangle.V1, triangle.V2, triangle.V3 })
        .ToArray();

    public void Deinit(GL gl)
    {
        gl.DeleteVertexArray(_vertexArray);
        gl.DeleteBuffer(_vertexBuffer);
        gl.DeleteBuffer(_elementBuffer);
    }

    public void Draw(
        GL gl,
        BSEffectShaderProgram.UniformLocations uniforms,
        in Transform? parentTransform = null,
        in Matrix33? overrideRotation = null)
    {
        if (_shaderProperty is null)
            return;

        gl.BindVertexArray(_vertexArray);

        Transform world = _worldTransform;

        if (parentTransform.HasValue)
            world *= parentTransform.Value;

        if (overrideRotation.HasValue)
            world.Rotation = overrideRotation.Value;

        Matrix4x4 World = world.ToMatrix();
        gl.UniformMatrix4(uniforms.World, 1, false, ref World.M11);

        gl.Uniform4(
            uniforms.TexcoordOffset,
            _shaderProperty.UVOffset.U,
            _shaderProperty.UVOffset.V,
            _shaderProperty.UVScale.U,
            _shaderProperty.UVScale.V);

        var baseColor = _shaderProperty.GetBaseColor();
        gl.Uniform4(
            uniforms.BaseColor,
            baseColor.R,
            baseColor.G,
            baseColor.B,
            baseColor.A);

        var baseColorScale = _shaderProperty.GetBaseColorScale();
        gl.Uniform4(
            uniforms.BaseColorScale,
            baseColorScale,
            baseColorScale,
            baseColorScale,
            baseColorScale);

        gl.Uniform4(
            uniforms.LightingInfluence,
            _shaderProperty.GetLightingInfluence() / 255f,
            0f,
            0f,
            0f);

        gl.Uniform4(uniforms.PropertyColor, 1f, 1f, 1f, 1f);

        var shaderFlags1 = _shaderProperty.ShaderFlags_SSPF1;
        var shaderFlags2 = _shaderProperty.ShaderFlags_SSPF2;

        if (shaderFlags1.HasFlag(SkyrimShaderPropertyFlags1.ZBuffer_Test) && !HasAlpha)
        {
            gl.DepthFunc(DepthFunction.Lequal);
        }
        else
        {
            gl.DepthFunc(DepthFunction.Always);
        }
            gl.DepthFunc(DepthFunction.Always);

        gl.Uniform1(
            uniforms.VC,
            shaderFlags2.HasFlag(SkyrimShaderPropertyFlags2.Vertex_Colors) ? 1 : 0);

        gl.Uniform1(
            uniforms.GrayscaleToColor,
            shaderFlags1.HasFlag(SkyrimShaderPropertyFlags1.Greyscale_To_PaletteColor) ? 1 : 0);

        gl.Uniform1(
            uniforms.GrayscaleToAlpha,
            shaderFlags1.HasFlag(SkyrimShaderPropertyFlags1.Greyscale_To_PaletteAlpha) ? 1 : 0);

        gl.DepthMask(
            shaderFlags2.HasFlag(SkyrimShaderPropertyFlags2.ZBuffer_Write));

        if (_alphaProperty is not null && _alphaProperty.Flags.AlphaBlend)
        {
            gl.Disable(EnableCap.PolygonOffsetFill);
            gl.Enable(EnableCap.Blend);

            static BlendingFactor GetBlendingFactor(NiflySharp.Enums.AlphaFunction alphaFunc)
                => alphaFunc switch
                {
                    NiflySharp.Enums.AlphaFunction.ONE => BlendingFactor.One,
                    NiflySharp.Enums.AlphaFunction.ZERO => BlendingFactor.Zero,
                    NiflySharp.Enums.AlphaFunction.SRC_COLOR => BlendingFactor.SrcColor,
                    NiflySharp.Enums.AlphaFunction.INV_SRC_COLOR => BlendingFactor.OneMinusSrcColor,
                    NiflySharp.Enums.AlphaFunction.DEST_COLOR => BlendingFactor.DstColor,
                    NiflySharp.Enums.AlphaFunction.INV_DEST_COLOR => BlendingFactor.OneMinusDstColor,
                    NiflySharp.Enums.AlphaFunction.SRC_ALPHA => BlendingFactor.SrcAlpha,
                    NiflySharp.Enums.AlphaFunction.INV_SRC_ALPHA => BlendingFactor.OneMinusSrcAlpha,
                    NiflySharp.Enums.AlphaFunction.DEST_ALPHA => BlendingFactor.DstAlpha,
                    NiflySharp.Enums.AlphaFunction.INV_DEST_ALPHA => BlendingFactor.OneMinusDstAlpha,
                    NiflySharp.Enums.AlphaFunction.SRC_ALPHA_SATURATE => BlendingFactor.SrcAlphaSaturate,
                    _ => BlendingFactor.One,
                };

            gl.BlendFunc(
                GetBlendingFactor(_alphaProperty.Flags.SourceBlendMode),
                GetBlendingFactor(_alphaProperty.Flags.DestinationBlendMode));
        }
        else
        {
            gl.Disable(EnableCap.Blend);
        }

        if (_sourceTexture is not null)
        {
            gl.Uniform1(uniforms.UseTexture, 1);
            _sourceTexture.Bind(gl, 1);
            gl.Uniform1(uniforms.BaseSampler, 1);
        }
        else
        {
            gl.Uniform1(uniforms.UseTexture, 0);
            gl.Uniform1(uniforms.BaseSampler, 0);
        }

        if (_grayscaleTexture is not null)
        {
            _grayscaleTexture.Bind(gl, 2);
            gl.Uniform1(uniforms.GrayscaleSampler, 2);
        }
        else
        {
            gl.Uniform1(uniforms.GrayscaleSampler, 0);
        }

        gl.BindBuffer(GLEnum.ArrayBuffer, _vertexBuffer);
        gl.BindBuffer(GLEnum.ElementArrayBuffer, _elementBuffer);
        gl.DrawElements(GLEnum.Triangles, _elementCount, GLEnum.UnsignedShort, new ReadOnlySpan<ushort>());

        gl.BindVertexArray(0);
    }
}
