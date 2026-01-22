using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;
using SyntheticDatasetGenerator;


#if UNITY_PIPELINE_URP
using UnityEngine.Rendering.Universal;
#elif UNITY_PIPELINE_HDRP
using UnityEngine.Rendering.HighDefinition;
#endif

namespace SyntheticDatasetGenerator
{
    #region === ENUMS ===
    public enum AnnotationFormat { YOLO, COCO, PascalVOC, KITTI, CreateML, TFRecord, All }
    public enum ImageFormat { JPG, PNG }
    public enum SplitMode { None, TrainValTest, TrainVal, KFold }
    public enum GeneratorMode { MultiObject, SingleObject360 }
    #endregion

    #region === DATA STRUCTURES ===
    [Serializable]
    public class DatasetConfig
    {
        [Header("Dataset Info")]
        public string datasetName = "SyntheticDataset";
        public string outputPath = "";

        [Header("Generator Mode")]
        public GeneratorMode generatorMode = GeneratorMode.MultiObject;

        [Header("Capture Settings")]
        public int totalImages = 1000;
        public int imageWidth = 1280;
        public int imageHeight = 720;
        public ImageFormat imageFormat = ImageFormat.JPG;
        [Range(1, 100)] public int jpgQuality = 95;

        [Header("Annotation Settings")]
        public AnnotationFormat annotationFormat = AnnotationFormat.YOLO;
        public bool saveVisualization = true;
        public bool includeOccluded = true;
        public bool includeTruncated = true;
        [Range(0f, 1f)] public float minVisibility = 0.1f;

        [Header("Segmentation")]
        public bool generateInstanceSegmentation = false;
        public bool generateSemanticSegmentation = false;
        public bool generatePanopticSegmentation = false;
        public bool generateBinaryMask = false;

        [Header("Depth Map")]
        public bool generateDepthMap = false;
        [Range(0.1f, 1000f)] public float depthMaxDistance = 100f;
        public bool depthLinear = true;
        public bool maskDepthToObjects = false;

        [Header("Split Settings")]
        public SplitMode splitMode = SplitMode.TrainValTest;
        [Range(0.5f, 0.9f)] public float trainRatio = 0.7f;
        [Range(0.05f, 0.3f)] public float valRatio = 0.2f;
        public int kFolds = 5;

        [Header("3D Settings (KITTI)")]
        public bool include3DInfo;

        public float TestRatio => 1f - trainRatio - valRatio;
    }

    [Serializable]
    public class SingleObject360Config
    {
        [Header("Object")]
        public GameObject targetObject;
        public int classId;
        public string className = "object";
        public Color boxColor = Color.red;

        [Header("Camera Orbit")]
        public float orbitDistance = 5f;
        public Vector3 lookAtOffset = Vector3.zero;

        [Header("Rotation Coverage")]
        [Range(1f, 90f)] public float angleStepYaw = 15f;
        [Range(1f, 90f)] public float angleStepPitch = 15f;
        [Range(-90f, 0f)] public float minPitch = -60f;
        [Range(0f, 90f)] public float maxPitch = 60f;

        [Header("Object Rotation")]
        public bool rotateObject;
        [Range(1f, 90f)] public float objectAngleStep = 15f;

        [Header("Scale Variation")]
        public bool randomizeScale;
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);

