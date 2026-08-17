using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Runtime
{
    public enum MiniFaceExpression
    {
        Neutral,
        Tap,
        Happy,
        Eating,
        Greeting
    }

    /// <summary>
    /// Drives the installed mini-character eye, mouth, and eyebrow atlases.
    /// Mini faces use discrete UV cells rather than blend shapes, so all changes
    /// deliberately snap between valid cells.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MiniFaceExpressionController : MonoBehaviour
    {
        private const string EyeRendererName = "M_Eye";
        private const string MouthRendererName = "M_Mouth";
        private const string EyebrowLeftRendererName = "M_Mayu_L";
        private const string EyebrowRightRendererName = "M_Mayu_R";
        private const string CheekRendererName = "M_Cheek";
        private const string EyeUvProperty = "_UVOffset";

        // These cells are visually calibrated by the local smoke-face diagnostic.
        private static readonly FaceSelection NeutralFace =
            new FaceSelection(0, 0, 1, 1, 1);
        private static readonly FaceSelection BlinkFace =
            new FaceSelection(8, 8, 1, 1, 1);
        private static readonly FaceSelection TapFace =
            new FaceSelection(0, 0, 2, 1, 1);
        private static readonly FaceSelection HappyFace =
            new FaceSelection(9, 9, 2, 1, 1);
        private static readonly FaceSelection EatingFace =
            new FaceSelection(9, 9, 16, 1, 1);
        private static readonly FaceSelection GreetingFace =
            new FaceSelection(4, 4, 16, 1, 1);

        private Material _eye;
        private Material _mouth;
        private Material _eyebrowLeft;
        private Material _eyebrowRight;
        private Material _cheek;
        private Texture _baselineCheekTexture;
        private Texture2D _normalCheekTexture;
        private Texture2D _blushCheekTexture;
        private Vector4 _baselineEyeUv;
        private Vector2 _baselineMouthOffset;
        private Vector2 _baselineEyebrowLeftOffset;
        private Vector2 _baselineEyebrowRightOffset;
        private MiniFaceExpression _expression;
        private float _nextBlinkAt;
        private float _blinkEndsAt;
        private bool _blinking;
        private bool _diagnosticOverride;
        private bool _initialized;

        public MiniFaceExpression CurrentExpression
        {
            get { return _expression; }
        }

        public void Initialize(
            GameObject characterRoot,
            IEnumerable<Texture2D> faceTextures)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The mini face controller is already initialized.");
            }
            if (characterRoot == null)
            {
                throw new ArgumentNullException("characterRoot");
            }

            MeshRenderer[] renderers =
                characterRoot.GetComponentsInChildren<MeshRenderer>(true);
            _eye = FindMaterial(renderers, EyeRendererName);
            _mouth = FindMaterial(renderers, MouthRendererName);
            _eyebrowLeft = FindMaterial(renderers, EyebrowLeftRendererName);
            _eyebrowRight = FindMaterial(renderers, EyebrowRightRendererName);
            _cheek = FindMaterial(renderers, CheekRendererName);
            if (!_eye.HasProperty(EyeUvProperty))
            {
                throw new InvalidOperationException(
                    "The installed mini eye material has no " + EyeUvProperty + ".");
            }

            _baselineEyeUv = _eye.GetVector(EyeUvProperty);
            _baselineMouthOffset = _mouth.mainTextureOffset;
            _baselineEyebrowLeftOffset = _eyebrowLeft.mainTextureOffset;
            _baselineEyebrowRightOffset = _eyebrowRight.mainTextureOffset;
            _baselineCheekTexture = _cheek.mainTexture;
            FindCheekTextures(
                faceTextures,
                out _normalCheekTexture,
                out _blushCheekTexture);
            _initialized = true;
            Show(MiniFaceExpression.Neutral);
        }

        public void Show(MiniFaceExpression expression)
        {
            if (!_initialized || _diagnosticOverride)
            {
                return;
            }

            _expression = expression;
            _blinking = false;
            Apply(SelectionFor(expression));
            ApplyCheek(expression);
            ScheduleBlink();
            Debug.Log("Oguri face: " + expression + ".");
        }

        public bool TryApplyDiagnostic(string value, out string error)
        {
            error = null;
            if (!_initialized)
            {
                error = "The mini face controller is not initialized.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "Expected eyeL,eyeR,mouth,browL,browR.";
                return false;
            }

            string[] parts = value.Split(',');
            if (parts.Length != 5)
            {
                error = "Expected five comma-separated mini face indices.";
                return false;
            }

            int[] parsed = new int[5];
            for (int index = 0; index < parts.Length; index++)
            {
                if (!int.TryParse(
                    parts[index],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsed[index]))
                {
                    error = "Invalid mini face index: " + parts[index] + ".";
                    return false;
                }
            }

            var selection = new FaceSelection(
                parsed[0], parsed[1], parsed[2], parsed[3], parsed[4]);
            if (!selection.IsValid)
            {
                error = "Mini face indices are outside eyes 0..14, mouth 0..18, " +
                    "or eyebrows 0..8.";
                return false;
            }

            _diagnosticOverride = true;
            _blinking = false;
            Apply(selection);
            Debug.Log("Oguri diagnostic face: " + value + ".");
            return true;
        }

        private void Update()
        {
            if (!_initialized || _diagnosticOverride)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (_expression != MiniFaceExpression.Neutral)
            {
                return;
            }

            if (_blinking)
            {
                if (now >= _blinkEndsAt)
                {
                    _blinking = false;
                    Apply(NeutralFace);
                    ScheduleBlink();
                }
                return;
            }

            if (now >= _nextBlinkAt)
            {
                _blinking = true;
                _blinkEndsAt = now + 0.12f;
                Apply(BlinkFace);
            }
        }

        private void LateUpdate()
        {
            if (!_initialized || _diagnosticOverride ||
                _expression == MiniFaceExpression.Neutral)
            {
                return;
            }

            // Installed motion clips can contain material animation curves. Reapply
            // the selected reaction after playable evaluation so a body animation
            // cannot replace Oguri's face with an unrelated mouth atlas cell.
            Apply(SelectionFor(_expression));
            ApplyCheek(_expression);
        }

        private void OnDestroy()
        {
            if (!_initialized)
            {
                return;
            }

            _eye.SetVector(EyeUvProperty, _baselineEyeUv);
            _mouth.mainTextureOffset = _baselineMouthOffset;
            _eyebrowLeft.mainTextureOffset = _baselineEyebrowLeftOffset;
            _eyebrowRight.mainTextureOffset = _baselineEyebrowRightOffset;
            _cheek.mainTexture = _baselineCheekTexture;
        }

        private void ScheduleBlink()
        {
            _nextBlinkAt = Time.unscaledTime + UnityEngine.Random.Range(2.4f, 5.2f);
        }

        private void Apply(FaceSelection selection)
        {
            if (!selection.IsValid)
            {
                throw new ArgumentOutOfRangeException(
                    "selection",
                    "The mini face selection is outside its texture atlas.");
            }

            Vector2 rightEye = Encode(selection.EyeRight, 0.125f);
            Vector2 leftEye = Encode(selection.EyeLeft, 0.125f);
            _eye.SetVector(
                EyeUvProperty,
                new Vector4(rightEye.x, rightEye.y, leftEye.x, leftEye.y));
            _mouth.mainTextureOffset = Encode(selection.Mouth, 0.125f);
            _eyebrowLeft.mainTextureOffset = Encode(selection.EyebrowLeft, 0.25f);
            _eyebrowRight.mainTextureOffset = Encode(selection.EyebrowRight, 0.25f);
        }

        private void ApplyCheek(MiniFaceExpression expression)
        {
            bool blushing = expression == MiniFaceExpression.Happy ||
                expression == MiniFaceExpression.Eating;
            Texture2D selected = blushing
                ? _blushCheekTexture
                : _normalCheekTexture;
            _cheek.mainTexture = selected != null ? selected : _baselineCheekTexture;
        }

        private static void FindCheekTextures(
            IEnumerable<Texture2D> textures,
            out Texture2D normal,
            out Texture2D blush)
        {
            normal = null;
            blush = null;
            if (textures == null)
            {
                return;
            }

            foreach (Texture2D texture in textures)
            {
                if (texture == null)
                {
                    continue;
                }

                if (texture.name.IndexOf(
                    "cheek0",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    normal = texture;
                }
                else if (texture.name.IndexOf(
                    "cheek1",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    blush = texture;
                }
            }
        }

        private static FaceSelection SelectionFor(MiniFaceExpression expression)
        {
            switch (expression)
            {
                case MiniFaceExpression.Neutral:
                    return NeutralFace;
                case MiniFaceExpression.Tap:
                    return TapFace;
                case MiniFaceExpression.Happy:
                    return HappyFace;
                case MiniFaceExpression.Eating:
                    return EatingFace;
                case MiniFaceExpression.Greeting:
                    return GreetingFace;
                default:
                    throw new ArgumentOutOfRangeException("expression");
            }
        }

        private static Material FindMaterial(
            MeshRenderer[] renderers,
            string objectName)
        {
            for (int index = 0; index < renderers.Length; index++)
            {
                MeshRenderer renderer = renderers[index];
                if (renderer != null &&
                    string.Equals(renderer.name, objectName, StringComparison.Ordinal))
                {
                    Material material = renderer.material;
                    if (material != null)
                    {
                        return material;
                    }
                }
            }

            throw new InvalidOperationException(
                "The installed mini face renderer was not found: " + objectName + ".");
        }

        private static Vector2 Encode(int index, float rowStep)
        {
            return new Vector2(0.25f * (index % 4), -rowStep * (index / 4));
        }

        private struct FaceSelection
        {
            public FaceSelection(
                int eyeLeft,
                int eyeRight,
                int mouth,
                int eyebrowLeft,
                int eyebrowRight)
            {
                EyeLeft = eyeLeft;
                EyeRight = eyeRight;
                Mouth = mouth;
                EyebrowLeft = eyebrowLeft;
                EyebrowRight = eyebrowRight;
            }

            public int EyeLeft;
            public int EyeRight;
            public int Mouth;
            public int EyebrowLeft;
            public int EyebrowRight;

            public bool IsValid
            {
                get
                {
                    return EyeLeft >= 0 && EyeLeft <= 14 &&
                        EyeRight >= 0 && EyeRight <= 14 &&
                        Mouth >= 0 && Mouth <= 18 &&
                        EyebrowLeft >= 0 && EyebrowLeft <= 8 &&
                        EyebrowRight >= 0 && EyebrowRight <= 8;
                }
            }
        }
    }
}
