// SPDX-License-Identifier: GPL-3.0-or-later
using NiflySharp.Structs;
using System.Numerics;

namespace PerkTreeEditor;

internal struct Transform()
{
    public Matrix33 Rotation = new() { M11 = 1f, M22 = 1f, M33 = 1f };
    public Vector3 Translation = Vector3.Zero;
    public float Scale = 1.0f;

    public static readonly Transform Identity = new();

    public static Vector3 Rotate(in Vector3 position, in Matrix33 mat)
        => new()
        {
            X = position.X * mat.M11 + position.Y * mat.M21 + position.Z * mat.M31,
            Y = position.X * mat.M12 + position.Y * mat.M22 + position.Z * mat.M32,
            Z = position.X * mat.M13 + position.Y * mat.M23 + position.Z * mat.M33,
        };

    public static Matrix33 Multiply(in Matrix33 lhs, in Matrix33 rhs)
        => new()
        {
            M11 = lhs.M11 * rhs.M11 + lhs.M12 * rhs.M21 + lhs.M13 * rhs.M31,
            M12 = lhs.M11 * rhs.M12 + lhs.M12 * rhs.M22 + lhs.M13 * rhs.M32,
            M13 = lhs.M11 * rhs.M13 + lhs.M12 * rhs.M23 + lhs.M13 * rhs.M33,
            M21 = lhs.M21 * rhs.M11 + lhs.M22 * rhs.M21 + lhs.M23 * rhs.M31,
            M22 = lhs.M21 * rhs.M12 + lhs.M22 * rhs.M22 + lhs.M23 * rhs.M32,
            M23 = lhs.M21 * rhs.M13 + lhs.M22 * rhs.M23 + lhs.M23 * rhs.M33,
            M31 = lhs.M31 * rhs.M11 + lhs.M32 * rhs.M21 + lhs.M33 * rhs.M31,
            M32 = lhs.M31 * rhs.M12 + lhs.M32 * rhs.M22 + lhs.M33 * rhs.M32,
            M33 = lhs.M31 * rhs.M13 + lhs.M32 * rhs.M23 + lhs.M33 * rhs.M33,
        };

    public static Transform operator *(in Transform lhs, in Transform rhs)
        => new()
        {
            Rotation = Multiply(lhs.Rotation, rhs.Rotation),
            Translation = Rotate(lhs.Translation * rhs.Scale, rhs.Rotation) + rhs.Translation,
            Scale = lhs.Scale * rhs.Scale,
        };

    public readonly Matrix4x4 ToMatrix()
    {
        var scale = Matrix4x4.CreateScale(Scale);
        var rotation = new Matrix4x4
        {
            M11 = Rotation.M11,
            M12 = Rotation.M12,
            M13 = Rotation.M13,
            M14 = 0,
            M21 = Rotation.M21,
            M22 = Rotation.M22,
            M23 = Rotation.M23,
            M24 = 0,
            M31 = Rotation.M31,
            M32 = Rotation.M32,
            M33 = Rotation.M33,
            M34 = 0,
            M41 = 0,
            M42 = 0,
            M43 = 0,
            M44 = 1,
        };
        var translation = Matrix4x4.CreateTranslation(Translation);
        return scale * rotation * translation;
    }
};
