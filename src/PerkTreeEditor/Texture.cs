// SPDX-License-Identifier: GPL-3.0-or-later
using DirectXTexNet;
using Silk.NET.OpenGL;
using System;
using System.IO;

namespace PerkTreeEditor;

internal class Texture
{
    private readonly TexMetadata metadata;
    private readonly ScratchImage scratch;

    internal readonly struct FormatDesc(
        InternalFormat internalFormat,
        PixelFormat externalFormat = 0,
        PixelType type = 0,
        bool isExternalBGRA = false)
    {
        public readonly InternalFormat Internal = internalFormat;
        public readonly PixelFormat External = externalFormat;
        public readonly PixelType Type = type;
        public readonly bool IsExternalBGRA = isExternalBGRA;
    };

    internal TextureTarget Target { get; }
    internal FormatDesc Format { get; }
    public int ArraySize { get; }
    public int MipLevels { get; }

    private uint _texture;

    internal bool IsCompressed => Format.External == 0;

    public bool IsCubemap =>
        Target == TextureTarget.TextureCubeMap ||
        Target == TextureTarget.TextureCubeMapArray;

    public unsafe Texture(Stream stream)
    {
        byte[] bytes;
        using (MemoryStream ms = new())
        {
            stream.CopyTo(ms);
            bytes = ms.ToArray();
        }

        fixed (byte* ptr = bytes)
        {
            var flags = DDS_FLAGS.FORCE_RGB;
            metadata = TexHelper.Instance.GetMetadataFromDDSMemory((nint)ptr, bytes.Length, flags);
            scratch = TexHelper.Instance.LoadFromDDSMemory((nint)ptr, bytes.Length, flags);
        }

        Target = GetTarget(metadata);
        Format = GetFormat(metadata);
        ArraySize = metadata.ArraySize;
        MipLevels = metadata.MipLevels;
    }

