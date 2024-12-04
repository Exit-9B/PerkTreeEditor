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
    public extern static ref NiString4 SourceTexture(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_greyscaleTexture")]
    public extern static ref NiString4 GrayscaleTexture(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_falloffStartAngle")]
    public extern static ref float FalloffStartAngle(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_falloffStopAngle")]
    public extern static ref float FalloffStopAngle(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_falloffStartOpacity")]
    public extern static ref float FalloffStartOpacity(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_falloffStopOpacity")]
    public extern static ref float FalloffStopOpacity(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_baseColor")]
    public extern static ref Color4 BaseColor(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_baseColorScale")]
    public extern static ref float BaseColorScale(this BSEffectShaderProperty @this);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_lightingInfluence")]
    public extern static ref byte LightingInfluence(this BSEffectShaderProperty @this);

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

    public static void GotoTime(this NiControllerSequence controllerSequence, NifFile nif, float time)
    {
        foreach (var block in controllerSequence.ControlledBlocks)
        {
            NiTimeController? controller = nif.GetBlock(block.Controller);
            NiInterpolator? interpolator = nif.GetBlock(block.Interpolator);
            string? nodeName = block.NodeName.String;

            controller?.Update(interpolator, nodeName, nif, controllerSequence.StopTime);
        }
    }

    public static void Update(
        this NiTimeController controller,
        NiInterpolator? interpolator,
        string? nodeName,
        NifFile nif,
        float time)
    {
        if (interpolator is NiTransformInterpolator transformInterpolator)
        {
            NiTransformData? data = nif.GetBlock(transformInterpolator.Data);
            if (data is null)
                return;

            if (controller is NiMultiTargetTransformController transformController)
            {
                // TODO: Update rotation
                float scale = Interpolate(data.Scales, time);
                Vector3 translation = Interpolate(data.Translations, time);
                foreach (var extraTargetRef in transformController.ExtraTargets.References)
                {
                    var extraTarget = nif.GetBlock(extraTargetRef);
                    if (extraTarget is null || extraTarget.Name.String != nodeName)
                        continue;

                    if (data.Scales.NumKeys != 0)
                        extraTarget.Scale = scale;

                    if (data.Translations.NumKeys != 0)
                        extraTarget.Translation = translation;
                }
            }
        }
        else if (interpolator is NiPoint3Interpolator pointInterpolator)
        {
            NiPosData? data = nif.GetBlock(pointInterpolator.Data);
            if (data is null || data.Data.NumKeys == 0)
                return;

            if (controller is BSEffectShaderPropertyColorController colorController)
            {
                BSEffectShaderProperty? shaderProperty = nif.GetBlock<BSEffectShaderProperty>(controller.Target);
                if (shaderProperty is null)
                    return;

                Vector3 color = Interpolate(data.Data, time);
                switch (colorController.ControlledColor)
                {
                    case NiflySharp.Enums.EffectShaderControlledColor.EmissiveColor:
                        ref Color4 emissiveColor = ref shaderProperty.BaseColor();
                        emissiveColor.R = color[0];
                        emissiveColor.G = color[1];
                        emissiveColor.B = color[2];
                        break;
                }
            }
        }
        else if (interpolator is NiFloatInterpolator floatInterpolator)
        {
            NiFloatData? data = nif.GetBlock(floatInterpolator.Data);
            if (data is null || data.Data.NumKeys == 0)
                return;

            if (controller is BSEffectShaderPropertyFloatController floatController)
            {
                BSEffectShaderProperty? shaderProperty = nif.GetBlock<BSEffectShaderProperty>(controller.Target);
                if (shaderProperty is null)
                    return;

                float value = Interpolate(data.Data, time);
                switch (floatController.ControlledVariable)
                {
                    case NiflySharp.Enums.EffectShaderControlledVariable.EmissiveMultiple:
                        shaderProperty.BaseColorScale() = value;
                        break;
                    case NiflySharp.Enums.EffectShaderControlledVariable.FalloffStartAngle:
                        shaderProperty.FalloffStartAngle() = value;
                        break;
                    case NiflySharp.Enums.EffectShaderControlledVariable.FalloffStopAngle:
                        shaderProperty.FalloffStopAngle() = value;
                        break;
                    case NiflySharp.Enums.EffectShaderControlledVariable.FalloffStartOpacity:
                        shaderProperty.FalloffStartOpacity() = value;
                        break;
                    case NiflySharp.Enums.EffectShaderControlledVariable.FalloffStopOpacity:
                        shaderProperty.FalloffStopOpacity() = value;
                        break;
                    case NiflySharp.Enums.EffectShaderControlledVariable.AlphaTransparency:
                        shaderProperty.BaseColor().A = value;
                        break;
                    case NiflySharp.Enums.EffectShaderControlledVariable.UOffset:
                        shaderProperty.UVOffset = new(value, shaderProperty.UVOffset.V);
                        break;
                    case NiflySharp.Enums.EffectShaderControlledVariable.UScale:
                        shaderProperty.UVScale = new(value, shaderProperty.UVScale.V);
                        break;
                    case NiflySharp.Enums.EffectShaderControlledVariable.VOffset:
                        shaderProperty.UVOffset = new(shaderProperty.UVOffset.U, value);
                        break;
                    case NiflySharp.Enums.EffectShaderControlledVariable.VScale:
                        shaderProperty.UVScale = new(shaderProperty.UVScale.U, value);
                        break;
                }
            }
        }
    }

    public static float Interpolate(in KeyGroup<float> keyGroup, float time)
    {
        var (pos, interp) = TimePos(keyGroup, time);
        if (pos >= keyGroup.NumKeys)
            return default;

        var keys = keyGroup.Keys;
        int next = Math.Min(pos + 1, keys.Count - 1);
        var v1 = keys[pos].Value;
        var v2 = keys[next].Value;

        switch (keyGroup.Interpolation)
        {
            default:
            case NiflySharp.Enums.KeyType.LINEAR_KEY:
                return float.Lerp(v1, v2, interp);
            case NiflySharp.Enums.KeyType.QUADRATIC_KEY:
                float t1 = keys[pos].Backward;
                float t2 = keys[next].Forward;
                float x = interp;
                float x2 = x * x;
                float x3 = x2 * x;
                return v1 * (2f * x3 - 3f * x2 + 1f)
                    + v2 * (-2f * x3 + 3f * x2)
                    + t1 * (x3 - 2f * x2 + x)
                    + t2 * (x3 - x2);
            case NiflySharp.Enums.KeyType.CONST_KEY:
                return interp < 0.5f ? v1 : v2;
        }
    }

    public static Vector3 Interpolate(in KeyGroup<Vector3> keyGroup, float time)
    {
        var (pos, interp) = TimePos(keyGroup, time);
        if (pos >= keyGroup.NumKeys)
            return default;

        var keys = keyGroup.Keys;
        if (interp > 0.0f)
        {
            // TODO: Quadratic and constant interpolation
            var v1 = keys[pos].Value;
            var v2 = keys[pos + 1].Value;
            return Vector3.Lerp(v1, v2, interp);
        }
        else
        {
            return keys[pos].Value;
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
