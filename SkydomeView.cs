// SPDX-License-Identifier: GPL-3.0-or-later
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Mutagen.Bethesda.Skyrim;
using NiflySharp;
using NiflySharp.Blocks;
using NiflySharp.Structs;
using Silk.NET.OpenGL;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace PerkTreeEditor;

public class SkydomeView : OpenGlControlBase
{
    internal float NearClip { get; set; } = 1.0f;
    internal float FarClip { get; set; } = 20480.0f;
    internal float FOV { get; set; } = 50.0f;
    internal float LineScale { get; set; } = 0.1175f;
    internal Vector3 SkillsLookAt { get; set; } = new(0.0f, 0.0f, 5.0f);
    internal float StarXIncrement { get; set; } = 5.0f;
    internal float StarYIncrement { get; set; } = 15.0f;
    internal float StarZIncrement { get; set; } = -9.0f;
    internal float StarZInitialOffset { get; set; } = 5.0f;
    internal float StarScale { get; set; } = 0.15f;

    private int selectedIndex = 0;
    internal int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            selectedIndex = value;
            CameraLookAt =
                Skydome?.FindBlockByName<NiNode>($"Point{value:00}")?.Translation
                + SkillsLookAt
                ?? Vector3.Zero;

            var targetShader = _cImageShaders[selectedIndex];
            _cImageShaders.ForEach(shader =>
            {
                if (shader is not null)
                    shader.GetBaseColor().A = shader == targetShader ? 1.0f : 0.0f;
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

    private readonly BSEffectShaderProgram _effectShaderProgram = new();

    private readonly List<BSEffectShaderProperty?> _cImageShaders = [];
    private List<OpenGlShape> _skydomeShapes = [];
    private bool _reloadSkydome;

    private List<OpenGlShape> _starsShapes = [];
    private bool _reloadStars;

    private List<OpenGlShape> _lineShapes = [];
    private bool _reloadLine;

    private GL? gl;

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
        _skydomeShapes.ForEach(shape => shape.Deinit(gl!));
        _skydomeShapes.Clear();
        _starsShapes.ForEach(shape => shape.Deinit(gl!));
        _starsShapes.Clear();
        _lineShapes.ForEach(shape => shape.Deinit(gl!));
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
        gl.Enable(EnableCap.DepthTest);
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

        Matrix4x4 cameraView = Matrix4x4.CreateLookTo(CameraPosition, CameraLookAt, CameraUp);
        Matrix4x4 cameraProj =
            Matrix4x4.CreatePerspectiveFieldOfView(
                float.DegreesToRadians(FOV),
                (float)(Bounds.Width / Bounds.Height),
                NearClip,
                FarClip);

        Matrix4x4 cameraViewProj = cameraView * cameraProj;

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

        var point = Skydome?.FindBlockByName<NiNode>($"Point{selectedIndex:00}");
        if (SelectedTree is not null && point is not null)
        {
            var parentTransform = point.WorldTransform(Skydome!);
            var zaxis = Vector3.Normalize(-CameraLookAt);
            var xaxis = Vector3.Normalize(Vector3.Cross(CameraUp, zaxis));
            var yaxis = Vector3.Cross(zaxis, xaxis);
            var starsBillboard = new Matrix33
            {
                M11 = xaxis.X,
                M12 = xaxis.Y,
                M13 = xaxis.Z,
                M21 = yaxis.X,
                M22 = yaxis.Y,
                M23 = yaxis.Z,
                M31 = zaxis.X,
                M32 = zaxis.Y,
                M33 = zaxis.Z,
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
                    starsShape.Draw(gl, _effectShaderProgram.Uniforms, starsTransform, starsBillboard);
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
                            Scale = lineScale,
                        } * starsTransform;

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

    private Vector3 GetPerkPosition(PerkTree.Node node)
        => new()
        {
            X = -node.X * StarXIncrement,
            Y = node.Y * StarYIncrement,
            Z = StarZInitialOffset - node.Y * StarZIncrement,
        };

    private List<OpenGlShape> InitGl(NifFile nif)
    {
        static bool ShapeIsVisible(INiShape shape) =>
            !((NiAVObjectFlags)shape.Flags_ui).HasFlag(NiAVObjectFlags.Hidden);

        return
            nif.Blocks
            .OfType<BSTriShape>()
            .Where(ShapeIsVisible)
            .Select(shape => new OpenGlShape(gl!, shape, nif, ResolveTexture))
            .ToList();
    }

    internal void LoadSkydome(NifFile skydome, int rightPoint = 1)
    {
        Skydome = skydome;
        _reloadSkydome = true;
        FinishCameraIntro(skydome);

        _cImageShaders.Clear();
        for (int i = 0; ; ++i)
        {
            var cImage = skydome.FindBlockByName<BSTriShape>($"cImage{i:00}");
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
    }

    internal void SetPerkLine(NifFile line)
    {
        PerkLine = line;
        _reloadLine = true;
    }

    internal void ShowPerks(int index, IActorValueInformationGetter actorValueInfo)
    {
        SelectedIndex = index;
        SelectedTree = new PerkTree(actorValueInfo);
        RequestNextFrameRendering();
    }

    private static void FinishCameraIntro(NifFile skydome)
    {
        var cameraIntro = skydome.FindBlockByName<NiControllerSequence>("CameraIntro");
        if (cameraIntro is null)
            return;

        foreach (var block in cameraIntro.ControlledBlocks)
        {
            NiMultiTargetTransformController? controller =
                skydome.GetBlock<NiMultiTargetTransformController>(block.Controller);
            if (controller is null)
                continue;

            NiTransformInterpolator? interpolator =
                skydome.GetBlock<NiTransformInterpolator>(block.Interpolator);
            if (interpolator is null)
                continue;

            NiTransformData? data = skydome.GetBlock(interpolator.Data);
            if (data is null)
                continue;

            var obj = skydome.FindBlockByName<NiAVObject>(block.NodeName.String);
            if (obj is not null)
            {
                controller.UpdateTransform(obj, data, cameraIntro.StopTime);
            }
        }
    }
}