    public void Init(GL gl, TextureWrapMode wrapS, TextureWrapMode wrapT, bool useMipmap)
    {
        if (_texture != 0)
            return;

        _texture = gl.GenTexture();

        gl.BindTexture(Target, _texture);

        gl.TexParameter(Target, TextureParameterName.TextureMaxLevel, MipLevels - 1);

        var minFilter = useMipmap && MipLevels > 1
            ? TextureMinFilter.LinearMipmapLinear
            : TextureMinFilter.Linear;

        gl.TexParameter(Target, TextureParameterName.TextureMinFilter, (int)minFilter);
        gl.TexParameter(Target, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(Target, TextureParameterName.TextureWrapS, (int)wrapS);
        gl.TexParameter(Target, TextureParameterName.TextureWrapT, (int)wrapT);

        for (int layer = 0; layer < ArraySize; ++layer)
        {
            for (int face = 0; face < (IsCubemap ? 6 : 1); ++face)
            {
                for (int level = 0; level < MipLevels; ++level)
                {
                    LoadTextureImage(gl, layer, face, level);
                }
            }
        }

        gl.BindTexture(Target, 0);
    }

    public void Bind(GL gl, int unit)
    {
        gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + unit));
        gl.BindTexture(TextureTarget.Texture2D, _texture);
    }

    private static TextureTarget GetTarget(TexMetadata metadata)
    {
        if (metadata.IsCubemap())
        {
            if (metadata.ArraySize > 1)
            {
                return TextureTarget.TextureCubeMapArray;
            }
            else
            {
                return TextureTarget.TextureCubeMap;
            }
        }
        else if (metadata.ArraySize > 1)
        {
            if (metadata.Dimension != TEX_DIMENSION.TEXTURE1D)
            {
                return TextureTarget.Texture2DArray;
            }
            else
            {
                return TextureTarget.Texture1DArray;
            }
        }
        else if (metadata.Dimension == TEX_DIMENSION.TEXTURE1D)
        {
            return TextureTarget.Texture1D;
        }
        else if (metadata.Dimension == TEX_DIMENSION.TEXTURE3D || metadata.IsVolumemap())
        {
            return TextureTarget.Texture3D;
        }
        else
        {
            return TextureTarget.Texture2D;
        }
    }

    private static FormatDesc GetFormat(TexMetadata metadata)
    {
        bool hasSwizzle = false;
        var externalBGRA = hasSwizzle ? PixelFormat.Rgba : PixelFormat.Bgra;
        var internalBGRA = (InternalFormat)0x93A1;

        return metadata.Format switch
        {
            DXGI_FORMAT.B4G4R4A4_UNORM => new(InternalFormat.Rgba4, PixelFormat.Rgba, PixelType.UnsignedShort4444, true),
            DXGI_FORMAT.B5G6R5_UNORM => new(InternalFormat.Rgb565, PixelFormat.Rgb, PixelType.UnsignedShort565, true),
            DXGI_FORMAT.B5G5R5A1_UNORM => new(InternalFormat.Rgb5A1, PixelFormat.Rgba, PixelType.UnsignedShort5551, true),

            DXGI_FORMAT.R8_UNORM => new(InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte),
            DXGI_FORMAT.R8_SNORM => new(InternalFormat.R8SNorm, PixelFormat.Red, PixelType.Byte),
            DXGI_FORMAT.R8_UINT => new(InternalFormat.R8ui, PixelFormat.RedInteger, PixelType.UnsignedByte),
            DXGI_FORMAT.R8_SINT => new(InternalFormat.R8i, PixelFormat.RedInteger, PixelType.Byte),

            DXGI_FORMAT.R8G8_UNORM => new(InternalFormat.RG8, PixelFormat.RG, PixelType.UnsignedByte),
            DXGI_FORMAT.R8G8_SNORM => new(InternalFormat.RG8SNorm, PixelFormat.RG, PixelType.Byte),
            DXGI_FORMAT.R8G8_UINT => new(InternalFormat.RG8ui, PixelFormat.RGInteger, PixelType.UnsignedByte),
            DXGI_FORMAT.R8G8_SINT => new(InternalFormat.RG8i, PixelFormat.RGInteger, PixelType.Byte),

            DXGI_FORMAT.R8G8B8A8_UNORM => new(InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte),
            DXGI_FORMAT.R8G8B8A8_SNORM => new(InternalFormat.Rgba8SNorm, PixelFormat.Rgba, PixelType.Byte),
            DXGI_FORMAT.R8G8B8A8_UINT => new(InternalFormat.Rgba8ui, PixelFormat.RgbaInteger, PixelType.UnsignedByte),
            DXGI_FORMAT.R8G8B8A8_SINT => new(InternalFormat.Rgba8i, PixelFormat.RgbaInteger, PixelType.Byte),
            DXGI_FORMAT.R8G8B8A8_UNORM_SRGB => throw new NotImplementedException(),

            DXGI_FORMAT.B8G8R8A8_UNORM => new(internalBGRA, externalBGRA, PixelType.UnsignedByte, !hasSwizzle),
            DXGI_FORMAT.B8G8R8A8_UNORM_SRGB => new(InternalFormat.Srgb8Alpha8, externalBGRA, PixelType.UnsignedByte, !hasSwizzle),

            DXGI_FORMAT.R10G10B10A2_UNORM => new(InternalFormat.Rgb10A2, PixelFormat.Rgba, PixelType.UnsignedInt2101010Rev),
            DXGI_FORMAT.R10G10B10A2_UINT => new(InternalFormat.Rgb10A2ui, PixelFormat.RgbaInteger, PixelType.UnsignedInt2101010Rev),

            DXGI_FORMAT.R16_UNORM => new(InternalFormat.R16, PixelFormat.Red, PixelType.UnsignedShort),
            DXGI_FORMAT.R16_SNORM => new(InternalFormat.R16SNorm, PixelFormat.Red, PixelType.Short),
            DXGI_FORMAT.R16_UINT => new(InternalFormat.R16ui, PixelFormat.RedInteger, PixelType.UnsignedShort),
            DXGI_FORMAT.R16_SINT => new(InternalFormat.R16i, PixelFormat.RedInteger, PixelType.Short),
            DXGI_FORMAT.R16_FLOAT => new(InternalFormat.R16f, PixelFormat.Red, PixelType.HalfFloat),

            DXGI_FORMAT.R16G16_UNORM => new(InternalFormat.RG16, PixelFormat.RG, PixelType.UnsignedShort),
            DXGI_FORMAT.R16G16_SNORM => new(InternalFormat.RG16SNorm, PixelFormat.RG, PixelType.Short),
            DXGI_FORMAT.R16G16_UINT => new(InternalFormat.RG16ui, PixelFormat.RGInteger, PixelType.UnsignedShort),
            DXGI_FORMAT.R16G16_SINT => new(InternalFormat.RG16i, PixelFormat.RGInteger, PixelType.Short),
            DXGI_FORMAT.R16G16_FLOAT => new(InternalFormat.RG16f, PixelFormat.RG, PixelType.HalfFloat),

            DXGI_FORMAT.R16G16B16A16_UNORM => new(InternalFormat.Rgba16, PixelFormat.Rgba, PixelType.UnsignedShort),
            DXGI_FORMAT.R16G16B16A16_SNORM => new(InternalFormat.Rgba16SNorm, PixelFormat.Rgba, PixelType.Short),
            DXGI_FORMAT.R16G16B16A16_UINT => new(InternalFormat.Rgba16ui, PixelFormat.RgbaInteger, PixelType.UnsignedShort),
            DXGI_FORMAT.R16G16B16A16_SINT => new(InternalFormat.Rgba16i, PixelFormat.RgbaInteger, PixelType.Short),
            DXGI_FORMAT.R16G16B16A16_FLOAT => new(InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),

            DXGI_FORMAT.R32_UINT => new(InternalFormat.R32ui, PixelFormat.RedInteger, PixelType.UnsignedInt),
            DXGI_FORMAT.R32_SINT => new(InternalFormat.R32i, PixelFormat.RedInteger, PixelType.Int),
            DXGI_FORMAT.R32_FLOAT => new(InternalFormat.R32f, PixelFormat.Red, PixelType.Float),

            DXGI_FORMAT.R32G32_UINT => new(InternalFormat.RG32ui, PixelFormat.RGInteger, PixelType.UnsignedInt),
            DXGI_FORMAT.R32G32_SINT => new(InternalFormat.RG32i, PixelFormat.RGInteger, PixelType.Int),
            DXGI_FORMAT.R32G32_FLOAT => new(InternalFormat.RG32f, PixelFormat.RG, PixelType.Float),

            DXGI_FORMAT.R32G32B32_UINT => new(InternalFormat.Rgb32ui, PixelFormat.RgbInteger, PixelType.UnsignedInt),
            DXGI_FORMAT.R32G32B32_SINT => new(InternalFormat.Rgb32i, PixelFormat.RgbInteger, PixelType.Int),
            DXGI_FORMAT.R32G32B32_FLOAT => new(InternalFormat.Rgb32f, PixelFormat.RgbInteger, PixelType.Float),

            DXGI_FORMAT.R32G32B32A32_UINT => new(InternalFormat.Rgba32ui, PixelFormat.RgbaInteger, PixelType.UnsignedInt),
            DXGI_FORMAT.R32G32B32A32_SINT => new(InternalFormat.Rgba32i, PixelFormat.RgbaInteger, PixelType.Int),
            DXGI_FORMAT.R32G32B32A32_FLOAT => new(InternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float),

            DXGI_FORMAT.R11G11B10_FLOAT => new(InternalFormat.R11fG11fB10f, PixelFormat.Rgb, PixelType.UnsignedInt10f11f11fRev),
            DXGI_FORMAT.R9G9B9E5_SHAREDEXP => new(InternalFormat.Rgb9E5, PixelFormat.Rgb, PixelType.UnsignedInt5999Rev),

            DXGI_FORMAT.D16_UNORM => new(InternalFormat.DepthComponent16, PixelFormat.DepthComponent),
            DXGI_FORMAT.D32_FLOAT => new(InternalFormat.DepthComponent32f, PixelFormat.DepthComponent),
            DXGI_FORMAT.D24_UNORM_S8_UINT => new(InternalFormat.Depth24Stencil8, PixelFormat.DepthStencil),
            DXGI_FORMAT.D32_FLOAT_S8X24_UINT => new(InternalFormat.Depth32fStencil8, PixelFormat.DepthStencil),

            DXGI_FORMAT.BC1_UNORM => new(InternalFormat.CompressedRgbaS3TCDxt1Ext),
            DXGI_FORMAT.BC1_UNORM_SRGB => new(InternalFormat.CompressedSrgbAlphaS3TCDxt1Ext),
            DXGI_FORMAT.BC2_UNORM => new(InternalFormat.CompressedRgbaS3TCDxt3Ext),
            DXGI_FORMAT.BC2_UNORM_SRGB => new(InternalFormat.CompressedSrgbAlphaS3TCDxt3Ext),
            DXGI_FORMAT.BC3_UNORM => new(InternalFormat.CompressedRgbaS3TCDxt5Ext),
            DXGI_FORMAT.BC3_UNORM_SRGB => new(InternalFormat.CompressedSrgbAlphaS3TCDxt5Ext),
            DXGI_FORMAT.BC4_UNORM => new(InternalFormat.CompressedRedRgtc1),
            DXGI_FORMAT.BC4_SNORM => new(InternalFormat.CompressedSignedRedRgtc1),
            DXGI_FORMAT.BC5_UNORM => new(InternalFormat.CompressedRGRgtc2),
            DXGI_FORMAT.BC5_SNORM => new(InternalFormat.CompressedSignedRGRgtc2),
            DXGI_FORMAT.BC6H_UF16 => new(InternalFormat.CompressedRgbBptcUnsignedFloat),
            DXGI_FORMAT.BC6H_SF16 => new(InternalFormat.CompressedRgbBptcSignedFloat),
            DXGI_FORMAT.BC7_UNORM => new(InternalFormat.CompressedRgbaBptcUnorm),
            DXGI_FORMAT.BC7_UNORM_SRGB => new(InternalFormat.CompressedSrgbAlphaBptcUnorm),

            DXGI_FORMAT.B8G8R8X8_UNORM => new(InternalFormat.Rgb8, PixelFormat.Bgra, PixelType.UnsignedByte),
            DXGI_FORMAT.B8G8R8X8_UNORM_SRGB => new(InternalFormat.Srgb8, PixelFormat.Bgra, PixelType.UnsignedByte),

            _ => throw new NotImplementedException(),
        };
    }

    private unsafe void LoadTextureImage(GL gl, int layer, int face, int level)
    {
        var target = IsCubemap ? TextureTarget.TextureCubeMapPositiveX + face : Target;
        var image = scratch.GetImage(level, face, layer);

        switch (Target)
        {
            case TextureTarget.Texture1D:
                if (IsCompressed)
                {
                    gl.CompressedTexImage1D(
                        target,
                        level,
                        Format.Internal,
                        (uint)image.Width,
                        0,
                        (uint)image.SlicePitch,
                        (void*)image.Pixels);
                }
                else
                {
                    gl.TexImage1D(
                        target,
                        level,
                        Format.Internal,
                        (uint)image.Width,
                        0,
                        Format.External,
                        Format.Type,
                        (void*)image.Pixels);
                }
                break;

            case TextureTarget.Texture1DArray:
            case TextureTarget.Texture2D:
            case TextureTarget.TextureCubeMap:
                if (IsCompressed)
                {
                    gl.CompressedTexImage2D(
                        target,
                        level,
                        Format.Internal,
                        (uint)image.Width,
                        (uint)(Target == TextureTarget.Texture1DArray ? layer : image.Height),
                        0,
                        (uint)image.SlicePitch,
                        (void*)image.Pixels);
                }
                else
                {
                    gl.TexImage2D(
                        target,
                        level,
                        Format.Internal,
                        (uint)image.Width,
                        (uint)(Target == TextureTarget.Texture1DArray ? layer : image.Height),
                        0,
                        Format.External,
                        Format.Type,
                        (void*)image.Pixels);
                }
                break;

            case TextureTarget.Texture2DArray:
            case TextureTarget.Texture3D:
            case TextureTarget.TextureCubeMapArray:
                if (IsCompressed)
                {
                    gl.CompressedTexImage3D(
                        target,
                        level,
                        Format.Internal,
                        (uint)image.Width,
                        (uint)image.Height,
                        (uint)layer,
                        0,
                        (uint)image.SlicePitch,
                        (void*)image.Pixels);
                }
                else
                {
                    gl.TexImage3D(
                        target,
                        level,
                        Format.Internal,
                        (uint)image.Width,
                        (uint)image.Height,
                        (uint)layer,
                        0,
                        Format.External,
                        Format.Type,
                        (void*)image.Pixels);
                }
                break;
        }
    }
}
