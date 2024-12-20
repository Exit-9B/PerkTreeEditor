﻿// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using Mutagen.Bethesda.Skyrim;
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Structs;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace PerkTreeEditor;

public class SkydomeView : OpenGlControlBase, ICustomHitTest
{
    const double ClickToleranceBase = 1.5;
    const double ClickToleranceMin = 8.0;
    const float PerkYMax = 100f;
    const float PerkYMin = -3f;

    private float _nearClip = 1.0f;
    private float _farClip = 20480.0f;
    private float _fov = 36.0f;
    private float _skillsLookAtX = 0.0f;
    private float _skillsLookAtY = 0.0f;
    private float _skillsLookAtZ = 12.0f;

    private int _selectedIndex = 0;
    private PerkTree.Node? draggingNode;

    private Transform _cachedPointTransform;

    private readonly BSEffectShaderProgram _effectShaderProgram = new();

    private readonly List<BSEffectShaderProperty?> _cImageShaders = [];
    private List<OpenGlShape> _skydomeShapes = [];
    private bool _reloadSkydome;

    private List<OpenGlShape> _starsShapes = [];
    private bool _reloadStars;

    private List<OpenGlShape> _lineShapes = [];
    private bool _reloadLine;

    private GL? gl;

    internal float NearClip
    {
        get => _nearClip;
        set
        {
            _nearClip = value;
            RequestNextFrameRendering();
        }
    }

    internal float FarClip
    {
        get => _farClip;
        set
        {
            _farClip = value;
            RequestNextFrameRendering();
        }
    }

    internal float FOV
    {
        get => _fov;
        set
        {
            _fov = value;
            RequestNextFrameRendering();
        }
    }

    internal float SkillsLookAtZ
    {
        get => _skillsLookAtZ;
        set
        {
            _skillsLookAtZ = value;
            UpdateLookAt();
            RequestNextFrameRendering();
        }
    }

    internal Vector3 SkillsLookAt
    {
        get => new(_skillsLookAtX, _skillsLookAtY, _skillsLookAtZ);
        set
        {
            _skillsLookAtX = value.X;
            _skillsLookAtY = value.Y;
            _skillsLookAtZ = value.Z;
            UpdateLookAt();
            RequestNextFrameRendering();
        }
    }

    internal float LineScale { get; set; } = 0.1175f;
    internal float StarXIncrement { get; set; } = 5.0f;
    internal float StarYIncrement { get; set; } = 15.0f;
    internal float StarZIncrement { get; set; } = -9.0f;
    internal float StarZInitialOffset { get; set; } = 5.0f;
    internal float StarScale { get; set; } = 0.15f;

    internal int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _selectedIndex = value;
            UpdateLookAt();

            if (_selectedIndex < 0 || _selectedIndex >= _cImageShaders.Count)
                return;

            var targetShader = _cImageShaders[_selectedIndex];
            _cImageShaders.ForEach(shader =>
            {
                if (shader is not null)
                    shader.BaseColor().A = shader == targetShader ? 1.0f : 0.0f;
            });