        public int CalculateTotalImages()
        {
            int yawSteps = Mathf.CeilToInt(360f / angleStepYaw);
            int pitchSteps = Mathf.CeilToInt((maxPitch - minPitch) / angleStepPitch) + 1;
            return yawSteps * pitchSteps;
        }
    }

    [Serializable]
    public class AugmentationConfig
    {
        [Header("Extra Augmentations")]
        public bool applyGaussianNoise;
        public bool applyMotionBlur;
        [Range(0f, 0.2f)] public float noiseSigma = 0.05f;
        [Range(1, 10)] public int blurRadius = 3;

        [Header("Randomize Lighting")]
        public bool randomizeLighting = true;
        [Range(0f, 2f)] public float intensityMin = 0.5f;
        [Range(0f, 4f)] public float intensityMax = 2f;
        public bool randomizeLightColor;
        public Color lightColorMin = new Color(0.9f, 0.9f, 0.9f);
        public Color lightColorMax = Color.white;
        public bool randomizeLightAngle;
        [Range(0f, 360f)] public float lightAngleRange = 90f;

        [Header("Randomize Depth of Field")]
        public bool randomizeDepthOfField;
        public Volume postProcessVolume;
        [Range(0.1f, 100f)] public float focusDistanceMin = 1f;
        [Range(0.1f, 100f)] public float focusDistanceMax = 20f;
        [Range(1f, 32f)] public float apertureMin = 1.4f;
        [Range(1f, 32f)] public float apertureMax = 16f;
        [Range(10f, 300f)] public float focalLengthMin = 20f;
        [Range(10f, 300f)] public float focalLengthMax = 85f;

        [Header("Randomize Chromatic Aberration")]
        public bool randomizeChromaticAberration;
        [Range(0f, 100f)] public float aberrationOffsetMin = 1f;
        [Range(0f, 100f)] public float aberrationOffsetMax = 10f;

        [Header("Randomize Color Grading")]
        public bool randomizeColorGrading;
        [Range(-180f, 180f)] public float hueShiftMin = -180f;
        [Range(-180f, 180f)] public float hueShiftMax = 180f;
        [Range(-100f, 100f)] public float saturationMin = -100f;
        [Range(-100f, 100f)] public float saturationMax = 100f;
        [Range(-100f, 100f)] public float contrastMin = -50f;
        [Range(-100f, 100f)] public float contrastMax = 50f;
        [Range(-5f, 5f)] public float exposureMin = -2f;
        [Range(-5f, 5f)] public float exposureMax = 2f;

        [Header("Randomize Background")]
        public bool randomizeBackground;
        public Color[] backgroundColors;
        public Material[] backgroundMaterials;
        public GameObject backgroundPlane;
    }

    [Serializable]
    public class ObjectClass
    {
        public string className = "object";
        public int classId;
        public Color boxColor = Color.red;
        public List<GameObject> prefabs = new List<GameObject>();
        [Range(0, 50)] public int minCount = 1;
        [Range(0, 50)] public int maxCount = 5;
        public string kittiType = "Car";
    }

    [Serializable]
    public class BoundingBoxData
    {
        public int classId;
        public string className;
        public string kittiType;
        public Color color;
        public Rect screenRect;
        public float xMin, yMin, xMax, yMax;
        public float centerX, centerY, normWidth, normHeight;
        public Vector3 worldPosition;
        public Vector3 dimensions;
        public float rotationY;
        public float alpha;
        public float truncation;
        public int occlusion;
        public float visibility;
        public GameObject sourceObject;
        public int instanceId;

        private static readonly StringBuilder _sb = new StringBuilder(256);

        public string ToYOLO()
        {
            _sb.Clear();
            _sb.Append(classId).Append(' ')
               .AppendFormat("{0:F6}", centerX).Append(' ')
               .AppendFormat("{0:F6}", centerY).Append(' ')
               .AppendFormat("{0:F6}", normWidth).Append(' ')
               .AppendFormat("{0:F6}", normHeight);
            return _sb.ToString();
        }

        public string ToKITTI()
        {
            _sb.Clear();
            _sb.Append(kittiType).Append(' ')
               .AppendFormat("{0:F2}", truncation).Append(' ')
               .Append(occlusion).Append(' ')
               .AppendFormat("{0:F2}", alpha).Append(' ')
               .AppendFormat("{0:F2}", xMin).Append(' ')
               .AppendFormat("{0:F2}", yMin).Append(' ')
               .AppendFormat("{0:F2}", xMax).Append(' ')
               .AppendFormat("{0:F2}", yMax).Append(' ')
               .AppendFormat("{0:F2}", dimensions.x).Append(' ')
               .AppendFormat("{0:F2}", dimensions.y).Append(' ')
               .AppendFormat("{0:F2}", dimensions.z).Append(' ')
               .AppendFormat("{0:F2}", worldPosition.x).Append(' ')
               .AppendFormat("{0:F2}", worldPosition.y).Append(' ')
               .AppendFormat("{0:F2}", worldPosition.z).Append(' ')
               .AppendFormat("{0:F2}", rotationY);
            return _sb.ToString();
        }

        public string ToCSV(string filename)
        {
            _sb.Clear();
            _sb.Append(filename).Append(',')
               .Append((int)xMin).Append(',')
               .Append((int)yMin).Append(',')
               .Append((int)xMax).Append(',')
               .Append((int)yMax).Append(',')
               .Append(className);
            return _sb.ToString();
        }
    }

    [Serializable] public class COCODataset { public COCOInfo info = new COCOInfo(); public List<COCOImage> images = new List<COCOImage>(); public List<COCOAnnotation> annotations = new List<COCOAnnotation>(); public List<COCOCategory> categories = new List<COCOCategory>(); }
    [Serializable] public class COCOInfo { public string description = "Synthetic Dataset"; public string version = "1.0"; public int year = DateTime.Now.Year; public string date_created = DateTime.Now.ToString("yyyy-MM-dd"); }
    [Serializable] public class COCOImage { public int id; public string file_name; public int width, height; }
    [Serializable] public class COCOAnnotation { public int id; public int image_id; public int category_id; public float[] bbox = new float[4]; public float area; public int iscrowd; }
    [Serializable] public class COCOCategory { public int id; public string name; public string supercategory = "object"; }
    [Serializable] public class CreateMLDataset { public List<CreateMLImage> images = new List<CreateMLImage>(); }
    [Serializable] public class CreateMLImage { public string image; public List<CreateMLAnnotation> annotations = new List<CreateMLAnnotation>(); }
    [Serializable] public class CreateMLAnnotation { public string label; public CreateMLCoordinates coordinates; }
    [Serializable] public class CreateMLCoordinates { public float x, y, width, height; }
    #endregion

    #region === GPU AUGMENTATION ===
    public class GPUAugmentation : IDisposable
    {
        private Material _material;
        private RenderTexture _tempRT1;
        private RenderTexture _tempRT2;

        private static readonly int _NoiseSigma = Shader.PropertyToID("_NoiseSigma");
        private static readonly int _Seed = Shader.PropertyToID("_Seed");
        private static readonly int _SeedOffset = Shader.PropertyToID("_SeedOffset");
        private static readonly int _BlurRadius = Shader.PropertyToID("_BlurRadius");
        private static readonly int _AberrationOffset = Shader.PropertyToID("_AberrationOffset");
        private static readonly int _HueShift = Shader.PropertyToID("_HueShift");
        private static readonly int _Saturation = Shader.PropertyToID("_Saturation");
        private static readonly int _Contrast = Shader.PropertyToID("_Contrast");
        private static readonly int _Exposure = Shader.PropertyToID("_Exposure");
        private static readonly int _ApplyNoise = Shader.PropertyToID("_ApplyNoise");
        private static readonly int _ApplyAberration = Shader.PropertyToID("_ApplyAberration");
        private static readonly int _ApplyColorGrading = Shader.PropertyToID("_ApplyColorGrading");

        private const int PASS_BLUR_H = 0;
        private const int PASS_BLUR_V = 1;
        private const int PASS_COMBINED = 2;

        public bool IsInitialized => _material != null;

        public bool Initialize()
        {
            Shader shader = Shader.Find("Hidden/SyntheticDatasetGenerator/ImageAugmentation");
            if (shader == null)
            {
                Debug.LogWarning("[GPUAugmentation] Shader not found. Using CPU fallback.");
                return false;
            }
            _material = new Material(shader);
            return true;
        }

        public void ApplyAll(RenderTexture source, RenderTexture destination, AugmentationConfig config)
        {
            if (_material == null) return;

            bool needsBlur = config.applyMotionBlur;
            bool needsCombined = config.applyGaussianNoise || config.randomizeChromaticAberration || config.randomizeColorGrading;

            if (!needsBlur && !needsCombined)
            {
                Graphics.Blit(source, destination);
                return;
            }

            EnsureTempRTs(source.width, source.height);
            RenderTexture current = source;

            if (needsBlur)
            {
                _material.SetInt(_BlurRadius, config.blurRadius);
                Graphics.Blit(current, _tempRT1, _material, PASS_BLUR_H);
                Graphics.Blit(_tempRT1, _tempRT2, _material, PASS_BLUR_V);
                current = _tempRT2;
            }

            if (needsCombined)
            {
                float hueShift = config.randomizeColorGrading ? Random.Range(config.hueShiftMin, config.hueShiftMax) : 0;
                float saturation = config.randomizeColorGrading ? Random.Range(config.saturationMin, config.saturationMax) : 0;
                float contrast = config.randomizeColorGrading ? Random.Range(config.contrastMin, config.contrastMax) : 0;
                float exposure = config.randomizeColorGrading ? Random.Range(config.exposureMin, config.exposureMax) : 0;
                float aberration = config.randomizeChromaticAberration ? Random.Range(config.aberrationOffsetMin, config.aberrationOffsetMax) : 0;

                _material.SetInt(_ApplyNoise, config.applyGaussianNoise ? 1 : 0);
                _material.SetFloat(_NoiseSigma, config.noiseSigma);
                _material.SetFloat(_Seed, Random.Range(0f, 10000f));
                _material.SetFloat(_SeedOffset, Random.Range(0f, 10000f));
                _material.SetInt(_ApplyAberration, config.randomizeChromaticAberration ? 1 : 0);
                _material.SetFloat(_AberrationOffset, aberration);
                _material.SetInt(_ApplyColorGrading, config.randomizeColorGrading ? 1 : 0);
                _material.SetFloat(_HueShift, hueShift / 360f);
                _material.SetFloat(_Saturation, saturation / 100f);
                _material.SetFloat(_Contrast, contrast / 100f);
                _material.SetFloat(_Exposure, exposure);

                Graphics.Blit(current, destination, _material, PASS_COMBINED);
            }
            else
            {
                Graphics.Blit(current, destination);
            }
        }

        private void EnsureTempRTs(int width, int height)
        {
            if (_tempRT1 == null || _tempRT1.width != width || _tempRT1.height != height)
            {
                if (_tempRT1 != null) _tempRT1.Release();
                _tempRT1 = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                _tempRT1.Create();
            }
            if (_tempRT2 == null || _tempRT2.width != width || _tempRT2.height != height)
            {
                if (_tempRT2 != null) _tempRT2.Release();
                _tempRT2 = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                _tempRT2.Create();
            }
        }

        public void Dispose()
        {
            if (_material != null) { UnityEngine.Object.DestroyImmediate(_material); _material = null; }
            if (_tempRT1 != null) { _tempRT1.Release(); _tempRT1 = null; }
            if (_tempRT2 != null) { _tempRT2.Release(); _tempRT2 = null; }
        }
    }
    #endregion

    #region === SEGMENTATION RENDERER ===
    public class SegmentationRenderer : IDisposable
    {
        private Material _instanceMaterial;
        private RenderTexture _renderRT;
        private Texture2D _outputTex;
        private int _instanceCounter = 1;

        private Dictionary<int, Color> _classColors = new Dictionary<int, Color>();

        private static readonly int _InstanceColor = Shader.PropertyToID("_InstanceColor");

        public bool IsInitialized { get; private set; }

        public bool Initialize(int width, int height)
        {
            Shader instanceShader = Shader.Find("Hidden/SyntheticDatasetGenerator/InstanceColor");

            if (instanceShader == null)
            {
                Debug.LogError("[SegmentationRenderer] InstanceColor shader not found!");
                return false;
            }

            _instanceMaterial = new Material(instanceShader);
            _renderRT = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            _renderRT.Create();
            _outputTex = new Texture2D(width, height, TextureFormat.RGB24, false);

            IsInitialized = true;
            Debug.Log("[SegmentationRenderer] Initialized successfully");
            return true;
        }

        public void ResetInstanceCounter()
        {
            _instanceCounter = 1;
        }

        public int AssignInstanceId()
        {
            return _instanceCounter++;
        }

        public void InitializeClassColors(List<ObjectClass> classes)
        {
            _classColors.Clear();

            // Generate consistent colors for each class using golden ratio
            for (int i = 0; i < classes.Count; i++)
            {
                int classId = classes[i].classId;
                float hue = (classId * 0.618033988749895f) % 1f;
                _classColors[classId] = Color.HSVToRGB(hue, 0.8f, 0.9f);
            }
        }

        private Color EncodeInstanceID(int instanceId)
        {
            // Unique color per instance
            float r = ((instanceId * 73) % 256) / 255f;
            float g = ((instanceId * 151) % 256) / 255f;
            float b = ((instanceId * 223) % 256) / 255f;
            return new Color(r, g, b, 1f);
        }

        private Color GetClassColor(int classId)
        {
            if (_classColors.TryGetValue(classId, out Color color))
                return color;

            // Fallback if class not initialized
            float hue = (classId * 0.618033988749895f) % 1f;
            return Color.HSVToRGB(hue, 0.8f, 0.9f);
        }

        private Color GetPanopticColor(int instanceId, int classId)
        {
            // Base color from class
            Color classColor = GetClassColor(classId);

            // Add subtle instance variation (brightness shift)
            float variation = ((instanceId * 0.377f) % 1f) * 0.2f - 0.1f;

            Color.RGBToHSV(classColor, out float h, out float s, out float v);
            v = Mathf.Clamp01(v + variation);

            return Color.HSVToRGB(h, s, v);
        }

        public void RenderInstanceSegmentation(Camera camera, List<GameObject> objects, Dictionary<GameObject, DatasetObjectLabel> labels, string outputPath)
        {
            RenderSegmentation(camera, objects, labels, outputPath, SegmentationType.Instance);
        }

        public void RenderSemanticSegmentation(Camera camera, List<GameObject> objects, Dictionary<GameObject, DatasetObjectLabel> labels, string outputPath)
        {
            RenderSegmentation(camera, objects, labels, outputPath, SegmentationType.Semantic);
        }

        public void RenderPanopticSegmentation(Camera camera, List<GameObject> objects, Dictionary<GameObject, DatasetObjectLabel> labels, string outputPath)
        {
            RenderSegmentation(camera, objects, labels, outputPath, SegmentationType.Panoptic);
        }

        public void RenderBinaryMask(Camera camera, List<GameObject> objects, Dictionary<GameObject, DatasetObjectLabel> labels, string outputPath)
        {
            RenderSegmentation(camera, objects, labels, outputPath, SegmentationType.BinaryMask);
        }

        private enum SegmentationType { Instance, Semantic, Panoptic, BinaryMask }

        private void RenderSegmentation(Camera camera, List<GameObject> objects, Dictionary<GameObject, DatasetObjectLabel> labels, string outputPath, SegmentationType type)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[SegmentationRenderer] Not initialized!");
                return;
            }

            Debug.Log($"[SegmentationRenderer] === START {type} Segmentation for {objects.Count} objects ===");

            // Activate render texture
            RenderTexture.active = _renderRT;
            GL.Clear(true, true, Color.black);

            // Setup camera matrices
            GL.PushMatrix();
            GL.LoadProjectionMatrix(camera.projectionMatrix);
            GL.modelview = camera.worldToCameraMatrix;

            int drawnCount = 0;
            foreach (var obj in objects)
            {
                if (obj == null) continue;

                DatasetObjectLabel label;
                if (!labels.TryGetValue(obj, out label))
                {
                    label = obj.GetComponent<DatasetObjectLabel>();
                    if (label == null) continue;
                }

                // Determine color based on segmentation type
                Color segColor;
                switch (type)
                {
                    case SegmentationType.Instance:
                        segColor = EncodeInstanceID(label.instanceId);
                        break;
                    case SegmentationType.Semantic:
                        segColor = GetClassColor(label.classId);
                        break;
                    case SegmentationType.Panoptic:
                        segColor = GetPanopticColor(label.instanceId, label.classId);
                        break;
                    case SegmentationType.BinaryMask:
                        segColor = Color.white; // All objects = white
                        break;
                    default:
                        segColor = Color.magenta;
                        break;
                }

                _instanceMaterial.SetColor(_InstanceColor, segColor);

                // Get all mesh filters
                var meshFilters = obj.GetComponentsInChildren<MeshFilter>();

                foreach (var mf in meshFilters)
                {
                    if (mf == null || mf.sharedMesh == null) continue;

                    Renderer renderer = mf.GetComponent<Renderer>();
                    if (renderer == null || !renderer.enabled) continue;

                    _instanceMaterial.SetPass(0);
                    Graphics.DrawMeshNow(mf.sharedMesh, renderer.transform.localToWorldMatrix);
                    drawnCount++;
                }
            }

            GL.PopMatrix();
            RenderTexture.active = null;

            Debug.Log($"[SegmentationRenderer] Drew {drawnCount} meshes");

            // Save
            SaveRenderTexture(_renderRT, outputPath);
            Debug.Log($"[SegmentationRenderer] === END {type} Saved to {outputPath} ===");
        }

        private void SaveRenderTexture(RenderTexture rt, string path)
        {
            RenderTexture.active = rt;
            _outputTex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            _outputTex.Apply();
            RenderTexture.active = null;

            File.WriteAllBytes(path, _outputTex.EncodeToPNG());
        }

        public void Dispose()
        {
            if (_instanceMaterial != null) UnityEngine.Object.DestroyImmediate(_instanceMaterial);
            if (_renderRT != null) { _renderRT.Release(); _renderRT = null; }
            if (_outputTex != null) UnityEngine.Object.DestroyImmediate(_outputTex);
        }
    }
    #endregion

    #region === DEPTH MAP RENDERER ===
    public class DepthMapRenderer : IDisposable
    {
        private Material _depthMaterial;
        private RenderTexture _tempRT;
        private Texture2D _outputTex;

        private static readonly int _MaxDistance = Shader.PropertyToID("_MaxDistance");

        public bool IsInitialized { get; private set; }

        public bool Initialize(int width, int height)
        {
            Shader depthShader = Shader.Find("Hidden/SyntheticDatasetGenerator/DepthFromCamera");

            if (depthShader == null)
            {
                Debug.LogError("[DepthMapRenderer] DepthFromCamera shader not found!");
                return false;
            }

            _depthMaterial = new Material(depthShader);
            _tempRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _tempRT.Create();
            _outputTex = new Texture2D(width, height, TextureFormat.RGB24, false);

            IsInitialized = true;
            Debug.Log("[DepthMapRenderer] Initialized successfully");
            return true;
        }

        public void RenderDepthMap(Camera camera, float manualMaxDistance, bool linearDepth, string outputPath, List<GameObject> spawnedObjects, bool maskToObjects = false)
        {
            if (!IsInitialized)
            {
                Debug.LogError("[DepthMapRenderer] Not initialized!");
                return;
            }

            Debug.Log($"[DepthMapRenderer] === RenderDepthMap START (Masked: {maskToObjects}) ===");
            Debug.Log($"[DepthMapRenderer] Spawned objects count: {(spawnedObjects != null ? spawnedObjects.Count : 0)}");

            // Calculate optimal max distance based on ONLY spawned objects
            float actualMaxDistance;
            if (spawnedObjects != null && spawnedObjects.Count > 0)
            {
                actualMaxDistance = CalculateOptimalMaxDistance(camera, spawnedObjects);
                Debug.Log($"[DepthMapRenderer] Using AUTO max distance: {actualMaxDistance:F2}");
            }
            else
            {
                actualMaxDistance = manualMaxDistance;
                Debug.LogWarning($"[DepthMapRenderer] Using MANUAL max distance: {manualMaxDistance:F2} (no spawned objects)");
            }

            // Enable depth texture on the camera
            DepthTextureMode originalDepthMode = camera.depthTextureMode;
            camera.depthTextureMode = DepthTextureMode.Depth;

            // Set shader parameters
            _depthMaterial.SetFloat(_MaxDistance, actualMaxDistance);

            // Blit the depth texture through our visualization shader
            Graphics.Blit(null, _tempRT, _depthMaterial);

            // Read the result
            RenderTexture.active = _tempRT;
            _outputTex.ReadPixels(new Rect(0, 0, _tempRT.width, _tempRT.height), 0, 0);
            _outputTex.Apply();
            RenderTexture.active = null;

            // Apply masking if requested
            if (maskToObjects && spawnedObjects != null && spawnedObjects.Count > 0)
            {
                ApplyObjectMask(_outputTex, camera, spawnedObjects);
            }

            // Restore camera depth mode
            camera.depthTextureMode = originalDepthMode;

            // Save
            File.WriteAllBytes(outputPath, _outputTex.EncodeToPNG());
            Debug.Log($"[DepthMapRenderer] Saved depth map (masked: {maskToObjects}) with max distance: {actualMaxDistance:F2}");
            Debug.Log($"[DepthMapRenderer] === RenderDepthMap END ===");
        }

        private void ApplyObjectMask(Texture2D depthTex, Camera camera, List<GameObject> objects)
        {
            Debug.Log("[DepthMapRenderer] Applying object mask to depth map");

            // Create a simple binary mask
            Color[] depthPixels = depthTex.GetPixels();
            bool[] objectMask = new bool[depthPixels.Length];

            // Create mask by rendering objects
            RenderTexture maskRT = RenderTexture.GetTemporary(depthTex.width, depthTex.height, 24, RenderTextureFormat.ARGB32);

            RenderTexture.active = maskRT;
            GL.Clear(true, true, Color.black);

            GL.PushMatrix();
            GL.LoadProjectionMatrix(camera.projectionMatrix);
            GL.modelview = camera.worldToCameraMatrix;

            // Create a simple white material for mask rendering
            Shader unlitShader = Shader.Find("Hidden/SyntheticDatasetGenerator/InstanceColor");
            if (unlitShader == null)
            {
                Debug.LogError("[DepthMapRenderer] Could not find shader for masking!");
                GL.PopMatrix();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(maskRT);
                return;
            }

            Material whiteMat = new Material(unlitShader);
            whiteMat.SetColor("_InstanceColor", Color.white);

            // Draw all spawned objects in white
            foreach (var obj in objects)
            {
                if (obj == null) continue;

                var meshFilters = obj.GetComponentsInChildren<MeshFilter>();
                foreach (var mf in meshFilters)
                {
                    if (mf == null || mf.sharedMesh == null) continue;

                    Renderer renderer = mf.GetComponent<Renderer>();
                    if (renderer == null || !renderer.enabled) continue;

                    whiteMat.SetPass(0);
                    Graphics.DrawMeshNow(mf.sharedMesh, renderer.transform.localToWorldMatrix);
                }
            }

            GL.PopMatrix();

            // Read the mask
            Texture2D maskTex = new Texture2D(depthTex.width, depthTex.height, TextureFormat.RGB24, false);
            maskTex.ReadPixels(new Rect(0, 0, maskRT.width, maskRT.height), 0, 0);
            maskTex.Apply();
            RenderTexture.active = null;

            // Get mask pixels
            Color[] maskPixels = maskTex.GetPixels();

            // Apply mask
            for (int i = 0; i < depthPixels.Length; i++)
            {
                // If mask pixel is dark (no object), set depth to pure black
                if (maskPixels[i].r < 0.1f)
                {
                    depthPixels[i] = Color.black;
                }
            }

            // Update the depth texture
            depthTex.SetPixels(depthPixels);
            depthTex.Apply();

            // Cleanup
            RenderTexture.ReleaseTemporary(maskRT);
            UnityEngine.Object.DestroyImmediate(maskTex);
            UnityEngine.Object.DestroyImmediate(whiteMat);

            Debug.Log("[DepthMapRenderer] Object mask applied successfully");
        }

        private float CalculateOptimalMaxDistance(Camera camera, List<GameObject> spawnedObjects)
        {
            if (spawnedObjects == null || spawnedObjects.Count == 0)
                return 100f;

            Vector3 cameraPos = camera.transform.position;
            float minDist = float.MaxValue;
            float maxDist = 0f;

            // ONLY loop through spawned objects
            foreach (var obj in spawnedObjects)
            {
                if (obj == null) continue;

                Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;

                    Bounds bounds = renderer.bounds;

                    // Calculate distance to object center
                    float centerDist = Vector3.Distance(cameraPos, bounds.center);
                    float extents = bounds.extents.magnitude;

                    float nearDist = centerDist - extents;
                    float farDist = centerDist + extents;

                    if (nearDist < minDist) minDist = Mathf.Max(0.1f, nearDist);
                    if (farDist > maxDist) maxDist = farDist;
                }
            }

            if (maxDist > 0 && minDist < float.MaxValue)
            {
                float optimalMax = maxDist * 1.05f;
                Debug.Log($"[DepthMapRenderer] Object depth range: {minDist:F2} to {maxDist:F2}, using max: {optimalMax:F2}");
                return optimalMax;
            }

            Debug.LogWarning("[DepthMapRenderer] No valid objects found, using default max distance");
            return 100f;
        }

        public void Dispose()
        {
            if (_depthMaterial != null) UnityEngine.Object.DestroyImmediate(_depthMaterial);
            if (_tempRT != null) { _tempRT.Release(); _tempRT = null; }
            if (_outputTex != null) UnityEngine.Object.DestroyImmediate(_outputTex);
        }
    }
    #endregion

    #region === MAIN GENERATOR ===
    public class MultiFormatDatasetGenerator : MonoBehaviour
    {
        [Header("Configuration")]
        public DatasetConfig config = new DatasetConfig();
        public List<ObjectClass> classes = new List<ObjectClass>();
        public AugmentationConfig augmentation = new AugmentationConfig();
        public SingleObject360Config singleObjectConfig = new SingleObject360Config();

        [Header("Scene Setup")]
        public Camera captureCamera;
        public Light mainLight;
        public Vector3 spawnAreaCenter = Vector3.zero;
        public Vector3 spawnAreaSize = new Vector3(10, 0, 10);
        public float minObjectSeparation = 1.5f;
        public bool randomizeRotation = true;
        public bool randomizeScale = true;
        public Vector2 scaleRange = new Vector2(0.8f, 1.2f);

        [Header("Runtime")]
        public bool showPreview = true;
        public bool isGenerating;
        public int currentImageIndex;

        public event Action<string> OnComplete;
        public event Action<int, int> OnProgress;

        private string _outputPath;
        private readonly List<BoundingBoxData> _currentBBoxes = new List<BoundingBoxData>(64);
        private readonly List<GameObject> _spawnedObjects = new List<GameObject>(64);
        private Dictionary<GameObject, DatasetObjectLabel> _objectLabels = new Dictionary<GameObject, DatasetObjectLabel>();
        private RenderTexture _captureRT;
        private RenderTexture _augmentedRT;
        private Texture2D _captureTex, _vizTex;
        private readonly List<string> _trainImages = new List<string>(512);
        private readonly List<string> _valImages = new List<string>(256);
        private readonly List<string> _testImages = new List<string>(128);
        private COCODataset _cocoDataset;
        private CreateMLDataset _createMLDataset;
        private List<string> _tfRecordCSV;
        private int _annotationId = 1;
        private DepthMapRenderer _depthRenderer;

        private Vector3 _originalCameraPos;
        private Quaternion _originalCameraRot;
        private float _originalLightIntensity;
        private Color _originalLightColor;
        private Quaternion _originalLightRot;

#if UNITY_PIPELINE_HDRP
        private HDAdditionalLightData _hdLightData;
        private float _originalHDRPIntensity;
#endif

#if UNITY_PIPELINE_URP
        private UnityEngine.Rendering.Universal.DepthOfField _dofOverride;
#elif UNITY_PIPELINE_HDRP
        private UnityEngine.Rendering.HighDefinition.DepthOfField _dofOverride;
#endif
        private bool _dofInitialized;

        private GPUAugmentation _gpuAugmentation;
        private bool _useGPUAugmentation;

        private SegmentationRenderer _segmentationRenderer;

        private readonly Vector3[] _boundsCorners = new Vector3[8];
        private Color[] _pixelBuffer;
        private Color[] _tempBuffer;
        private readonly StringBuilder _stringBuilder = new StringBuilder(4096);
        private WaitForEndOfFrame _waitForEndOfFrame;

        private static readonly Dictionary<char, byte[]> Font = new Dictionary<char, byte[]>
        {
            {'A', new byte[]{0x7C,0x82,0x82,0xFE,0x82,0x82,0x82}}, {'B', new byte[]{0xFC,0x82,0xFC,0x82,0x82,0x82,0xFC}},
            {'C', new byte[]{0x7E,0x80,0x80,0x80,0x80,0x80,0x7E}}, {'D', new byte[]{0xFC,0x82,0x82,0x82,0x82,0x82,0xFC}},
            {'E', new byte[]{0xFE,0x80,0xFC,0x80,0x80,0x80,0xFE}}, {'F', new byte[]{0xFE,0x80,0xFC,0x80,0x80,0x80,0x80}},
            {'G', new byte[]{0x7E,0x80,0x80,0x9E,0x82,0x82,0x7E}}, {'H', new byte[]{0x82,0x82,0xFE,0x82,0x82,0x82,0x82}},
            {'I', new byte[]{0xFE,0x10,0x10,0x10,0x10,0x10,0xFE}}, {'J', new byte[]{0x7E,0x08,0x08,0x08,0x08,0x88,0x70}},
            {'K', new byte[]{0x84,0x88,0xF0,0x88,0x84,0x82,0x82}}, {'L', new byte[]{0x80,0x80,0x80,0x80,0x80,0x80,0xFE}},
            {'M', new byte[]{0x82,0xC6,0xAA,0x92,0x82,0x82,0x82}}, {'N', new byte[]{0x82,0xC2,0xA2,0x92,0x8A,0x86,0x82}},
            {'O', new byte[]{0x7C,0x82,0x82,0x82,0x82,0x82,0x7C}}, {'P', new byte[]{0xFC,0x82,0x82,0xFC,0x80,0x80,0x80}},
            {'Q', new byte[]{0x7C,0x82,0x82,0x82,0x8A,0x84,0x7A}}, {'R', new byte[]{0xFC,0x82,0x82,0xFC,0x88,0x84,0x82}},
            {'S', new byte[]{0x7E,0x80,0x7C,0x02,0x02,0x82,0x7C}}, {'T', new byte[]{0xFE,0x10,0x10,0x10,0x10,0x10,0x10}},
            {'U', new byte[]{0x82,0x82,0x82,0x82,0x82,0x82,0x7C}}, {'V', new byte[]{0x82,0x82,0x82,0x44,0x44,0x28,0x10}},
            {'W', new byte[]{0x82,0x82,0x82,0x92,0xAA,0xC6,0x82}}, {'X', new byte[]{0x82,0x44,0x28,0x10,0x28,0x44,0x82}},
            {'Y', new byte[]{0x82,0x44,0x28,0x10,0x10,0x10,0x10}}, {'Z', new byte[]{0xFE,0x04,0x08,0x10,0x20,0x40,0xFE}},
            {'0', new byte[]{0x7C,0x86,0x8A,0x92,0xA2,0xC2,0x7C}}, {'1', new byte[]{0x10,0x30,0x10,0x10,0x10,0x10,0x38}},
            {'2', new byte[]{0x7C,0x82,0x04,0x08,0x30,0x40,0xFE}}, {'3', new byte[]{0xFE,0x04,0x08,0x1C,0x02,0x82,0x7C}},
            {'4', new byte[]{0x08,0x18,0x28,0x48,0xFE,0x08,0x08}}, {'5', new byte[]{0xFE,0x80,0xFC,0x02,0x02,0x82,0x7C}},
            {'6', new byte[]{0x3C,0x40,0x80,0xFC,0x82,0x82,0x7C}}, {'7', new byte[]{0xFE,0x02,0x04,0x08,0x10,0x20,0x20}},
            {'8', new byte[]{0x7C,0x82,0x82,0x7C,0x82,0x82,0x7C}}, {'9', new byte[]{0x7C,0x82,0x82,0x7E,0x02,0x04,0x78}},
            {'_', new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0xFE}}, {'-', new byte[]{0x00,0x00,0x00,0x7C,0x00,0x00,0x00}},
            {' ', new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x00}}, {'.', new byte[]{0x00,0x00,0x00,0x00,0x00,0x00,0x18}},
        };

        #region === INITIALIZATION ===
        void Start()
        {
            if (!captureCamera) captureCamera = Camera.main;
            if (!mainLight) mainLight = FindAnyObjectByType<Light>();
            if (string.IsNullOrEmpty(config.outputPath))
                config.outputPath = Path.Combine(Application.persistentDataPath, "Datasets");

            _waitForEndOfFrame = new WaitForEndOfFrame();

            _gpuAugmentation = new GPUAugmentation();
            _useGPUAugmentation = _gpuAugmentation.Initialize();

            if (_useGPUAugmentation)
                Debug.Log("[Dataset Generator] GPU augmentation initialized.");
            else
                Debug.Log("[Dataset Generator] Using CPU augmentation fallback.");

            if (augmentation.randomizeDepthOfField)
                InitializeDOF();

            bool needsSegmentation = config.generateInstanceSegmentation ||
                                   config.generateSemanticSegmentation ||
                                   config.generatePanopticSegmentation ||
                                   config.generateBinaryMask;
            if (needsSegmentation)
                InitializeSegmentation();

            if (config.generateDepthMap)
                InitializeDepthMap();

#if UNITY_PIPELINE_HDRP
    if (mainLight != null)
    {
        _hdLightData = mainLight.GetComponent<HDAdditionalLightData>();
        if (_hdLightData == null)
            Debug.LogWarning("[Dataset Generator] HDAdditionalLightData not found on main light.");
    }
#endif
        }

        void InitializeSegmentation()
        {
            _segmentationRenderer = new SegmentationRenderer();
            if (_segmentationRenderer.Initialize(config.imageWidth, config.imageHeight))
            {
                Debug.Log("[Dataset Generator] Segmentation renderer initialized.");

                // Initialize class colors for semantic/panoptic
                if (config.generatorMode == GeneratorMode.MultiObject)
                    _segmentationRenderer.InitializeClassColors(classes);
            }
            else
                Debug.LogError("[Dataset Generator] Failed to initialize segmentation renderer!");
        }

        void InitializeDepthMap()
        {
            _depthRenderer = new DepthMapRenderer();
            if (_depthRenderer.Initialize(config.imageWidth, config.imageHeight))
                Debug.Log("[Dataset Generator] Depth map renderer initialized.");
            else
                Debug.LogError("[Dataset Generator] Failed to initialize depth renderer!");
        }

        void InitializeDOF()
        {
#if UNITY_PIPELINE_URP || UNITY_PIPELINE_HDRP
            if (augmentation.postProcessVolume != null && augmentation.postProcessVolume.profile != null)
            {
                if (augmentation.postProcessVolume.profile.TryGet(out _dofOverride))
                {
                    _dofInitialized = true;
                    Debug.Log("[Dataset Generator] DOF override found and cached.");
                }
                else
                    Debug.LogWarning("[Dataset Generator] Volume Profile has no Depth of Field override!");
            }
            else
                Debug.LogWarning("[Dataset Generator] Post Process Volume not assigned or has no profile!");
#else
            Debug.LogWarning("[Dataset Generator] DOF requires URP or HDRP.");
#endif
        }

        public void StartGeneration()
        {
            if (isGenerating) return;

            if (augmentation.randomizeDepthOfField && !_dofInitialized)
                InitializeDOF();

            bool needsSegmentation = config.generateInstanceSegmentation ||
                                   config.generateSemanticSegmentation ||
                                   config.generatePanopticSegmentation ||
                                   config.generateBinaryMask;

            if (needsSegmentation && (_segmentationRenderer == null || !_segmentationRenderer.IsInitialized))
                InitializeSegmentation();

            if (config.generateDepthMap && (_depthRenderer == null || !_depthRenderer.IsInitialized))
                InitializeDepthMap();

            StartCoroutine(config.generatorMode == GeneratorMode.SingleObject360
                ? Generate360Coroutine()
                : GenerateMultiObjectCoroutine());
        }

        public void StopGeneration()
        {
            isGenerating = false;
            StopAllCoroutines();
            ResetDOF();
        }
        #endregion

        #region === MULTI-OBJECT GENERATION ===
        IEnumerator GenerateMultiObjectCoroutine()
        {
            isGenerating = true;
            currentImageIndex = 0;
            InitializeOutput();
            InitializeTextures();
            InitializeAnnotations();
            SaveOriginalSettings();
            yield return null;

            while (currentImageIndex < config.totalImages && isGenerating)
            {
                ClearSpawnedObjects();
                _currentBBoxes.Clear();
                _objectLabels.Clear();

                if (_segmentationRenderer != null)
                    _segmentationRenderer.ResetInstanceCounter();

                ApplyRandomizations();
                SpawnObjects();

                yield return null;
                yield return _waitForEndOfFrame;

                ComputeBoundingBoxes();
                CaptureAndSave();

                currentImageIndex++;
                OnProgress?.Invoke(currentImageIndex, config.totalImages);
                yield return null;
            }

            RestoreOriginalSettings();
            ResetDOF();
            FinalizeDataset();
            isGenerating = false;
        }
        #endregion

        #region === 360 SINGLE OBJECT GENERATION ===
        IEnumerator Generate360Coroutine()
        {
            isGenerating = true;
            currentImageIndex = 0;
            InitializeOutput();
            InitializeTextures();
            InitializeAnnotations();
            SaveOriginalSettings();

            if (singleObjectConfig.targetObject == null)
            {
                Debug.LogError("[Dataset Generator] No target object assigned for 360 capture!");
                isGenerating = false;
                yield break;
            }

            Vector3 targetPos = singleObjectConfig.targetObject.transform.position + singleObjectConfig.lookAtOffset;
            int totalImages = singleObjectConfig.CalculateTotalImages();

            var label = singleObjectConfig.targetObject.GetComponent<DatasetObjectLabel>();
            if (label == null)
            {
                label = singleObjectConfig.targetObject.AddComponent<DatasetObjectLabel>();
                label.classId = singleObjectConfig.classId;
                label.className = singleObjectConfig.className;
                label.boxColor = singleObjectConfig.boxColor;
            }

            for (float pitch = singleObjectConfig.minPitch; pitch <= singleObjectConfig.maxPitch && isGenerating; pitch += singleObjectConfig.angleStepPitch)
            {
                for (float yaw = 0f; yaw < 360f && isGenerating; yaw += singleObjectConfig.angleStepYaw)
                {
                    _currentBBoxes.Clear();
                    _objectLabels.Clear();

                    if (_segmentationRenderer != null)
                        _segmentationRenderer.ResetInstanceCounter();

                    ApplyRandomizations();

                    if (singleObjectConfig.rotateObject)
                        singleObjectConfig.targetObject.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
                    else
                    {
                        Vector3 orbitPos = CalculateOrbitPosition(targetPos, yaw, pitch, singleObjectConfig.orbitDistance);
                        captureCamera.transform.position = orbitPos;
                        captureCamera.transform.LookAt(targetPos);
                    }

                    if (singleObjectConfig.randomizeScale)
                    {
                        float scale = Random.Range(singleObjectConfig.scaleRange.x, singleObjectConfig.scaleRange.y);
                        singleObjectConfig.targetObject.transform.localScale = Vector3.one * scale;
                    }

                    _spawnedObjects.Clear();
                    _spawnedObjects.Add(singleObjectConfig.targetObject);

                    if (_segmentationRenderer != null)
                        label.instanceId = _segmentationRenderer.AssignInstanceId();

                    _objectLabels[singleObjectConfig.targetObject] = label;

                    yield return null;
                    yield return _waitForEndOfFrame;

                    ComputeSingleObjectBBox();
                    CaptureAndSave();

                    currentImageIndex++;
                    OnProgress?.Invoke(currentImageIndex, totalImages);
                    yield return null;
                }
            }

            RestoreOriginalSettings();
            ResetDOF();
            FinalizeDataset();
            isGenerating = false;
        }

        Vector3 CalculateOrbitPosition(Vector3 center, float yaw, float pitch, float distance)
        {
            float yawRad = yaw * Mathf.Deg2Rad;
            float pitchRad = pitch * Mathf.Deg2Rad;
            float cosPitch = Mathf.Cos(pitchRad);
            return new Vector3(
                center.x + distance * cosPitch * Mathf.Sin(yawRad),
                center.y + distance * Mathf.Sin(pitchRad),
                center.z + distance * cosPitch * Mathf.Cos(yawRad)
            );
        }

        void ComputeSingleObjectBBox()
        {
            var obj = singleObjectConfig.targetObject;
            if (!obj) return;

            var label = obj.GetComponent<DatasetObjectLabel>();
            Bounds bounds = CalculateBounds(obj);
            Rect screenRect = BoundsToScreenRect(bounds);

            if (screenRect.width < 5 || screenRect.height < 5) return;

            float visibility = CalculateVisibility(screenRect);
            Rect clampedRect = ClampToScreen(screenRect);
            float invW = 1f / config.imageWidth;
            float invH = 1f / config.imageHeight;

            _currentBBoxes.Add(new BoundingBoxData
            {
                classId = singleObjectConfig.classId,
                className = singleObjectConfig.className,
                kittiType = "Car",
                color = singleObjectConfig.boxColor,
                screenRect = clampedRect,
                xMin = clampedRect.x,
                yMin = clampedRect.y,
                xMax = clampedRect.x + clampedRect.width,
                yMax = clampedRect.y + clampedRect.height,
                centerX = (clampedRect.x + clampedRect.width * 0.5f) * invW,
                centerY = (clampedRect.y + clampedRect.height * 0.5f) * invH,
                normWidth = clampedRect.width * invW,
                normHeight = clampedRect.height * invH,
                worldPosition = obj.transform.position,
                dimensions = bounds.size,
                rotationY = obj.transform.eulerAngles.y * Mathf.Deg2Rad,
                visibility = visibility,
                sourceObject = obj,
                instanceId = label != null ? label.instanceId : 0
            });
        }
        #endregion

        #region === RANDOMIZATION ===
        void SaveOriginalSettings()
        {
            if (captureCamera)
            {
                _originalCameraPos = captureCamera.transform.position;
                _originalCameraRot = captureCamera.transform.rotation;
            }
            if (mainLight)
            {
                _originalLightIntensity = mainLight.intensity;
                _originalLightColor = mainLight.color;
                _originalLightRot = mainLight.transform.rotation;
#if UNITY_PIPELINE_HDRP
                if (_hdLightData != null)
                    _originalHDRPIntensity = _hdLightData.intensity;
#endif
            }
        }

        void RestoreOriginalSettings()
        {
            if (captureCamera)
            {
                captureCamera.transform.position = _originalCameraPos;
                captureCamera.transform.rotation = _originalCameraRot;
            }
            if (mainLight)
            {
                mainLight.color = _originalLightColor;
                mainLight.transform.rotation = _originalLightRot;
#if UNITY_PIPELINE_HDRP
                if (_hdLightData != null)
                {
                    _hdLightData.intensity = _originalHDRPIntensity;
                    _hdLightData.SetColor(_originalLightColor);
                }
                else
                    mainLight.intensity = _originalLightIntensity;
#else
                mainLight.intensity = _originalLightIntensity;
#endif
            }
        }

        void ApplyRandomizations()
        {
            if (augmentation.randomizeLighting && mainLight)
                ApplyLightingRandomization();
            if (augmentation.randomizeDepthOfField)
                ApplyDOF();
            if (augmentation.randomizeBackground && augmentation.backgroundPlane)
            {
                var renderer = augmentation.backgroundPlane.GetComponent<Renderer>();
                if (renderer)
                {
                    if (augmentation.backgroundColors?.Length > 0)
                        renderer.material.color = augmentation.backgroundColors[Random.Range(0, augmentation.backgroundColors.Length)];
                    if (augmentation.backgroundMaterials?.Length > 0)
                        renderer.material = augmentation.backgroundMaterials[Random.Range(0, augmentation.backgroundMaterials.Length)];
                }
            }
        }

        void ApplyLightingRandomization()
        {
            float intensityMult = Random.Range(augmentation.intensityMin, augmentation.intensityMax);
            Color randomColor = augmentation.randomizeLightColor
                ? Color.Lerp(augmentation.lightColorMin, augmentation.lightColorMax, Random.value)
                : mainLight.color;

#if UNITY_PIPELINE_HDRP
            if (_hdLightData != null)
            {
                _hdLightData.intensity = _originalHDRPIntensity * intensityMult;
                _hdLightData.SetColor(randomColor);
            }
            else
            {
                mainLight.intensity = _originalLightIntensity * intensityMult;
                mainLight.color = randomColor;
            }
#else
            mainLight.intensity = _originalLightIntensity * intensityMult;
            mainLight.color = randomColor;
#endif
            if (augmentation.randomizeLightAngle)
            {
                Vector3 euler = _originalLightRot.eulerAngles;
                euler.x += Random.Range(-augmentation.lightAngleRange, augmentation.lightAngleRange);
                euler.y += Random.Range(-augmentation.lightAngleRange, augmentation.lightAngleRange);
                mainLight.transform.rotation = Quaternion.Euler(euler);
            }
        }

        void ApplyDOF()
        {
            if (!_dofInitialized) { InitializeDOF(); if (!_dofInitialized) return; }

#if UNITY_PIPELINE_URP
            if (_dofOverride == null) return;
            float focusDist = Random.Range(augmentation.focusDistanceMin, augmentation.focusDistanceMax);
            float aperture = Random.Range(augmentation.apertureMin, augmentation.apertureMax);
            float focalLen = Random.Range(augmentation.focalLengthMin, augmentation.focalLengthMax);
            _dofOverride.mode.value = UnityEngine.Rendering.Universal.DepthOfFieldMode.Bokeh;
            _dofOverride.mode.overrideState = true;
            _dofOverride.focusDistance.value = focusDist;
            _dofOverride.focusDistance.overrideState = true;
            _dofOverride.aperture.value = aperture;
            _dofOverride.aperture.overrideState = true;
            _dofOverride.focalLength.value = focalLen;
            _dofOverride.focalLength.overrideState = true;
#elif UNITY_PIPELINE_HDRP
            if (_dofOverride == null) return;
            float focusDist = Random.Range(augmentation.focusDistanceMin, augmentation.focusDistanceMax);
            float aperture = Random.Range(augmentation.apertureMin, augmentation.apertureMax);
            float focalLen = Random.Range(augmentation.focalLengthMin, augmentation.focalLengthMax);
            _dofOverride.focusMode.value = UnityEngine.Rendering.HighDefinition.DepthOfFieldMode.Manual;
            _dofOverride.focusMode.overrideState = true;
            _dofOverride.focusDistance.value = focusDist;
            _dofOverride.focusDistance.overrideState = true;
            if (captureCamera != null)
            {
                captureCamera.usePhysicalProperties = true;
                captureCamera.aperture = aperture;
                captureCamera.focalLength = focalLen;
            }
            _dofOverride.nearFocusStart.value = focusDist * 0.5f;
            _dofOverride.nearFocusStart.overrideState = true;
            _dofOverride.nearFocusEnd.value = focusDist * 0.8f;
            _dofOverride.nearFocusEnd.overrideState = true;
            _dofOverride.farFocusStart.value = focusDist * 1.2f;
            _dofOverride.farFocusStart.overrideState = true;
            _dofOverride.farFocusEnd.value = focusDist * 2f;
            _dofOverride.farFocusEnd.overrideState = true;
#endif
        }

        void ResetDOF()
        {
#if UNITY_PIPELINE_URP
            if (_dofOverride == null) return;
            _dofOverride.mode.overrideState = false;
            _dofOverride.focusDistance.overrideState = false;
            _dofOverride.aperture.overrideState = false;
            _dofOverride.focalLength.overrideState = false;
#elif UNITY_PIPELINE_HDRP
            if (_dofOverride == null) return;
            _dofOverride.focusMode.overrideState = false;
            _dofOverride.focusDistance.overrideState = false;
            _dofOverride.nearFocusStart.overrideState = false;
            _dofOverride.nearFocusEnd.overrideState = false;
            _dofOverride.farFocusStart.overrideState = false;
            _dofOverride.farFocusEnd.overrideState = false;
#endif
        }

        RenderTexture ApplyImageAugmentations(RenderTexture source)
        {
            bool needsAug = augmentation.applyGaussianNoise || augmentation.applyMotionBlur ||
                            augmentation.randomizeChromaticAberration || augmentation.randomizeColorGrading;
            if (!needsAug) return source;

            if (_useGPUAugmentation)
            {
                _gpuAugmentation.ApplyAll(source, _augmentedRT, augmentation);
                return _augmentedRT;
            }

            RenderTexture.active = source;
            _captureTex.ReadPixels(new Rect(0, 0, config.imageWidth, config.imageHeight), 0, 0);
            _captureTex.Apply();
            RenderTexture.active = null;
            ApplyImageAugmentationsCPU(_captureTex);
            Graphics.Blit(_captureTex, _augmentedRT);
            return _augmentedRT;
        }

        void ApplyImageAugmentationsCPU(Texture2D tex)
        {
            if (augmentation.randomizeChromaticAberration) ApplyChromaticAberrationCPU(tex);
            if (augmentation.applyGaussianNoise) ApplyGaussianNoiseCPU(tex, augmentation.noiseSigma);
            if (augmentation.applyMotionBlur) ApplySimpleBlurCPU(tex, augmentation.blurRadius);
            if (augmentation.randomizeColorGrading) ApplyColorGradingCPU(tex);
        }

        void ApplyChromaticAberrationCPU(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            _pixelBuffer = tex.GetPixels();
            EnsureBufferSize(ref _tempBuffer, w * h);
            float offset = Random.Range(augmentation.aberrationOffsetMin, augmentation.aberrationOffsetMax);
            float cx = w * 0.5f, cy = h * 0.5f;
            float invMaxDist = 1f / Mathf.Sqrt(cx * cx + cy * cy);

            for (int y = 0; y < h; y++)
            {
                float dy = y - cy;
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float curOff = offset * (dist * invMaxDist) * (dist * invMaxDist);
                    float invD = dist > 0 ? 1f / dist : 0;
                    float dirX = dx * invD, dirY = dy * invD;
                    int rX = Mathf.Clamp(Mathf.RoundToInt(x + dirX * curOff), 0, w - 1);
                    int rY = Mathf.Clamp(Mathf.RoundToInt(y + dirY * curOff), 0, h - 1);
                    int bX = Mathf.Clamp(Mathf.RoundToInt(x - dirX * curOff), 0, w - 1);
                    int bY = Mathf.Clamp(Mathf.RoundToInt(y - dirY * curOff), 0, h - 1);
                    int idx = row + x;
                    _tempBuffer[idx] = new Color(_pixelBuffer[rY * w + rX].r, _pixelBuffer[idx].g, _pixelBuffer[bY * w + bX].b, _pixelBuffer[idx].a);
                }
            }
            tex.SetPixels(_tempBuffer);
            tex.Apply();
        }

        void ApplyGaussianNoiseCPU(Texture2D tex, float sigma)
        {
            _pixelBuffer = tex.GetPixels();
            for (int i = 0; i < _pixelBuffer.Length; i++)
            {
                float noise = GaussianRandom() * sigma;
                ref Color c = ref _pixelBuffer[i];
                c.r = Mathf.Clamp01(c.r + noise);
                c.g = Mathf.Clamp01(c.g + noise);
                c.b = Mathf.Clamp01(c.b + noise);
            }
            tex.SetPixels(_pixelBuffer);
            tex.Apply();
        }

        float GaussianRandom()
        {
            float u1 = 1f - Random.value, u2 = 1f - Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        }

        void ApplySimpleBlurCPU(Texture2D tex, int radius)
        {
            int w = tex.width, h = tex.height;
            _pixelBuffer = tex.GetPixels();
            EnsureBufferSize(ref _tempBuffer, w * h);
            int kernelSize = radius * 2 + 1;
            float invK = 1f / kernelSize;

            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                Color sum = Color.black;
                for (int kx = -radius; kx <= radius; kx++)
                    sum += _pixelBuffer[row + Mathf.Clamp(kx, 0, w - 1)];
                _tempBuffer[row] = sum * invK;
                for (int x = 1; x < w; x++)
                {
                    sum -= _pixelBuffer[row + Mathf.Clamp(x - radius - 1, 0, w - 1)];
                    sum += _pixelBuffer[row + Mathf.Clamp(x + radius, 0, w - 1)];
                    _tempBuffer[row + x] = sum * invK;
                }
            }
            for (int x = 0; x < w; x++)
            {
                Color sum = Color.black;
                for (int ky = -radius; ky <= radius; ky++)
                    sum += _tempBuffer[Mathf.Clamp(ky, 0, h - 1) * w + x];
                _pixelBuffer[x] = sum * invK;
                for (int y = 1; y < h; y++)
                {
                    sum -= _tempBuffer[Mathf.Clamp(y - radius - 1, 0, h - 1) * w + x];
                    sum += _tempBuffer[Mathf.Clamp(y + radius, 0, h - 1) * w + x];
                    _pixelBuffer[y * w + x] = sum * invK;
                }
            }
            tex.SetPixels(_pixelBuffer);
            tex.Apply();
        }

        void ApplyColorGradingCPU(Texture2D tex)
        {
            float hueShift = Random.Range(augmentation.hueShiftMin, augmentation.hueShiftMax) / 360f;
            float satMod = Random.Range(augmentation.saturationMin, augmentation.saturationMax) * 0.01f;
            float contrastMod = Random.Range(augmentation.contrastMin, augmentation.contrastMax) * 0.01f;
            float exposureMod = Mathf.Pow(2f, Random.Range(augmentation.exposureMin, augmentation.exposureMax));
            float contrastFactor = 1f + contrastMod;

            _pixelBuffer = tex.GetPixels();
            for (int i = 0; i < _pixelBuffer.Length; i++)
            {
                ref Color c = ref _pixelBuffer[i];
                c.r *= exposureMod; c.g *= exposureMod; c.b *= exposureMod;
                Color.RGBToHSV(c, out float h, out float s, out float v);
                h = (h + hueShift + 1f) % 1f;
                s = Mathf.Clamp01(s + satMod);
                c = Color.HSVToRGB(h, s, v);
                c.r = Mathf.Clamp01((c.r - 0.5f) * contrastFactor + 0.5f);
                c.g = Mathf.Clamp01((c.g - 0.5f) * contrastFactor + 0.5f);
                c.b = Mathf.Clamp01((c.b - 0.5f) * contrastFactor + 0.5f);
            }
            tex.SetPixels(_pixelBuffer);
            tex.Apply();
        }

        void EnsureBufferSize(ref Color[] buffer, int size) { if (buffer == null || buffer.Length < size) buffer = new Color[size]; }
        #endregion

        #region === SPAWNING ===
        void SpawnObjects()
        {
            foreach (var objClass in classes)
            {
                if (objClass.prefabs.Count == 0) continue;
                int count = Random.Range(objClass.minCount, objClass.maxCount + 1);

                for (int i = 0; i < count; i++)
                {
                    Vector3 pos = GetValidSpawnPosition();
                    if (pos == Vector3.zero) continue;

                    GameObject prefab = objClass.prefabs[Random.Range(0, objClass.prefabs.Count)];
                    GameObject obj = Instantiate(prefab, pos, Quaternion.identity);

                    if (randomizeRotation)
                        obj.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                    if (randomizeScale)
                        obj.transform.localScale *= Random.Range(scaleRange.x, scaleRange.y);

                    var label = obj.AddComponent<DatasetObjectLabel>();
                    label.classId = objClass.classId;
                    label.className = objClass.className;
                    label.kittiType = objClass.kittiType;
                    label.boxColor = objClass.boxColor;

                    if (_segmentationRenderer != null)
                        label.instanceId = _segmentationRenderer.AssignInstanceId();

                    _spawnedObjects.Add(obj);
                    _objectLabels[obj] = label;
                }
            }
        }

        Vector3 GetValidSpawnPosition()
        {
            float halfX = spawnAreaSize.x * 0.5f, halfZ = spawnAreaSize.z * 0.5f;
            float sepSq = minObjectSeparation * minObjectSeparation;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                Vector3 pos = new Vector3(
                    spawnAreaCenter.x + Random.Range(-halfX, halfX),
                    spawnAreaCenter.y + spawnAreaSize.y * 0.5f,
                    spawnAreaCenter.z + Random.Range(-halfZ, halfZ)
                );
                bool valid = true;
                for (int i = 0; i < _spawnedObjects.Count; i++)
                {
                    if ((pos - _spawnedObjects[i].transform.position).sqrMagnitude < sepSq)
                    { valid = false; break; }
                }
                if (valid) return pos;
            }
            return Vector3.zero;
        }

        void ClearSpawnedObjects()
        {
            for (int i = 0; i < _spawnedObjects.Count; i++)
                if (_spawnedObjects[i]) DestroyImmediate(_spawnedObjects[i]);
            _spawnedObjects.Clear();
            _objectLabels.Clear();
        }
        #endregion

        #region === BOUNDING BOX ===
        void ComputeBoundingBoxes()
        {
            _currentBBoxes.Clear();
            float invW = 1f / config.imageWidth, invH = 1f / config.imageHeight;

            for (int i = 0; i < _spawnedObjects.Count; i++)
            {
                var obj = _spawnedObjects[i];
                if (!obj) continue;
                var label = obj.GetComponent<DatasetObjectLabel>();
                if (label == null) continue;

                Bounds bounds = CalculateBounds(obj);
                Rect screenRect = BoundsToScreenRect(bounds);
                if (screenRect.width < 5 || screenRect.height < 5) continue;

                float visibility = CalculateVisibility(screenRect);
                float truncation = 1f - visibility;
                if (visibility < config.minVisibility) continue;
                if (!config.includeTruncated && truncation > 0.5f) continue;

                Rect clamped = ClampToScreen(screenRect);
                _currentBBoxes.Add(new BoundingBoxData
                {
                    classId = label.classId,
                    className = label.className,
                    kittiType = label.kittiType,
                    color = label.boxColor,
                    screenRect = clamped,
                    xMin = clamped.x,
                    yMin = clamped.y,
                    xMax = clamped.x + clamped.width,
                    yMax = clamped.y + clamped.height,
                    centerX = (clamped.x + clamped.width * 0.5f) * invW,
                    centerY = (clamped.y + clamped.height * 0.5f) * invH,
                    normWidth = clamped.width * invW,
                    normHeight = clamped.height * invH,
                    worldPosition = obj.transform.position,
                    dimensions = bounds.size,
                    rotationY = obj.transform.eulerAngles.y * Mathf.Deg2Rad,
                    alpha = CalculateAlpha(obj.transform.position),
                    truncation = truncation,
                    visibility = visibility,
                    sourceObject = obj,
                    instanceId = label.instanceId
                });
            }
        }

        Bounds CalculateBounds(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(obj.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        Rect BoundsToScreenRect(Bounds bounds)
        {
            Vector3 min = bounds.min, max = bounds.max;
            _boundsCorners[0] = min; _boundsCorners[1] = max;
            _boundsCorners[2] = new Vector3(min.x, min.y, max.z);
            _boundsCorners[3] = new Vector3(min.x, max.y, min.z);
            _boundsCorners[4] = new Vector3(max.x, min.y, min.z);
            _boundsCorners[5] = new Vector3(min.x, max.y, max.z);
            _boundsCorners[6] = new Vector3(max.x, min.y, max.z);
            _boundsCorners[7] = new Vector3(max.x, max.y, min.z);

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                Vector3 sp = captureCamera.WorldToScreenPoint(_boundsCorners[i]);
                if (sp.z < 0) continue;
                if (sp.x < minX) minX = sp.x; if (sp.y < minY) minY = sp.y;
                if (sp.x > maxX) maxX = sp.x; if (sp.y > maxY) maxY = sp.y;
            }
            float imgMinY = config.imageHeight - maxY;
            float imgMaxY = config.imageHeight - minY;
            return new Rect(minX, imgMinY, maxX - minX, imgMaxY - imgMinY);
        }

        float CalculateVisibility(Rect rect)
        {
            float x1 = Mathf.Max(rect.x, 0), y1 = Mathf.Max(rect.y, 0);
            float x2 = Mathf.Min(rect.xMax, config.imageWidth);
            float y2 = Mathf.Min(rect.yMax, config.imageHeight);
            float intersect = Mathf.Max(0, x2 - x1) * Mathf.Max(0, y2 - y1);
            float original = rect.width * rect.height;
            return original > 0 ? intersect / original : 0;
        }

        Rect ClampToScreen(Rect rect)
        {
            float x1 = Mathf.Max(0, rect.x), y1 = Mathf.Max(0, rect.y);
            float x2 = Mathf.Min(config.imageWidth, rect.xMax);
            float y2 = Mathf.Min(config.imageHeight, rect.yMax);
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }

        float CalculateAlpha(Vector3 pos)
        {
            Vector3 dir = pos - captureCamera.transform.position;
            return Mathf.Atan2(dir.x, dir.z);
        }
        #endregion

        #region === CAPTURE & SAVE ===
        void InitializeOutput()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            _outputPath = Path.Combine(config.outputPath, $"{config.datasetName}_{timestamp}");
            CreateDirectoryStructure();
            _trainImages.Clear(); _valImages.Clear(); _testImages.Clear();
        }

        void CreateDirectoryStructure()
        {
            Directory.CreateDirectory(_outputPath);
            if (config.splitMode == SplitMode.TrainValTest || config.splitMode == SplitMode.TrainVal)
            {
                string[] splits = config.splitMode == SplitMode.TrainVal ? new[] { "train", "valid" } : new[] { "train", "valid", "test" };
                foreach (var split in splits)
                {
                    Directory.CreateDirectory(Path.Combine(_outputPath, split, "images"));
                    Directory.CreateDirectory(Path.Combine(_outputPath, split, "labels"));
                    if (config.generateInstanceSegmentation)
                        Directory.CreateDirectory(Path.Combine(_outputPath, split, "instance_segmentation"));
                    if (config.generateSemanticSegmentation)
                        Directory.CreateDirectory(Path.Combine(_outputPath, split, "semantic_segmentation"));
                    if (config.generatePanopticSegmentation)
                        Directory.CreateDirectory(Path.Combine(_outputPath, split, "panoptic_segmentation"));
                    if (config.generateBinaryMask)
                        Directory.CreateDirectory(Path.Combine(_outputPath, split, "masks"));
                    if (config.generateDepthMap)
                        Directory.CreateDirectory(Path.Combine(_outputPath, split, "depth"));
                }
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(_outputPath, "images"));
                Directory.CreateDirectory(Path.Combine(_outputPath, "labels"));
                if (config.generateInstanceSegmentation)
                    Directory.CreateDirectory(Path.Combine(_outputPath, "instance_segmentation"));
                if (config.generateSemanticSegmentation)
                    Directory.CreateDirectory(Path.Combine(_outputPath, "semantic_segmentation"));
                if (config.generatePanopticSegmentation)
                    Directory.CreateDirectory(Path.Combine(_outputPath, "panoptic_segmentation"));
                if (config.generateBinaryMask)
                    Directory.CreateDirectory(Path.Combine(_outputPath, "masks"));
                if (config.generateDepthMap)
                    Directory.CreateDirectory(Path.Combine(_outputPath, "depth"));
            }
            if (config.saveVisualization)
                Directory.CreateDirectory(Path.Combine(_outputPath, "visualizations"));
            Directory.CreateDirectory(Path.Combine(_outputPath, "annotations"));
        }

        void InitializeTextures()
        {
            _captureRT = new RenderTexture(config.imageWidth, config.imageHeight, 24, RenderTextureFormat.ARGB32);
            _captureRT.Create();
            _augmentedRT = new RenderTexture(config.imageWidth, config.imageHeight, 0, RenderTextureFormat.ARGB32);
            _augmentedRT.Create();
            _captureTex = new Texture2D(config.imageWidth, config.imageHeight, TextureFormat.RGB24, false);
            _vizTex = new Texture2D(config.imageWidth, config.imageHeight, TextureFormat.RGB24, false);
            int pixelCount = config.imageWidth * config.imageHeight;
            _pixelBuffer = new Color[pixelCount];
            _tempBuffer = new Color[pixelCount];
        }

        void InitializeAnnotations()
        {
            _cocoDataset = new COCODataset();
            if (config.generatorMode == GeneratorMode.SingleObject360)
                _cocoDataset.categories.Add(new COCOCategory { id = singleObjectConfig.classId, name = singleObjectConfig.className });
            else
                foreach (var c in classes)
                    _cocoDataset.categories.Add(new COCOCategory { id = c.classId, name = c.className });

            _createMLDataset = new CreateMLDataset();
            _tfRecordCSV = new List<string>(config.totalImages + 1) { "filename,xmin,ymin,xmax,ymax,class" };
            _annotationId = 1;
        }

        void CaptureAndSave()
        {
            string split = DetermineSplit();
            string baseName = $"img_{currentImageIndex:D6}";
            string ext = config.imageFormat == ImageFormat.PNG ? ".png" : ".jpg";

            string imgPath, lblPath, instPath = "", semPath = "", panPath = "", maskPath = "", depthPath = "";
            string baseDir = config.splitMode != SplitMode.None ? Path.Combine(_outputPath, split) : _outputPath;

            imgPath = Path.Combine(baseDir, "images", baseName + ext);
            lblPath = Path.Combine(baseDir, "labels", baseName + ".txt");
            if (config.generateInstanceSegmentation)
                instPath = Path.Combine(baseDir, "instance_segmentation", baseName + ".png");
            if (config.generateSemanticSegmentation)
                semPath = Path.Combine(baseDir, "semantic_segmentation", baseName + ".png");
            if (config.generatePanopticSegmentation)
                panPath = Path.Combine(baseDir, "panoptic_segmentation", baseName + ".png");
            if (config.generateBinaryMask)
                maskPath = Path.Combine(baseDir, "masks", baseName + ".png");
            if (config.generateDepthMap)
                depthPath = Path.Combine(baseDir, "depth", baseName + ".png");

            TrackSplit(split, baseName);

            // Enable depth texture if needed
            DepthTextureMode originalDepthMode = captureCamera.depthTextureMode;
            if (config.generateDepthMap)
                captureCamera.depthTextureMode = DepthTextureMode.Depth;

            // === Capture RGB ===
            var prevRT = captureCamera.targetTexture;
            captureCamera.targetTexture = _captureRT;
            captureCamera.Render();
            captureCamera.targetTexture = prevRT;

            RenderTexture finalRT = ApplyImageAugmentations(_captureRT);
            RenderTexture.active = finalRT;
            _captureTex.ReadPixels(new Rect(0, 0, config.imageWidth, config.imageHeight), 0, 0);
            _captureTex.Apply();
            RenderTexture.active = null;

            byte[] bytes = config.imageFormat == ImageFormat.PNG ? _captureTex.EncodeToPNG() : _captureTex.EncodeToJPG(config.jpgQuality);
            File.WriteAllBytes(imgPath, bytes);

            // === Capture Segmentations ===
            if (_segmentationRenderer != null)
            {
                if (config.generateInstanceSegmentation)
                    _segmentationRenderer.RenderInstanceSegmentation(captureCamera, _spawnedObjects, _objectLabels, instPath);

                if (config.generateSemanticSegmentation)
                    _segmentationRenderer.RenderSemanticSegmentation(captureCamera, _spawnedObjects, _objectLabels, semPath);

                if (config.generatePanopticSegmentation)
                    _segmentationRenderer.RenderPanopticSegmentation(captureCamera, _spawnedObjects, _objectLabels, panPath);

                if (config.generateBinaryMask)
                    _segmentationRenderer.RenderBinaryMask(captureCamera, _spawnedObjects, _objectLabels, maskPath);
            }

            // === Capture Depth Map ===
            if (config.generateDepthMap && _depthRenderer != null)
            {
                _depthRenderer.RenderDepthMap(captureCamera, config.depthMaxDistance, config.depthLinear, depthPath, _spawnedObjects, config.maskDepthToObjects);
            }

            captureCamera.depthTextureMode = originalDepthMode;

            SaveAnnotations(baseName, ext, lblPath);
            if (config.saveVisualization) SaveVisualization(baseName);
        }

        void SaveAnnotations(string baseName, string ext, string labelPath)
        {
            string filename = baseName + ext;
            bool all = config.annotationFormat == AnnotationFormat.All;

            if (config.annotationFormat == AnnotationFormat.YOLO || all)
                File.WriteAllLines(labelPath, _currentBBoxes.Select(b => b.ToYOLO()));
            if (config.annotationFormat == AnnotationFormat.KITTI || all)
                File.WriteAllLines(all ? labelPath.Replace(".txt", "_kitti.txt") : labelPath, _currentBBoxes.Select(b => b.ToKITTI()));
            if (config.annotationFormat == AnnotationFormat.PascalVOC || all)
                SaveVOC(Path.Combine(_outputPath, "annotations", baseName + ".xml"), filename);
            if (config.annotationFormat == AnnotationFormat.COCO || all)
                AccumulateCOCO(filename);
            if (config.annotationFormat == AnnotationFormat.CreateML || all)
                AccumulateCreateML(filename);
            if (config.annotationFormat == AnnotationFormat.TFRecord || all)
                foreach (var b in _currentBBoxes) _tfRecordCSV.Add(b.ToCSV(filename));
        }

        void SaveVOC(string path, string filename)
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            _stringBuilder.AppendLine("<annotation>");
            _stringBuilder.Append("  <filename>").Append(filename).AppendLine("</filename>");
            _stringBuilder.Append("  <size><width>").Append(config.imageWidth).Append("</width><height>").Append(config.imageHeight).AppendLine("</height><depth>3</depth></size>");
            foreach (var b in _currentBBoxes)
            {
                _stringBuilder.AppendLine("  <object>");
                _stringBuilder.Append("    <name>").Append(b.className).Append("</name><truncated>").Append(b.truncation > 0.1f ? 1 : 0).AppendLine("</truncated><difficult>0</difficult>");
                _stringBuilder.Append("    <bndbox><xmin>").Append((int)b.xMin).Append("</xmin><ymin>").Append((int)b.yMin).Append("</ymin><xmax>").Append((int)b.xMax).Append("</xmax><ymax>").Append((int)b.yMax).AppendLine("</ymax></bndbox>");
                _stringBuilder.AppendLine("  </object>");
            }
            _stringBuilder.AppendLine("</annotation>");
            File.WriteAllText(path, _stringBuilder.ToString());
        }

        void AccumulateCOCO(string filename)
        {
            _cocoDataset.images.Add(new COCOImage { id = currentImageIndex, file_name = filename, width = config.imageWidth, height = config.imageHeight });
            foreach (var b in _currentBBoxes)
            {
                var ann = new COCOAnnotation { id = _annotationId++, image_id = currentImageIndex, category_id = b.classId, area = b.screenRect.width * b.screenRect.height };
                ann.bbox[0] = b.xMin; ann.bbox[1] = b.yMin; ann.bbox[2] = b.screenRect.width; ann.bbox[3] = b.screenRect.height;
                _cocoDataset.annotations.Add(ann);
            }
        }

        void AccumulateCreateML(string filename)
        {
            var img = new CreateMLImage { image = filename };
            foreach (var b in _currentBBoxes)
                img.annotations.Add(new CreateMLAnnotation { label = b.className, coordinates = new CreateMLCoordinates { x = b.xMin + b.screenRect.width * 0.5f, y = b.yMin + b.screenRect.height * 0.5f, width = b.screenRect.width, height = b.screenRect.height } });
            _createMLDataset.images.Add(img);
        }

        string DetermineSplit()
        {
            if (config.splitMode == SplitMode.None) return "";
            float r = Random.value;
            if (r < config.trainRatio) return "train";
            if (r < config.trainRatio + config.valRatio) return "valid";
            return config.splitMode == SplitMode.TrainVal ? "valid" : "test";
        }

        void TrackSplit(string split, string baseName)
        {
            switch (split) { case "train": _trainImages.Add(baseName); break; case "valid": _valImages.Add(baseName); break; case "test": _testImages.Add(baseName); break; }
        }
        #endregion

        #region === VISUALIZATION ===
        void SaveVisualization(string baseName)
        {
            _pixelBuffer = _captureTex.GetPixels();
            _vizTex.SetPixels(_pixelBuffer);
            foreach (var bbox in _currentBBoxes)
            {
                DrawRectangle(_vizTex, bbox.screenRect, bbox.color, 3);
                DrawLabel(_vizTex, bbox.className.ToUpper(), bbox.screenRect, bbox.color);
            }
            _vizTex.Apply();
            File.WriteAllBytes(Path.Combine(_outputPath, "visualizations", baseName + "_viz.jpg"), _vizTex.EncodeToJPG(95));
        }

        void DrawRectangle(Texture2D tex, Rect rect, Color color, int thickness)
        {
            int x = (int)rect.x, y = config.imageHeight - (int)rect.y - (int)rect.height;
            int w = (int)rect.width, h = (int)rect.height;
            int texW = tex.width, texH = tex.height;
            for (int t = 0; t < thickness; t++)
            {
                int topY = y + h - t, botY = y + t, leftX = x + t, rightX = x + w - t;
                for (int i = x; i < x + w && i < texW; i++)
                {
                    if (i >= 0) { if (topY >= 0 && topY < texH) tex.SetPixel(i, topY, color); if (botY >= 0 && botY < texH) tex.SetPixel(i, botY, color); }
                }
                for (int j = y; j < y + h && j < texH; j++)
                {
                    if (j >= 0) { if (leftX >= 0 && leftX < texW) tex.SetPixel(leftX, j, color); if (rightX >= 0 && rightX < texW) tex.SetPixel(rightX, j, color); }
                }
            }
        }

        void DrawLabel(Texture2D tex, string text, Rect rect, Color bgColor)
        {
            int labelH = 18, labelW = text.Length * 8 + 8, pad = 2;
            int boxTopY = config.imageHeight - (int)rect.y;
            int labelX = (int)rect.x, labelY = boxTopY;
            int texW = tex.width, texH = tex.height;
            for (int i = labelX; i < labelX + labelW && i < texW; i++)
                for (int j = labelY; j < labelY + labelH && j < texH; j++)
                    if (i >= 0 && j >= 0) tex.SetPixel(i, j, bgColor);
            Color textColor = GetContrastColor(bgColor);
            int textX = labelX + pad, textY = labelY + pad + 2;
            foreach (char c in text)
            {
                if (Font.TryGetValue(c, out byte[] glyph))
                {
                    for (int row = 0; row < 7; row++)
                    {
                        byte glyphRow = glyph[row];
                        for (int col = 0; col < 8; col++)
                            if ((glyphRow & (1 << (7 - col))) != 0)
                            {
                                int px = textX + col, py = textY + (6 - row);
                                if (px >= 0 && px < texW && py >= 0 && py < texH) tex.SetPixel(px, py, textColor);
                            }
                    }
                }
                textX += 8;
            }
        }

        Color GetContrastColor(Color bg) { float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b; return lum > 0.5f ? Color.black : Color.white; }
        #endregion

        #region === FINALIZATION ===
        void FinalizeDataset()
        {
            bool all = config.annotationFormat == AnnotationFormat.All;
            if (config.annotationFormat == AnnotationFormat.COCO || all)
                File.WriteAllText(Path.Combine(_outputPath, "annotations", "instances_coco.json"), JsonUtility.ToJson(_cocoDataset, true));
            if (config.annotationFormat == AnnotationFormat.CreateML || all)
                File.WriteAllText(Path.Combine(_outputPath, "annotations", "annotations_createml.json"), JsonUtility.ToJson(_createMLDataset, true));
            if (config.annotationFormat == AnnotationFormat.TFRecord || all)
                File.WriteAllLines(Path.Combine(_outputPath, "annotations", "annotations_tfrecord.csv"), _tfRecordCSV);
            if (config.annotationFormat == AnnotationFormat.YOLO || all)
                GenerateYOLOConfig();
            GenerateDatasetInfo();
            Debug.Log($"[Dataset Generator] Complete! {currentImageIndex} images → {_outputPath}");
            OnComplete?.Invoke(_outputPath);
        }

        void GenerateYOLOConfig()
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendLine($"# YOLO Dataset - {config.datasetName}");
            _stringBuilder.AppendLine($"path: {_outputPath}");
            _stringBuilder.AppendLine("train: train/images");
            _stringBuilder.AppendLine("val: valid/images");
            if (config.splitMode == SplitMode.TrainValTest) _stringBuilder.AppendLine("test: test/images");
            _stringBuilder.AppendLine();
            int nc = config.generatorMode == GeneratorMode.SingleObject360 ? 1 : classes.Count;
            _stringBuilder.AppendLine($"nc: {nc}");
            _stringBuilder.AppendLine("names:");
            if (config.generatorMode == GeneratorMode.SingleObject360)
                _stringBuilder.AppendLine($"  {singleObjectConfig.classId}: {singleObjectConfig.className}");
            else
                foreach (var c in classes.OrderBy(c => c.classId))
                    _stringBuilder.AppendLine($"  {c.classId}: {c.className}");
            File.WriteAllText(Path.Combine(_outputPath, "data.yaml"), _stringBuilder.ToString());
            var names = config.generatorMode == GeneratorMode.SingleObject360 ? new[] { singleObjectConfig.className } : classes.OrderBy(c => c.classId).Select(c => c.className);
            File.WriteAllLines(Path.Combine(_outputPath, "classes.txt"), names);
        }

        void GenerateDatasetInfo()
        {
            _stringBuilder.Clear();
            _stringBuilder.AppendLine($"Dataset: {config.datasetName}");
            _stringBuilder.AppendLine($"Generated: {DateTime.Now}");
            _stringBuilder.AppendLine($"Mode: {config.generatorMode}");
            _stringBuilder.AppendLine($"Format: {config.annotationFormat}");
            _stringBuilder.AppendLine($"Total Images: {currentImageIndex}");
            _stringBuilder.AppendLine($"Resolution: {config.imageWidth}x{config.imageHeight}");
            _stringBuilder.AppendLine($"GPU Augmentation: {(_useGPUAugmentation ? "Enabled" : "CPU Fallback")}");
            _stringBuilder.AppendLine($"\nSplit: Train={_trainImages.Count}, Valid={_valImages.Count}, Test={_testImages.Count}");
            _stringBuilder.AppendLine($"\nSegmentation Outputs:");
            _stringBuilder.AppendLine($"  Instance Segmentation: {(config.generateInstanceSegmentation ? "Enabled" : "Disabled")}");
            _stringBuilder.AppendLine($"  Semantic Segmentation: {(config.generateSemanticSegmentation ? "Enabled" : "Disabled")}");
            _stringBuilder.AppendLine($"  Panoptic Segmentation: {(config.generatePanopticSegmentation ? "Enabled" : "Disabled")}");
            _stringBuilder.AppendLine($"  Binary Mask: {(config.generateBinaryMask ? "Enabled" : "Disabled")}");
            _stringBuilder.AppendLine($"  Depth Map: {(config.generateDepthMap ? "Enabled" : "Disabled")}");

            if (config.generatorMode == GeneratorMode.SingleObject360)
            {
                _stringBuilder.AppendLine("\n360 Capture Settings:");
                _stringBuilder.AppendLine($"  Yaw Step: {singleObjectConfig.angleStepYaw}°");
                _stringBuilder.AppendLine($"  Pitch Step: {singleObjectConfig.angleStepPitch}°");
                _stringBuilder.AppendLine($"  Pitch Range: {singleObjectConfig.minPitch}° to {singleObjectConfig.maxPitch}°");
            }
            File.WriteAllText(Path.Combine(_outputPath, "dataset_info.txt"), _stringBuilder.ToString());
        }
        #endregion

        void OnDestroy()
        {
            if (_captureRT) _captureRT.Release();
            if (_augmentedRT) _augmentedRT.Release();
            _gpuAugmentation?.Dispose();
            _segmentationRenderer?.Dispose();
            _depthRenderer?.Dispose();
            ClearSpawnedObjects();
            ResetDOF();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0, 1, 0, 0.25f);
            Gizmos.DrawCube(spawnAreaCenter, spawnAreaSize);
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(spawnAreaCenter, spawnAreaSize);
        }
    }
    #endregion

    #region === HELPER ===
    public class DatasetObjectLabel : MonoBehaviour
    {
        public int classId;
        public string className;
        public string kittiType = "Car";
        public Color boxColor = Color.red;
        public int instanceId;
    }
    #endregion
}
