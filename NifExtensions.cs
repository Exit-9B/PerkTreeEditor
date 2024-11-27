// SPDX-License-Identifier: GPL-3.0-or-later
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Structs;
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace PerkTreeEditor;

[Flags]
internal enum NiAVObjectFlags
{
    Hidden = 0x1,
    SelectiveUpdate = 0x2,
    SelectiveUpdateTransforms = 0x4,
    SelectiveUpdateController = 0x8,
    SelectiveUpdateRigid = 0x10,
    DisplayUIObject = 0x20,
    DisableSorting = 0x40,
    SelectiveUpdateTransformsOverride = 0x80,
    SaveExternalGeomData = 0x200,
    NoDecals = 0x400,
    AlwaysDraw = 0x800,
    MeshLODFO4 = 0x1000,
    FixedBound = 0x2000,
    TopFadeNode = 0x4000,
    IgnoreFade = 0x8000,
    NoAnimSyncX = 0x10000,
    NoAnimSyncY = 0x20000,
    NoAnimSyncZ = 0x40000,
    NoAnimSyncS = 0x80000,
    NoDismember = 0x100000,
    NoDismemberValidity = 0x200000,
    RenderUse = 0x400000,
    MaterialsApplied = 0x800000,
    HighDetail = 0x1000000,
    ForceUpdate = 0x2000000,
    PreProcessedNode = 0x4000000,
    MeshLODSkyrim = 0x8000000,
}


internal static class NifExtensions
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_sourceTexture")]
    public extern static ref NiString4 GetSourceTexture(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_greyscaleTexture")]
    public extern static ref NiString4 GetGrayscaleTexture(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_baseColor")]
    public extern static ref Color4 GetBaseColor(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_baseColorScale")]
    public extern static ref float GetBaseColorScale(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_lightingInfluence")]
    public extern static ref byte GetLightingInfluence(this BSEffectShaderProperty @this);

    public static Vector2 ToVector2(this HalfTexCoord uv)
        => new((float)uv.U, (float)uv.V);

    public static Vector3 ToVector3(this ByteVector3 bvec)
        => new(
            (byte)bvec.X / 255f * 2f - 1f,
            (byte)bvec.Y / 255f * 2f - 1f,
            (byte)bvec.Z / 255f * 2f - 1f);

    public static Transform LocalTransform(this NiAVObject obj)
        => new()
        {
            Rotation = obj.Rotation,
            Translation = obj.Translation,
            Scale = obj.Scale,
        };

    public static Transform WorldTransform(this NiAVObject obj, NifFile nif)
    {
        Transform result = obj.LocalTransform();
        for (var node = nif.GetParentNode(obj); node is not null; node = nif.GetParentNode(node))
        {
            result = node.LocalTransform() * result;
        }
        return result;
    }

    public static void UpdateTransform(
        this NiMultiTargetTransformController _,
        NiAVObject target,
        NiTransformData data,
        float time)
    {
        // TODO: Update rotation and scale
        var (pos, interp) = TimePos(data.Translations, time);
        if (pos < data.Translations.NumKeys)
        {
            var keys = data.Translations.Keys;
            if (interp > 0.0f)
            {
                // TODO: Quadratic and constant interpolation
                var v1 = keys[pos].Value;
                var v2 = keys[pos + 1].Value;
                target.Translation = Vector3.Lerp(v1, v2, interp);
            }
            else
            {
                target.Translation = keys[pos].Value;
            }
        }
    }

    private static (int, float) TimePos<T>(in KeyGroup<T> keyGroup, float time)
    {
        if (keyGroup.NumKeys == 0)
        {
            return (0, 0.0f);
        }

        int first = 0;
        int last = (int)keyGroup.NumKeys;
        int count = last - first;

        while (count > 0)
        {
            var i = first;
            var step = count / 2;
            i += step;

            if (keyGroup.Keys[i].Time < time)
            {
                first = ++i;
                count -= step + 1;
            }
            else
            {
                count = step;
            }
        }

        if (first < keyGroup.NumKeys - 1)
        {
            var t0 = keyGroup.Keys[first].Time;
            var t1 = keyGroup.Keys[first + 1].Time;
            return (first, (time - t0) / (t1 - t0));
        }
        else
        {
            return ((int)keyGroup.NumKeys - 1, 0.0f);
        }
    }
}
