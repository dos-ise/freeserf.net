/*
 * ColorShader.cs - Basic color shader for colored shapes
 *
 * Copyright (C) 2018-2019  Robert Schneckenhaus <robert.schneckenhaus@web.de>
 *
 * This file is part of freeserf.net. freeserf.net is based on freeserf.
 *
 * freeserf.net is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * freeserf.net is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with freeserf.net. If not, see <http://www.gnu.org/licenses/>.
 */

namespace Freeserf.Renderer
{
    internal class ColorShader
    {
        static ColorShader colorShader = null;
        internal static readonly string DefaultFragmentOutColorName = "outColor";
        internal static readonly string DefaultPositionName = "position";
        internal static readonly string DefaultModelViewMatrixName = "mvMat";
        internal static readonly string DefaultProjectionMatrixName = "projMat";
        internal static readonly string DefaultColorName = "color";
        internal static readonly string DefaultZName = "z";
        internal static readonly string DefaultLayerName = "layer";

        internal ShaderProgram shaderProgram;
        readonly string fragmentOutColorName;
        readonly string modelViewMatrixName;
        readonly string projectionMatrixName;
        readonly string colorName;
        readonly string zName;
        readonly string positionName;
        readonly string layerName;

        // ---------------------------------------------------------------------
        // Unified GLSL / GLES helpers
        // ---------------------------------------------------------------------

        private static bool IsGLES()
        {
            return !string.IsNullOrEmpty(State.GLSLVersionSuffix) &&
                   State.GLSLVersionSuffix.ToLower().Contains("es");
        }

        private static bool IsLegacyGL()
        {
            // Desktop GLSL < 1.30
            return !IsGLES() && State.GLSLVersionMajor == 1 && State.GLSLVersionMinor < 3;
        }

        private static string GLSLVersionHeader()
        {
            if (IsGLES())
            {
                // GLES 2.0 → GLSL ES 1.00
                if (State.GLSLVersionMajor == 1 && State.GLSLVersionMinor == 0)
                {
                    return "#version 100\n" +
                           "precision mediump float;\n" +
                           "precision mediump int;\n\n";
                }

                // GLES 3.x → e.g. #version 300 es
                return $"#version {State.GLSLVersionMajor}{State.GLSLVersionMinor} es\n" +
                       "precision mediump float;\n" +
                       "precision mediump int;\n\n";
            }

            // Desktop GL: keep original style
            return $"#version {State.GLSLVersionMajor}{State.GLSLVersionMinor} {State.GLSLVersionSuffix}\n\n";
        }

        private static string InQualifier(bool fragment)
        {
            if (IsLegacyGL())
                return fragment ? "varying" : "attribute";

            return "in";
        }

        private static string OutQualifier()
        {
            if (IsLegacyGL())
                return "varying";

            return "out";
        }

        private static bool SupportsFlat()
        {
            if (IsLegacyGL())
                return false;

            if (IsGLES() && State.GLSLVersionMajor < 3)
                return false;

            return true;
        }

        private static bool SupportsIntegerAttributes()
        {
            if (IsLegacyGL())
                return false;

            if (IsGLES() && State.GLSLVersionMajor < 3)
                return false;

            return true;
        }

        private static bool UsesLegacyFragColor()
        {
            // Desktop GLSL < 1.30 or GLES 2.0 → gl_FragColor
            if (IsLegacyGL())
                return true;

            if (IsGLES() && State.GLSLVersionMajor < 3)
                return true;

            return false;
        }

        // ---------------------------------------------------------------------
        // Compatibility layer for old API (names used in this file)
        // ---------------------------------------------------------------------

        protected static bool HasGLFragColor()
        {
            return UsesLegacyFragColor();
        }

        protected static string GetFragmentShaderHeader()
        {
            string header = GLSLVersionHeader();

            if (!UsesLegacyFragColor())
                header += $"out vec4 {DefaultFragmentOutColorName};\n";

            return header;
        }

        protected static string GetVertexShaderHeader()
        {
            return GLSLVersionHeader();
        }

        protected static string GetInName(bool fragment)
        {
            return InQualifier(fragment);
        }

        protected static string GetOutName()
        {
            return OutQualifier();
        }

        // ---------------------------------------------------------------------
        // Unified shader generators (used via the arrays below)
        // ---------------------------------------------------------------------