            RequestNextFrameRendering();
        }
    }
    internal OpenGlShape.TextureResolver ResolveTexture { get; set; } = _ => null;

    internal PerkTree? SelectedTree { get; set; }

    private NifFile? Skydome { get; set; }
    private NifFile? PerkStars { get; set; }
    private NifFile? PerkLine { get; set; }

    private Vector3 CameraPosition { get; set; }
    private Vector3 CameraUp { get; set; }
    private Vector3 CameraLookAt { get; set; }

    private Matrix4x4 CameraView => Matrix4x4.CreateLookTo(CameraPosition, CameraLookAt, CameraUp);

    private Matrix4x4 CameraProjection =>
            Matrix4x4.CreatePerspectiveFieldOfView(
                float.DegreesToRadians(FOV),
                (float)(Bounds.Width / Bounds.Height),
                NearClip,
                FarClip);

    protected override void OnOpenGlInit(GlInterface gl_)
    {
        gl = GL.GetApi(gl_.GetProcAddress);
        gl.ClearColor(0.0f, 0.0f, 0.0f, 0.0f);
        _effectShaderProgram.Init(gl);
    }

    protected override void OnOpenGlDeinit(GlInterface gl_)
    {
        if (gl is null)
            return;

        _effectShaderProgram.Deinit(gl);
        _skydomeShapes.ForEach(shape => shape.Deinit(gl));
        _skydomeShapes.Clear();
        _starsShapes.ForEach(shape => shape.Deinit(gl));
        _starsShapes.Clear();
        _lineShapes.ForEach(shape => shape.Deinit(gl));
        _lineShapes.Clear();
    }

    protected override void OnOpenGlLost()
    {
        Debug.WriteLine("OpenGL context was lost.");
    }

    protected override void OnOpenGlRender(GlInterface gl_, int fb)
    {
        if (gl is null)
            return;

        gl.Viewport(0, 0, (uint)Bounds.Width, (uint)Bounds.Height);
        gl.Enable(EnableCap.CullFace);
        gl.CullFace(TriangleFace.Back);

        if (_reloadSkydome)
        {
            _skydomeShapes.ForEach(shape => shape.Deinit(gl));
            if (Skydome is not null)
            {
                _skydomeShapes = InitGl(Skydome);
            }
            _reloadSkydome = false;
        }

        if (_reloadStars)
        {
            _starsShapes.ForEach(shape => shape.Deinit(gl));
            if (PerkStars is not null)
            {
                _starsShapes = InitGl(PerkStars);
                _starsShapes.Sort((s1, s2) =>
                {
                    float z1 = s1.WorldTransform.Translation.Z;
                    float z2 = s2.WorldTransform.Translation.Z;
                    return z1.CompareTo(z2);
                });
            }
            _reloadStars = false;
        }

        if (_reloadLine)
        {
            _lineShapes.ForEach(shape => shape.Deinit(gl));
            if (PerkLine is not null)
            {
                _lineShapes = InitGl(PerkLine);
            }
            _reloadLine = false;
        }

        gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_effectShaderProgram.Uniforms is null)
            return;

        Matrix4x4 cameraViewProj = CameraView * CameraProjection;

        using var ctx = _effectShaderProgram.Use(gl);

        gl.UniformMatrix4(
            _effectShaderProgram.Uniforms.CameraViewProj,
            1,
            false,
            ref cameraViewProj.M11);

        foreach (var shape in _skydomeShapes)
        {
            shape.Draw(gl, _effectShaderProgram.Uniforms);
        }

        if (SelectedTree is not null)
        {
            var parentTransform = _cachedPointTransform;
            var zaxis = Vector3.Normalize(-CameraLookAt);
            var xaxis = Vector3.Normalize(Vector3.Cross(CameraUp, zaxis));
            var yaxis = Vector3.Cross(zaxis, xaxis);
            var starsBillboard = new Matrix33
            {
                M11 = xaxis[0],
                M12 = xaxis[1],
                M13 = xaxis[2],
                M21 = yaxis[0],
                M22 = yaxis[1],
                M23 = yaxis[2],
                M31 = zaxis[0],
                M32 = zaxis[1],
                M33 = zaxis[2],
            };

            foreach (var (index, node) in SelectedTree.Nodes)
            {
                if (index == 0)
                    continue;

                Vector3 starsPos = GetPerkPosition(node);

                var starsTransform = new Transform
                {
                    Scale = StarScale,
                    Translation = starsPos,
                } * parentTransform;

                foreach (var starsShape in _starsShapes)
                {
                    var transform = new Transform
                    {
                        Scale = StarScale,
                        Translation = starsPos,
                    } * parentTransform;
                    transform.Rotation = starsBillboard;

                    starsShape.Draw(gl, _effectShaderProgram.Uniforms, transform);
                }

                foreach (var childIndex in node.Children)
                {
                    if (SelectedTree.Nodes.TryGetValue(childIndex, out var child))
                    {
                        var childPos = GetPerkPosition(child);
                        var lineVector = childPos - starsPos;
                        var lineLength = lineVector.Length();
                        var yline = lineVector / lineLength;
                        var zline = Vector3.Cross(CameraUp, yline);
                        var xline = Vector3.Cross(yline, zline);

                        var lineScale = lineLength * LineScale;
                        var invScale = 1.0f / lineScale;
                        var lineRotation = new Matrix33
                        {
                            M11 = xline[0] * invScale,
                            M12 = xline[1] * invScale,
                            M13 = xline[2] * invScale,
                            M21 = yline[0],
                            M22 = yline[1],
                            M23 = yline[2],
                            M31 = zline[0] * invScale,
                            M32 = zline[1] * invScale,
                            M33 = zline[2] * invScale,
                        };

                        var lineTransform = new Transform
                        {
                            Rotation = lineRotation,
                            Translation = starsPos,
                            Scale = lineScale * StarScale,
                        } * parentTransform;

                        Matrix4x4.Invert(lineTransform.ToMatrix(), out var lineTransformInv);
                        var ylocal = Vector3.UnitY;
                        var zlocal = Vector3.Normalize(
                            Vector3.TransformNormal(Vector3.Normalize(CameraLookAt), lineTransformInv));
                        var xlocal = Vector3.Cross(zlocal, ylocal);
                        var lineBillboard = Transform.Multiply(
                            new Matrix33
                            {
                                M11 = xlocal[0],
                                M12 = xlocal[1],
                                M13 = xlocal[2],
                                M21 = ylocal[0],
                                M22 = ylocal[1],
                                M23 = ylocal[2],
                                M31 = zlocal[0],
                                M32 = zlocal[1],
                                M33 = zlocal[2],
                            },
                            lineTransform.Rotation);

                        foreach (var lineShape in _lineShapes)
                        {
                            lineShape.Draw(gl, _effectShaderProgram.Uniforms, lineTransform, lineBillboard);
                        }
                    }
                }
            }
        }
    }

    private void UpdateLookAt()
    {
        NiNode? point = Skydome?.FindBlockByName<NiNode>($"Point{_selectedIndex:00}");
        if (point is not null)
        {
            CameraLookAt = point.Translation + SkillsLookAt;
            _cachedPointTransform = point.WorldTransform(Skydome!);
        }
    }

    private List<OpenGlShape> InitGl(NifFile nif)
    {
        static bool ShapeIsVisible(INiShape shape) =>
            !((NiAVObjectFlags)shape.Flags_ui).HasFlag(NiAVObjectFlags.Hidden);

        return
            nif.Blocks
            .OfType<INiShape>()
            .Where(ShapeIsVisible)
            .Select(shape => new OpenGlShape(gl!, shape, nif, ResolveTexture))
            .ToList();
    }

    internal void LoadSkydome(NifFile skydome, int rightPoint = 1)
    {
        Skydome = skydome;
        _reloadSkydome = true;

        var cameraIntro = skydome.FindBlockByName<NiControllerSequence>("CameraIntro");
        cameraIntro?.GotoTime(skydome, cameraIntro.StopTime);

        _cImageShaders.Clear();
        for (int i = 0; ; ++i)
        {
            var cImage = skydome.FindBlockByName<NiAVObject>($"cImage{i:00}") as INiShape;
            if (cImage is null)
                break;

            var shaderProperty = skydome.GetBlock<BSEffectShaderProperty>(cImage.ShaderPropertyRef);
            _cImageShaders.Add(shaderProperty);
        }

        NiNode? cameraPosition = skydome.FindBlockByName<NiNode>("CameraPosition");
        CameraPosition = cameraPosition?.Translation ?? Vector3.Zero;

        NiNode? pointF = skydome.FindBlockByName<NiNode>("Point00");
        NiNode? pointR = skydome.FindBlockByName<NiNode>($"Point{rightPoint:00}");
        if (pointF is not null && pointR is not null)
        {
            CameraUp =
                Vector3.Normalize(Vector3.Cross(
                    Vector3.Normalize(pointR.Translation),
                    Vector3.Normalize(pointF.Translation)));
        }
        else
        {
            CameraUp = Vector3.UnitZ;
        }

        SelectedIndex = 0;

        RequestNextFrameRendering();
    }

    internal void SetPerkStars(NifFile stars)
    {
        PerkStars = stars;
        _reloadStars = true;

        var selected = stars.FindBlockByName<NiControllerSequence>("Owned");
        selected?.GotoTime(stars, 0f);
    }

    internal void SetPerkLine(NifFile line)
    {
        PerkLine = line;
        _reloadLine = true;

        var unlocked = line.FindBlockByName<NiControllerSequence>("Unlocked");
        unlocked?.GotoTime(line, 0f);
    }

    internal void ShowPerks(int index, IActorValueInformationGetter actorValueInfo)
    {
        SelectedIndex = index;
        SelectedTree = new PerkTree(actorValueInfo);
        RequestNextFrameRendering();
    }

    public bool HitTest(Point point) => Bounds.Contains(point);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (draggingNode is null)
            return;

        var position = e.GetPosition(this);
        if (!Bounds.Contains(position))
            return;

        var (x, y) = PointToPerkCoordinates(position);
        if (float.IsNaN(x) || float.IsNaN(y) || y > PerkYMax || y < PerkYMin)
            return;

        draggingNode.X = x;
        draggingNode.Y = y;

        RequestNextFrameRendering();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (draggingNode is not null)
            return;

        var pointerPoint = e.GetCurrentPoint(this);
        if (pointerPoint.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        var p = pointerPoint.Position;
        var (x, y) = PointToPerkCoordinates(p);

        PerkTree.Node? nearestNode = FindNearestNode(x, y);

        if (nearestNode is null)
            return;

        var perkPosition = Vector3.Transform(GetPerkPosition(nearestNode), _cachedPointTransform.ToMatrix());
        var pixelDistance = Point.Distance(PositionToPoint(perkPosition), p);

        var cameraDistance = Vector3.Dot(perkPosition - CameraPosition, Vector3.Normalize(CameraLookAt));
        var invPixelSize = Bounds.Height / (Math.Tan(double.DegreesToRadians(FOV * 0.5)) * cameraDistance * 2.0);
        var tolerance = Math.Max(ClickToleranceBase * invPixelSize, ClickToleranceMin);

        if (pixelDistance < tolerance)
        {
            draggingNode = nearestNode;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonReleased)
            return;

        draggingNode = null;
    }

    private Vector3 GetPerkPosition(PerkTree.Node node)
        => new()
        {
            X = -node.X * StarXIncrement,
            Y = node.Y * StarYIncrement,
            Z = StarZInitialOffset - node.Y * StarZIncrement,
        };

    private Point PositionToPoint(Vector3 position)
    {
        var screenPos = Vector4.Transform(position, CameraView * CameraProjection);

        screenPos /= screenPos.W;

        return new Point(
            (0.5 + screenPos.X * 0.5) * Bounds.Width,
            (0.5 - screenPos.Y * 0.5) * Bounds.Height);
    }

    private (float x, float y) PointToPerkCoordinates(Point p)
    {
        var pointMatrix = _cachedPointTransform.ToMatrix();
        if (!Matrix4x4.Invert(pointMatrix, out Matrix4x4 pointInverse))
            return (float.NaN, float.NaN);

        Matrix4x4 view = CameraView;
        if (!Matrix4x4.Invert(view, out Matrix4x4 viewInverse))
            return (float.NaN, float.NaN);

        // Convert from framebuffer to screen space
        double screenX = p.X / Bounds.Width * 2.0 - 1.0;
        double screenY = p.Y / Bounds.Height * -2.0 + 1.0;
        double viewDimY = Math.Tan(double.DegreesToRadians(FOV * 0.5));
        double viewDimX = viewDimY * Bounds.Width / Bounds.Height;

        // Compute Plucker matrix
        Vector4 A = new Vector4(CameraPosition, 1f);
        Vector4 VB = new Vector4((float)(screenX * viewDimX), (float)(screenY * viewDimY), -1f, 0f);
        Vector4 B = Vector4.Transform(VB, viewInverse);

        Matrix4x4 Lx = new Matrix4x4(
            0f,
            A[0] * B[1] - B[0] * A[1],
            A[0] * B[2] - B[0] * A[2],
            A[0] * B[3] - B[0] * A[3],
            A[1] * B[0] - B[1] * A[0],
            0f,
            A[1] * B[2] - B[1] * A[2],
            A[1] * B[3] - B[1] * A[3],
            A[2] * B[0] - B[2] * A[0],
            A[2] * B[1] - B[2] * A[1],
            0f,
            A[2] * B[3] - B[2] * A[3],
            A[3] * B[0] - B[3] * A[0],
            A[3] * B[1] - B[3] * A[1],
            A[3] * B[2] - B[3] * A[2],
            0f);

        // Compute constellation plane in world coordinates
        Matrix4x4 pointInverseTranspose = Matrix4x4.Transpose(pointInverse);
        Vector4 E = Vector4.Transform(
            new Vector4(
                0f,
                1f / StarYIncrement,
                1f / StarZIncrement,
                -StarZInitialOffset / StarZIncrement),
            pointInverseTranspose);

        // Compute line-plane intersection
        Vector4 X = Vector4.Transform(E, Lx);
        X /= X.W;

        // Compute local translation of stats node
        Vector4 nodePosition = Vector4.Transform(X, pointInverse);

        float x = -nodePosition.X / StarXIncrement;
        float y = nodePosition.Y / StarYIncrement;
        return (x, y);
    }

    private PerkTree.Node? FindNearestNode(float x, float y)
    {
        PerkTree.Node? nearestNode = null;
        float nearestDistance = float.MaxValue;

        foreach (var (index, node) in SelectedTree?.Nodes ?? [])
        {
            if (index == 0)
                continue;

            float deltaX = node.X - x;
            float deltaY = node.Y - y;
            float distance = (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            if (distance < nearestDistance)
            {
                nearestNode = node;
                nearestDistance = distance;
            }
        }

        return nearestNode;
    }
}
