// SPDX-License-Identifier: GPL-3.0-or-later
using Silk.NET.OpenGL;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace PerkTreeEditor;

internal enum AttribLocation
{
    Position,
    TexCoord,
    Normal,
    Bitangent,
    Color,
}

internal readonly ref struct OpenGlProgramBinding : IDisposable
{
    private readonly GL gl;

    public OpenGlProgramBinding(GL gl, uint program)
    {
        this.gl = gl;
        gl.UseProgram(program);
    }

    public readonly void Dispose()
    {
        gl.UseProgram(0);
    }
}

internal class BSEffectShaderProgram
{
    public class UniformLocations(GL gl, uint program)
    {
        public int CameraViewProj { get; } = gl.GetUniformLocation(program, "CameraViewProj");
        public int TexcoordOffset { get; } = gl.GetUniformLocation(program, "TexcoordOffset");
        public int World { get; } = gl.GetUniformLocation(program, "World");
        public int BaseColor { get; } = gl.GetUniformLocation(program, "BaseColor");
        public int BaseColorScale { get; } = gl.GetUniformLocation(program, "BaseColorScale");
        public int LightingInfluence { get; } = gl.GetUniformLocation(program, "LightingInfluence");
        public int PropertyColor { get; } = gl.GetUniformLocation(program, "PropertyColor");
        public int BaseSampler { get; } = gl.GetUniformLocation(program, "BaseSampler");
        public int GrayscaleSampler { get; } = gl.GetUniformLocation(program, "GrayscaleSampler");
        public int VC { get; } = gl.GetUniformLocation(program, "VC");
        public int UseTexture { get; } = gl.GetUniformLocation(program, "UseTexture");
        public int GrayscaleToAlpha { get; } = gl.GetUniformLocation(program, "GrayscaleToAlpha");
        public int GrayscaleToColor { get; } = gl.GetUniformLocation(program, "GrayscaleToColor");
    }

    uint _vertexShader;
    uint _fragmentShader;
    uint _shaderProgram;

    public UniformLocations? Uniforms { get; private set; }

    static Stream GetResource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceStream(name)!;
    }

    public void Init(GL gl)
    {
        using (var stream = GetResource($"PerkTreeEditor.Shaders.BSEffect.vert"))
        {
            _vertexShader = gl.CreateShader(ShaderType.VertexShader);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            gl.ShaderSource(_vertexShader, reader.ReadToEnd());
            gl.CompileShader(_vertexShader);

            var infoLog = gl.GetShaderInfoLog(_vertexShader);
            if (infoLog.Length > 0)
            {
                Debug.WriteLine(infoLog);
            }
        }

        using (var stream = GetResource($"PerkTreeEditor.Shaders.BSEffect.frag"))
        {
            _fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            gl.ShaderSource(_fragmentShader, reader.ReadToEnd());
            gl.CompileShader(_fragmentShader);

            var infoLog = gl.GetShaderInfoLog(_fragmentShader);
            if (infoLog.Length > 0)
            {
                Debug.WriteLine(infoLog);
            }
        }

        {
            _shaderProgram = gl.CreateProgram();
            gl.AttachShader(_shaderProgram, _vertexShader);
            gl.AttachShader(_shaderProgram, _fragmentShader);
            gl.BindAttribLocation(_shaderProgram, (int)AttribLocation.Position, "aPosition");
            gl.BindAttribLocation(_shaderProgram, (int)AttribLocation.TexCoord, "aTexCoord0");
            gl.BindAttribLocation(_shaderProgram, (int)AttribLocation.Normal, "aNormal");
            gl.BindAttribLocation(_shaderProgram, (int)AttribLocation.Bitangent, "aBitangent");
            gl.BindAttribLocation(_shaderProgram, (int)AttribLocation.Color, "aColor");
            gl.LinkProgram(_shaderProgram);

            var infoLog = gl.GetProgramInfoLog(_shaderProgram);
            if (infoLog.Length > 0)
            {
                Debug.WriteLine(infoLog);
            }

            Uniforms = new(gl, _shaderProgram);
        }
    }

    public void Deinit(GL gl)
    {
        gl.DeleteProgram(_shaderProgram);
        _shaderProgram = 0;
        gl.DeleteShader(_fragmentShader);
        _fragmentShader = 0;
        gl.DeleteShader(_vertexShader);
        _vertexShader = 0;
    }

    public OpenGlProgramBinding Use(GL gl) => new(gl, _shaderProgram);
}