        private static string GenerateVertexShader()
        {
            string header = GLSLVersionHeader();

            bool ints = SupportsIntegerAttributes();
            bool flat = SupportsFlat();

            string posType = ints ? "ivec2" : "vec2";
            string layerType = ints ? "uint" : "float";
            string colorType = ints ? "uvec4" : "vec4";

            string varying = flat ? "flat " + OutQualifier() : OutQualifier();

            string layerDecl = ints
                ? $"    float layer = float({DefaultLayerName});"
                : $"    float layer = {DefaultLayerName};";

            string posExpr = ints
                ? $"    vec2 pos = vec2(float({DefaultPositionName}.x) + 0.49, float({DefaultPositionName}.y) + 0.49);"
                : $"    vec2 pos = {DefaultPositionName} + vec2(0.49, 0.49);";

            string colorExpr = ints
                ? $"    pixelColor = vec4({DefaultColorName}.r / 255.0, {DefaultColorName}.g / 255.0, {DefaultColorName}.b / 255.0, {DefaultColorName}.a / 255.0);"
                : $"    pixelColor = {DefaultColorName} / 255.0;";

            return string.Join("\n", new[]
            {
                header,
                $"{InQualifier(false)} {posType} {DefaultPositionName};",
                $"{InQualifier(false)} {layerType} {DefaultLayerName};",
                $"{InQualifier(false)} {colorType} {DefaultColorName};",
                $"uniform float {DefaultZName};",
                $"uniform mat4 {DefaultProjectionMatrixName};",
                $"uniform mat4 {DefaultModelViewMatrixName};",
                $"{varying} vec4 pixelColor;",
                "",
                "void main()",
                "{",
                posExpr,
                colorExpr,
                layerDecl,
                $"    gl_Position = {DefaultProjectionMatrixName} * {DefaultModelViewMatrixName} * vec4(pos, 1.0 - {DefaultZName} - layer * 0.00001, 1.0);",
                "}"
            });
        }

        private static string GenerateFragmentShader()
        {
            string header = GLSLVersionHeader();

            bool flat = SupportsFlat();
            bool legacyFragColor = UsesLegacyFragColor();

            string varying = flat ? "flat " + InQualifier(true) : InQualifier(true);

            string outputDecl = legacyFragColor
                ? ""
                : $"out vec4 {DefaultFragmentOutColorName};\n";

            string outputAssign = legacyFragColor
                ? "gl_FragColor = pixelColor;"
                : $"{DefaultFragmentOutColorName} = pixelColor;";

            return string.Join("\n", new[]
            {
                header,
                outputDecl,
                $"{varying} vec4 pixelColor;",
                "",
                "void main()",
                "{",
                $"    {outputAssign}",
                "}"
            });
        }

        // ---------------------------------------------------------------------
        // Static shader arrays (kept for constructor compatibility)
        // ---------------------------------------------------------------------

        static readonly string[] ColorFragmentShader = new string[]
        {
            GenerateFragmentShader()
        };

        static readonly string[] ColorVertexShader = new string[]
        {
            GenerateVertexShader()
        };

        // ---------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------

        public void UpdateMatrices(bool zoom)
        {
            if (zoom)
                shaderProgram.SetInputMatrix(modelViewMatrixName, State.CurrentModelViewMatrix.ToArray(), true);
            else
                shaderProgram.SetInputMatrix(modelViewMatrixName, State.CurrentUnzoomedModelViewMatrix.ToArray(), true);

            shaderProgram.SetInputMatrix(projectionMatrixName, State.CurrentProjectionMatrix.ToArray(), true);
        }

        public void Use()
        {
            if (shaderProgram != ShaderProgram.ActiveProgram)
                shaderProgram.Use();
        }

        ColorShader()
            : this(DefaultModelViewMatrixName, DefaultProjectionMatrixName, DefaultColorName, DefaultZName,
                  DefaultPositionName, DefaultLayerName, ColorFragmentShader, ColorVertexShader)
        {
        }

        protected ColorShader(string modelViewMatrixName, string projectionMatrixName, string colorName, string zName,
            string positionName, string layerName, string[] fragmentShaderLines, string[] vertexShaderLines)
        {
            bool legacyFragColor = UsesLegacyFragColor();

            fragmentOutColorName = legacyFragColor ? "gl_FragColor" : DefaultFragmentOutColorName;

            this.modelViewMatrixName = modelViewMatrixName;
            this.projectionMatrixName = projectionMatrixName;
            this.colorName = colorName;
            this.zName = zName;
            this.positionName = positionName;
            this.layerName = layerName;

            var fragmentShader = new Shader(Shader.Type.Fragment, string.Join("\n", fragmentShaderLines));
            var vertexShader = new Shader(Shader.Type.Vertex, string.Join("\n", vertexShaderLines));

            shaderProgram = new ShaderProgram(fragmentShader, vertexShader);

            // Option A: always call this; for legacy/GLES2 use "gl_FragColor"
            shaderProgram.SetFragmentColorOutputName(fragmentOutColorName);
        }

        public ShaderProgram ShaderProgram => shaderProgram;

        public void SetZ(float z)
        {
            shaderProgram.SetInput(zName, z);
        }

        public static ColorShader Instance
        {
            get
            {
                if (colorShader == null)
                    colorShader = new ColorShader();

                return colorShader;
            }
        }
    }
}
