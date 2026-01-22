using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SyntheticDatasetGenerator;

namespace SyntheticDatasetGenerator.Editor
{
    public class MultiFormatDatasetWindow : EditorWindow
    {
        private int _tab;
        private static readonly string[] MultiObjectTabs = { "Setup", "Classes", "Spawn", "Augmentation", "Segmentation", "Generate" };
        private static readonly string[] SingleObjectTabs = { "Setup", "360 Object", "Augmentation", "Segmentation", "Generate" };
        private Vector2 _scroll, _classScroll, _augScroll, _segScroll;

        private DatasetConfig _config = new DatasetConfig();
        private List<ObjectClass> _classes = new List<ObjectClass>();
        private AugmentationConfig _augmentation = new AugmentationConfig();
        private SingleObject360Config _singleObjectConfig = new SingleObject360Config();

        private Camera _camera;
        private Light _mainLight;
        private Vector3 _spawnCenter = Vector3.zero;
        private Vector3 _spawnSize = new Vector3(10, 0, 10);
        private float _minSeparation = 1.5f;
        private bool _randomRotation = true;
        private bool _randomScale = true;
        private Vector2 _scaleRange = new Vector2(0.8f, 1.2f);

        private string _status;
        private MessageType _statusType;
        private bool _showFormatDetails = true;
        private bool _showSegmentationInfo = true;
        private MultiFormatDatasetGenerator _activeGenerator;

        private static readonly Color[] DefaultColors = {
            new Color(1f, 0.2f, 0.2f), new Color(0.2f, 1f, 0.2f), new Color(0.2f, 0.4f, 1f),
            new Color(1f, 1f, 0.2f), new Color(0.2f, 1f, 1f), new Color(1f, 0.2f, 1f),
            new Color(1f, 0.6f, 0.2f), new Color(0.6f, 0.2f, 1f)
        };

        private static readonly string[] KittiTypes = {
            "Car", "Van", "Truck", "Pedestrian", "Person_sitting", "Cyclist", "Tram", "Misc", "DontCare"
        };

        // Cached GUIContent
        private static GUIContent _gcGeneratorMode, _gcDatasetName, _gcOutputPath, _gcBrowse;
        private static GUIContent _gcTotalImages, _gcWidth, _gcHeight, _gcFormat, _gcQuality;
        private static GUIContent _gcAnnotationFormat, _gcSaveVis, _gcSplitMode, _gcTrain, _gcValid;
        private static GUIContent _gcTargetObject, _gcClassId, _gcClassName, _gcBoxColor;
        private static GUIContent _gcOrbitDistance, _gcLookAtOffset, _gcAngleStep, _gcRange;
        private static GUIContent _gcRotateObject, _gcRandomizeScale, _gcScaleRange;
        private static GUIContent _gcCamera, _gcMainLight, _gcCenter, _gcSize, _gcMinSeparation;
        private static GUIContent _gcRandomYRot, _gcRandomScale, _gcMinVisibility, _gcIncludeTrunc, _gcIncludeOccl;
        private static GUIContent _gcInstanceSeg, _gcSemanticSeg, _gcPanopticSeg, _gcDepthMap;
        private static GUIContent _gcDepthMaxDist, _gcDepthLinear;
        private static GUIStyle _headerStyle;
        private static bool _stylesInitialized;

        // Pipeline detection
        private static bool _isPipelineDetected;
        private static string _detectedPipeline = "Unknown";
        private static bool _isDOFSupported;

        [MenuItem("Tools/Multi-Format Dataset Generator")]
        public static void Open()
        {
            var w = GetWindow<MultiFormatDatasetWindow>("Dataset Generator");
            w.minSize = new Vector2(520, 700);
        }

        void OnEnable()
        {
            _config.outputPath = Path.Combine(Application.persistentDataPath, "Datasets");
            if (_classes.Count == 0)
                _classes.Add(new ObjectClass { className = "object", classId = 0, boxColor = DefaultColors[0] });

            DetectRenderPipeline();
        }

        static void DetectRenderPipeline()
        {
            if (_isPipelineDetected) return;
            _isPipelineDetected = true;

            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null)
            {
                _detectedPipeline = "Built-in";
                _isDOFSupported = false;
            }
            else
            {
                string pipelineName = pipeline.GetType().Name;
                if (pipelineName.Contains("Universal") || pipelineName.Contains("URP"))
                {
                    _detectedPipeline = "URP";
#if UNITY_PIPELINE_URP
                    _isDOFSupported = true;
#else
                    _isDOFSupported = false;
#endif
                }
                else if (pipelineName.Contains("HD") || pipelineName.Contains("HDRP"))
                {
                    _detectedPipeline = "HDRP";
#if UNITY_PIPELINE_HDRP
                    _isDOFSupported = true;
#else
                    _isDOFSupported = false;
#endif
                }
                else
                {
                    _detectedPipeline = pipelineName;
                    _isDOFSupported = false;
                }
            }
        }

        static void InitStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, alignment = TextAnchor.MiddleCenter };

            _gcGeneratorMode = new GUIContent("Generator Mode", "Multi-Object: Spawn multiple random objects per image\nSingle Object 360°: Capture one object from all viewing angles");
            _gcDatasetName = new GUIContent("Dataset Name", "Name for this dataset.");
            _gcOutputPath = new GUIContent("Output Path", "Root folder where dataset will be saved.");
            _gcBrowse = new GUIContent("...", "Browse for output folder");
            _gcTotalImages = new GUIContent("Total Images", "Total number of images to generate");
            _gcWidth = new GUIContent("Width", "Output image width in pixels");
            _gcHeight = new GUIContent("Height", "Output image height in pixels");
            _gcFormat = new GUIContent("Format", "Output image file format");
            _gcQuality = new GUIContent("Quality", "JPG compression quality");
            _gcAnnotationFormat = new GUIContent("Format", "Annotation format for bounding box labels.");
            _gcSaveVis = new GUIContent("Save Visualizations", "Save images with bounding boxes drawn");
            _gcSplitMode = new GUIContent("Split Mode", "How to divide dataset into train/validation/test sets");
            _gcTrain = new GUIContent("Train", "Percentage of images for training");
            _gcValid = new GUIContent("Valid", "Percentage of images for validation");

            _gcTargetObject = new GUIContent("Object", "The 3D object to capture from all angles.");
            _gcClassId = new GUIContent("Class ID", "Unique numeric identifier for this object class");
            _gcClassName = new GUIContent("Class Name", "Human-readable name for this object class");
            _gcBoxColor = new GUIContent("Box Color", "Color of the bounding box in visualization images");
            _gcOrbitDistance = new GUIContent("Orbit Distance", "Distance from camera to the object center.");
            _gcLookAtOffset = new GUIContent("Look At Offset", "Offset from object's pivot point.");
            _gcAngleStep = new GUIContent("Angle Step", "Degrees between each capture position.");
            _gcRange = new GUIContent("Range", "Vertical angle range.");
            _gcRotateObject = new GUIContent("Rotate Object Instead", "Object rotates while camera stays fixed.");
            _gcRandomizeScale = new GUIContent("Randomize Scale", "Randomly scale object for each capture");
            _gcScaleRange = new GUIContent("Scale Range", "Min and max scale multipliers");

            _gcCamera = new GUIContent("Camera", "The camera used to capture images.");
            _gcMainLight = new GUIContent("Main Light", "Primary light source for lighting randomization.");
            _gcCenter = new GUIContent("Center", "World position center of spawn area");
            _gcSize = new GUIContent("Size", "Width, Height, and Depth of spawn area.");
            _gcMinSeparation = new GUIContent("Min Separation", "Minimum distance between spawned objects");
            _gcRandomYRot = new GUIContent("Random Y Rotation", "Randomly rotate each object");
            _gcRandomScale = new GUIContent("Random Scale", "Randomly scale each object");
            _gcMinVisibility = new GUIContent("Min Visibility", "Minimum percentage visible");
            _gcIncludeTrunc = new GUIContent("Include Truncated", "Include objects extending beyond borders");
            _gcIncludeOccl = new GUIContent("Include Occluded", "Include partially blocked objects");

            // Segmentation GUIContent
            _gcInstanceSeg = new GUIContent("Instance Segmentation", "Generate per-instance mask images where each object instance has a unique color");
            _gcSemanticSeg = new GUIContent("Semantic Segmentation", "Generate class-based mask images where all objects of the same class share a color");
            _gcPanopticSeg = new GUIContent("Panoptic Segmentation", "Generate combined instance+semantic masks with JSON annotations");
            _gcDepthMap = new GUIContent("Depth Map", "Generate grayscale depth images encoding distance from camera");
            _gcDepthMaxDist = new GUIContent("Max Distance", "Maximum distance for depth normalization (objects beyond appear white)");
            _gcDepthLinear = new GUIContent("Linear Depth", "Use linear depth (true) or logarithmic depth (false) for better near-range precision");
        }

        void OnGUI()
        {
            InitStyles();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawHeader();

            EditorGUILayout.BeginVertical("box");
            _config.generatorMode = (GeneratorMode)EditorGUILayout.EnumPopup(_gcGeneratorMode, _config.generatorMode);
            EditorGUILayout.HelpBox(_config.generatorMode == GeneratorMode.MultiObject
                ? "Multi-Object: Spawn multiple random objects in scene"
                : "Single Object 360°: Capture one object from all angles", MessageType.Info);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);

            string[] currentTabs = _config.generatorMode == GeneratorMode.SingleObject360 ? SingleObjectTabs : MultiObjectTabs;
            _tab = GUILayout.Toolbar(_tab, currentTabs, GUILayout.Height(28));
            EditorGUILayout.Space(10);

            if (_config.generatorMode == GeneratorMode.SingleObject360)
            {
                switch (_tab)
                {
                    case 0: DrawSetupTab(); break;
                    case 1: Draw360ObjectTab(); break;
                    case 2: DrawAugmentationTab(); break;
                    case 3: DrawInstanceSegmentationTab(); break;
                    case 4: DrawGenerateTab(); break;
                }
            }
            else
            {
                switch (_tab)
                {
                    case 0: DrawSetupTab(); break;
                    case 1: DrawClassesTab(); break;
                    case 2: DrawSpawnTab(); break;
                    case 3: DrawAugmentationTab(); break;
                    case 4: DrawInstanceSegmentationTab(); break;
                    case 5: DrawGenerateTab(); break;
                }
            }

            DrawStatus();
            EditorGUILayout.EndScrollView();
        }

        #region === HEADER ===
        void DrawHeader()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Multi-Format Dataset Generator", _headerStyle);
            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField("YOLO • COCO • Pascal VOC • KITTI • CreateML • TFRecord", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.LabelField($"Pipeline: {_detectedPipeline}", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(10);
        }
        #endregion

        #region === SETUP TAB ===
        void DrawSetupTab()
        {
            EditorGUILayout.LabelField("Dataset Information", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _config.datasetName = EditorGUILayout.TextField(_gcDatasetName, _config.datasetName);
            EditorGUILayout.BeginHorizontal();
            _config.outputPath = EditorGUILayout.TextField(_gcOutputPath, _config.outputPath);
            if (GUILayout.Button(_gcBrowse, GUILayout.Width(30)))
            {
                string p = EditorUtility.SaveFolderPanel("Output Folder", _config.outputPath, "");
                if (!string.IsNullOrEmpty(p)) _config.outputPath = p;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Image Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            if (_config.generatorMode == GeneratorMode.MultiObject)
            {
                _config.totalImages = EditorGUILayout.IntSlider(_gcTotalImages, _config.totalImages, 1, 10000);
            }
            else
            {
                int calculated = _singleObjectConfig.CalculateTotalImages();
                EditorGUILayout.HelpBox($"360° mode will generate {calculated} images based on angle settings", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            _config.imageWidth = EditorGUILayout.IntField(_gcWidth, _config.imageWidth);
            _config.imageHeight = EditorGUILayout.IntField(_gcHeight, _config.imageHeight);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("640×480")) { _config.imageWidth = 640; _config.imageHeight = 480; }
            if (GUILayout.Button("1280×720")) { _config.imageWidth = 1280; _config.imageHeight = 720; }
            if (GUILayout.Button("1920×1080")) { _config.imageWidth = 1920; _config.imageHeight = 1080; }
            EditorGUILayout.EndHorizontal();

            _config.imageFormat = (ImageFormat)EditorGUILayout.EnumPopup(_gcFormat, _config.imageFormat);
            if (_config.imageFormat == ImageFormat.JPG)
                _config.jpgQuality = EditorGUILayout.IntSlider(_gcQuality, _config.jpgQuality, 1, 100);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Annotation Format", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _config.annotationFormat = (AnnotationFormat)EditorGUILayout.EnumPopup(_gcAnnotationFormat, _config.annotationFormat);
            _showFormatDetails = EditorGUILayout.Foldout(_showFormatDetails, "Format Details");
            if (_showFormatDetails)
                EditorGUILayout.HelpBox(GetFormatDescription(_config.annotationFormat), MessageType.None);
            _config.saveVisualization = EditorGUILayout.Toggle(_gcSaveVis, _config.saveVisualization);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Dataset Split", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _config.splitMode = (SplitMode)EditorGUILayout.EnumPopup(_gcSplitMode, _config.splitMode);
            if (_config.splitMode == SplitMode.TrainValTest || _config.splitMode == SplitMode.TrainVal)
            {
                _config.trainRatio = EditorGUILayout.Slider(_gcTrain, _config.trainRatio, 0.5f, 0.9f);
                _config.valRatio = EditorGUILayout.Slider(_gcValid, _config.valRatio, 0.05f, 0.3f);
                if (_config.splitMode == SplitMode.TrainValTest)
                    EditorGUILayout.LabelField($"Test: {_config.TestRatio:P0}");
                DrawSplitBar();
            }
            EditorGUILayout.EndVertical();
        }

        void DrawSplitBar()
        {
            Rect r = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width * _config.trainRatio, r.height), new Color(0.2f, 0.7f, 0.2f));
            EditorGUI.DrawRect(new Rect(r.x + r.width * _config.trainRatio, r.y, r.width * _config.valRatio, r.height), new Color(0.2f, 0.4f, 0.9f));
            if (_config.splitMode == SplitMode.TrainValTest)
                EditorGUI.DrawRect(new Rect(r.x + r.width * (_config.trainRatio + _config.valRatio), r.y, r.width * _config.TestRatio, r.height), new Color(0.9f, 0.4f, 0.2f));
        }

        static string GetFormatDescription(AnnotationFormat f) => f switch
        {
            AnnotationFormat.YOLO => "YOLO: .txt per image, normalized center coords\nCompatible with YOLOv5/v8, Ultralytics",
            AnnotationFormat.COCO => "COCO: Single JSON, absolute pixel coords\nCompatible with Detectron2, MMDetection",
            AnnotationFormat.PascalVOC => "Pascal VOC: .xml per image, corner coords\nCompatible with TensorFlow OD API",
            AnnotationFormat.KITTI => "KITTI: .txt with 2D + 3D info\nFor autonomous driving applications",
            AnnotationFormat.CreateML => "CreateML: JSON for Apple ecosystem\nDeploy to iOS/macOS via Core ML",
            AnnotationFormat.TFRecord => "TFRecord: CSV for TensorFlow conversion\nUse with tf.io.TFRecordWriter",
            AnnotationFormat.All => "Export ALL formats simultaneously",
            _ => ""
        };
        #endregion

        #region === 360 OBJECT TAB ===
        void Draw360ObjectTab()
        {
            EditorGUILayout.LabelField("Target Object", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _singleObjectConfig.targetObject = (GameObject)EditorGUILayout.ObjectField(_gcTargetObject, _singleObjectConfig.targetObject, typeof(GameObject), true);
            _singleObjectConfig.classId = EditorGUILayout.IntField(_gcClassId, _singleObjectConfig.classId);
            _singleObjectConfig.className = EditorGUILayout.TextField(_gcClassName, _singleObjectConfig.className);
            _singleObjectConfig.boxColor = EditorGUILayout.ColorField(_gcBoxColor, _singleObjectConfig.boxColor);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Camera Orbit Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _singleObjectConfig.orbitDistance = EditorGUILayout.FloatField(_gcOrbitDistance, _singleObjectConfig.orbitDistance);
            _singleObjectConfig.lookAtOffset = EditorGUILayout.Vector3Field(_gcLookAtOffset, _singleObjectConfig.lookAtOffset);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Rotation Coverage", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.LabelField("Yaw (Horizontal Rotation)", EditorStyles.miniBoldLabel);
            _singleObjectConfig.angleStepYaw = EditorGUILayout.Slider(_gcAngleStep, _singleObjectConfig.angleStepYaw, 1f, 90f);
            int yawSteps = Mathf.CeilToInt(360f / _singleObjectConfig.angleStepYaw);
            EditorGUILayout.LabelField($"  → {yawSteps} horizontal positions", EditorStyles.miniLabel);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Pitch (Vertical Rotation)", EditorStyles.miniBoldLabel);
            _singleObjectConfig.angleStepPitch = EditorGUILayout.Slider(_gcAngleStep, _singleObjectConfig.angleStepPitch, 1f, 90f);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_gcRange, GUILayout.Width(50));
            _singleObjectConfig.minPitch = EditorGUILayout.FloatField(_singleObjectConfig.minPitch, GUILayout.Width(50));
            EditorGUILayout.MinMaxSlider(ref _singleObjectConfig.minPitch, ref _singleObjectConfig.maxPitch, -90f, 90f);
            _singleObjectConfig.maxPitch = EditorGUILayout.FloatField(_singleObjectConfig.maxPitch, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();

            int pitchSteps = Mathf.CeilToInt((_singleObjectConfig.maxPitch - _singleObjectConfig.minPitch) / _singleObjectConfig.angleStepPitch) + 1;
            EditorGUILayout.LabelField($"  → {pitchSteps} vertical positions ({_singleObjectConfig.minPitch:F0}° to {_singleObjectConfig.maxPitch:F0}°)", EditorStyles.miniLabel);

            EditorGUILayout.Space(5);
            int totalImages = _singleObjectConfig.CalculateTotalImages();
            EditorGUILayout.HelpBox($"Total Images: {yawSteps} × {pitchSteps} = {totalImages}", MessageType.Info);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Capture Method", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _singleObjectConfig.rotateObject = EditorGUILayout.Toggle(_gcRotateObject, _singleObjectConfig.rotateObject);
            EditorGUILayout.HelpBox(_singleObjectConfig.rotateObject
                ? "Object will rotate while camera remains stationary"
                : "Camera will orbit around the stationary object", MessageType.None);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Scale Variation", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _singleObjectConfig.randomizeScale = EditorGUILayout.Toggle(_gcRandomizeScale, _singleObjectConfig.randomizeScale);
            if (_singleObjectConfig.randomizeScale)
            {
                EditorGUILayout.MinMaxSlider(_gcScaleRange, ref _singleObjectConfig.scaleRange.x, ref _singleObjectConfig.scaleRange.y, 0.1f, 3f);
                EditorGUILayout.LabelField($"Range: {_singleObjectConfig.scaleRange.x:F2}x - {_singleObjectConfig.scaleRange.y:F2}x");
            }
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region === CLASSES TAB ===
        void DrawClassesTab()
        {
            EditorGUILayout.LabelField("Object Classes", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Add Class", GUILayout.Height(25)))
            {
                int id = _classes.Count > 0 ? _classes.Max(c => c.classId) + 1 : 0;
                _classes.Add(new ObjectClass { className = $"class_{id}", classId = id, boxColor = DefaultColors[id % DefaultColors.Length] });
            }
            if (GUILayout.Button("Clear", GUILayout.Width(60)))
                if (EditorUtility.DisplayDialog("Clear", "Remove all classes?", "Yes", "No")) _classes.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            _classScroll = EditorGUILayout.BeginScrollView(_classScroll, GUILayout.Height(400));
            for (int i = 0; i < _classes.Count; i++) DrawClassEntry(i);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Classes: {_classes.Count} | Prefabs: {_classes.Sum(c => c.prefabs.Count(p => p != null))}");
            EditorGUILayout.EndVertical();
        }

        void DrawClassEntry(int i)
        {
            var c = _classes[i];
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18)), c.boxColor);
            c.className = EditorGUILayout.TextField(c.className, GUILayout.Width(100));
            EditorGUILayout.LabelField("ID:", GUILayout.Width(20));
            c.classId = EditorGUILayout.IntField(c.classId, GUILayout.Width(35));
            c.boxColor = EditorGUILayout.ColorField(c.boxColor, GUILayout.Width(50));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(22))) { _classes.RemoveAt(i); return; }
            EditorGUILayout.EndHorizontal();

            if (_config.annotationFormat == AnnotationFormat.KITTI || _config.annotationFormat == AnnotationFormat.All)
            {
                int ki = System.Array.IndexOf(KittiTypes, c.kittiType);
                c.kittiType = KittiTypes[EditorGUILayout.Popup("KITTI Type", ki < 0 ? 0 : ki, KittiTypes)];
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Spawn:", GUILayout.Width(45));
            c.minCount = EditorGUILayout.IntField(c.minCount, GUILayout.Width(35));
            EditorGUILayout.LabelField("-", GUILayout.Width(10));
            c.maxCount = EditorGUILayout.IntField(c.maxCount, GUILayout.Width(35));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Prefabs ({c.prefabs.Count}):", GUILayout.Width(90));
            if (GUILayout.Button("+", GUILayout.Width(22))) c.prefabs.Add(null);
            EditorGUILayout.EndHorizontal();

            for (int p = 0; p < c.prefabs.Count; p++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(15);
                c.prefabs[p] = (GameObject)EditorGUILayout.ObjectField(c.prefabs[p], typeof(GameObject), false);
                if (GUILayout.Button("×", GUILayout.Width(20))) { c.prefabs.RemoveAt(p); break; }
                EditorGUILayout.EndHorizontal();
            }

            Rect drop = GUILayoutUtility.GetRect(0, 25, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drop Prefabs Here", EditorStyles.helpBox);
            if (drop.Contains(Event.current.mousePosition))
            {
                if (Event.current.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var o in DragAndDrop.objectReferences)
                        if (o is GameObject go) c.prefabs.Add(go);
                    Event.current.Use();
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }
        #endregion

        #region === SPAWN TAB ===
        void DrawSpawnTab()
        {
            EditorGUILayout.LabelField("Scene References", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _camera = (Camera)EditorGUILayout.ObjectField(_gcCamera, _camera, typeof(Camera), true);
            _mainLight = (Light)EditorGUILayout.ObjectField(_gcMainLight, _mainLight, typeof(Light), true);
            if (!_camera)
                EditorGUILayout.HelpBox(_camera == null && Camera.main ? "Using Main Camera" : "Assign a camera!",
                    _camera == null && Camera.main ? MessageType.Info : MessageType.Warning);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Spawn Area", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _spawnCenter = EditorGUILayout.Vector3Field(_gcCenter, _spawnCenter);
            _spawnSize = EditorGUILayout.Vector3Field(_gcSize, _spawnSize);
            _minSeparation = EditorGUILayout.Slider(_gcMinSeparation, _minSeparation, 0.1f, 5f);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Randomization", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _randomRotation = EditorGUILayout.Toggle(_gcRandomYRot, _randomRotation);
            _randomScale = EditorGUILayout.Toggle(_gcRandomScale, _randomScale);
            if (_randomScale)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(_gcScaleRange, GUILayout.Width(100));
                _scaleRange.x = EditorGUILayout.FloatField(_scaleRange.x, GUILayout.Width(50));
                EditorGUILayout.MinMaxSlider(ref _scaleRange.x, ref _scaleRange.y, 0.1f, 3f);
                _scaleRange.y = EditorGUILayout.FloatField(_scaleRange.y, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Visibility Filters", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _config.minVisibility = EditorGUILayout.Slider(_gcMinVisibility, _config.minVisibility, 0f, 1f);
            _config.includeTruncated = EditorGUILayout.Toggle(_gcIncludeTrunc, _config.includeTruncated);
            _config.includeOccluded = EditorGUILayout.Toggle(_gcIncludeOccl, _config.includeOccluded);
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region === AUGMENTATION TAB ===
        void DrawAugmentationTab()
        {
            _augScroll = EditorGUILayout.BeginScrollView(_augScroll);

            DrawExtraAugmentations();
            EditorGUILayout.Space(10);
            DrawLightingAugmentations();
            EditorGUILayout.Space(5);
            DrawDOFAugmentations();
            EditorGUILayout.Space(5);
            DrawChromaticAberration();
            EditorGUILayout.Space(5);
            DrawColorGrading();
            EditorGUILayout.Space(5);
            DrawBackgroundAugmentation();

            EditorGUILayout.EndScrollView();
        }

        void DrawExtraAugmentations()
        {
            EditorGUILayout.LabelField("Extra Augmentations", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _augmentation.applyGaussianNoise = EditorGUILayout.Toggle("Apply Gaussian Noise", _augmentation.applyGaussianNoise);
            _augmentation.applyMotionBlur = EditorGUILayout.Toggle("Apply Motion Blur", _augmentation.applyMotionBlur);
            if (_augmentation.applyGaussianNoise)
                _augmentation.noiseSigma = EditorGUILayout.Slider("Noise Sigma", _augmentation.noiseSigma, 0f, 0.2f);
            if (_augmentation.applyMotionBlur)
                _augmentation.blurRadius = EditorGUILayout.IntSlider("Blur Radius", _augmentation.blurRadius, 1, 10);
            EditorGUILayout.EndVertical();
        }

        void DrawLightingAugmentations()
        {
            EditorGUILayout.LabelField("Randomization Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _augmentation.randomizeLighting = EditorGUILayout.Toggle("Randomize Lighting", _augmentation.randomizeLighting);
            if (_augmentation.randomizeLighting)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Lighting Settings", EditorStyles.miniBoldLabel);
                DrawMinMaxField("Intensity", ref _augmentation.intensityMin, ref _augmentation.intensityMax, 0f, 4f);
                _augmentation.randomizeLightColor = EditorGUILayout.Toggle("Randomize Color", _augmentation.randomizeLightColor);
                if (_augmentation.randomizeLightColor)
                {
                    EditorGUILayout.BeginHorizontal();
                    _augmentation.lightColorMin = EditorGUILayout.ColorField("Min", _augmentation.lightColorMin);
                    _augmentation.lightColorMax = EditorGUILayout.ColorField("Max", _augmentation.lightColorMax);
                    EditorGUILayout.EndHorizontal();
                }
                _augmentation.randomizeLightAngle = EditorGUILayout.Toggle("Randomize Angle", _augmentation.randomizeLightAngle);
                if (_augmentation.randomizeLightAngle)
                    _augmentation.lightAngleRange = EditorGUILayout.Slider("Angle Range", _augmentation.lightAngleRange, 0f, 180f);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        void DrawDOFAugmentations()
        {
            EditorGUILayout.BeginVertical("box");

            string dofLabel = $"Randomize Depth of Field ({_detectedPipeline})";
            _augmentation.randomizeDepthOfField = EditorGUILayout.Toggle(dofLabel, _augmentation.randomizeDepthOfField);

            if (_augmentation.randomizeDepthOfField)
            {
                EditorGUI.indentLevel++;

                if (!_isDOFSupported)
                {
                    string defineSymbol = _detectedPipeline == "URP" ? "UNITY_PIPELINE_URP" : "UNITY_PIPELINE_HDRP";
                    EditorGUILayout.HelpBox(
                        $"{_detectedPipeline} detected but {defineSymbol} is not defined.\n" +
                        $"Add '{defineSymbol}' to Scripting Define Symbols in Player Settings.",
                        MessageType.Error);

                    if (GUILayout.Button("Open Player Settings"))
                        SettingsService.OpenProjectSettings("Project/Player");
                }
                else
                {
                    _augmentation.postProcessVolume = (Volume)EditorGUILayout.ObjectField(
                        "Post Process Volume", _augmentation.postProcessVolume, typeof(Volume), true);

                    if (_augmentation.postProcessVolume == null)
                    {
                        EditorGUILayout.HelpBox("Assign a Volume with Depth of Field override.", MessageType.Warning);
                    }
                    else if (_augmentation.postProcessVolume.profile == null)
                    {
                        EditorGUILayout.HelpBox("The assigned Volume has no profile!", MessageType.Error);
                    }
                    else
                    {
#if UNITY_PIPELINE_URP
                        EditorGUILayout.HelpBox(
                            "URP DOF Mode: Bokeh\n" +
                            "• Ensure camera has Post Processing enabled\n" +
                            "• Ensure URP Asset has Post Processing enabled",
                            MessageType.Info);
#elif UNITY_PIPELINE_HDRP
                        EditorGUILayout.HelpBox(
                            "HDRP DOF Mode: Manual\n" +
                            "• Physical Camera properties will be modified\n" +
                            "• Near/Far focus ranges auto-calculated from focus distance",
                            MessageType.Info);
#endif
                    }

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("DOF Parameters", EditorStyles.miniBoldLabel);
                    DrawMinMaxField("Focus Distance", ref _augmentation.focusDistanceMin, ref _augmentation.focusDistanceMax, 0.1f, 100f);
                    DrawMinMaxField("Aperture (f-stop)", ref _augmentation.apertureMin, ref _augmentation.apertureMax, 1f, 32f);
                    DrawMinMaxField("Focal Length (mm)", ref _augmentation.focalLengthMin, ref _augmentation.focalLengthMax, 10f, 300f);
                }

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        void DrawChromaticAberration()
        {
            EditorGUILayout.BeginVertical("box");
            _augmentation.randomizeChromaticAberration = EditorGUILayout.Toggle("Randomize Chromatic Aberration (CPU)", _augmentation.randomizeChromaticAberration);
            if (_augmentation.randomizeChromaticAberration)
            {
                EditorGUI.indentLevel++;
                DrawMinMaxField("Offset (pixels)", ref _augmentation.aberrationOffsetMin, ref _augmentation.aberrationOffsetMax, 0f, 100f);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        void DrawColorGrading()
        {
            EditorGUILayout.BeginVertical("box");
            _augmentation.randomizeColorGrading = EditorGUILayout.Toggle("Randomize Color Grading (CPU)", _augmentation.randomizeColorGrading);
            if (_augmentation.randomizeColorGrading)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Color Grading Settings", EditorStyles.miniBoldLabel);
                DrawMinMaxField("Hue Shift", ref _augmentation.hueShiftMin, ref _augmentation.hueShiftMax, -180f, 180f);
                DrawMinMaxField("Saturation", ref _augmentation.saturationMin, ref _augmentation.saturationMax, -100f, 100f);
                DrawMinMaxField("Contrast", ref _augmentation.contrastMin, ref _augmentation.contrastMax, -100f, 100f);
                DrawMinMaxField("Exposure", ref _augmentation.exposureMin, ref _augmentation.exposureMax, -5f, 5f);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        void DrawBackgroundAugmentation()
        {
            EditorGUILayout.BeginVertical("box");
            _augmentation.randomizeBackground = EditorGUILayout.Toggle("Randomize Background", _augmentation.randomizeBackground);
            if (_augmentation.randomizeBackground)
            {
                EditorGUI.indentLevel++;
                _augmentation.backgroundPlane = (GameObject)EditorGUILayout.ObjectField(
                    "Background Plane", _augmentation.backgroundPlane, typeof(GameObject), true);
                EditorGUILayout.HelpBox("Assign background colors and materials via Generator component inspector", MessageType.Info);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        void DrawMinMaxField(string label, ref float min, ref float max, float minLimit, float maxLimit)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            min = EditorGUILayout.FloatField(min, GUILayout.Width(50));
            EditorGUILayout.MinMaxSlider(ref min, ref max, minLimit, maxLimit);
            max = EditorGUILayout.FloatField(max, GUILayout.Width(50));
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region === SEGMENTATION TAB ===
        void DrawInstanceSegmentationTab()
        {
            _segScroll = EditorGUILayout.BeginScrollView(_segScroll);

            EditorGUILayout.LabelField("Segmentation & Depth Outputs", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Enable additional output types for training segmentation models and depth estimation networks.",
                MessageType.Info);

            EditorGUILayout.Space(5);

            // Binary Mask
            EditorGUILayout.LabelField("Binary Mask", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _config.generateBinaryMask = EditorGUILayout.Toggle(
                "Generate Binary Mask",
                _config.generateBinaryMask);

            if (_config.generateBinaryMask)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Simple foreground/background separation.\n" +
                    "• All spawned objects = White (255, 255, 255)\n" +
                    "• Background/everything else = Black (0, 0, 0)\n" +
                    "• Useful for background removal and matting\n" +
                    "• Output: PNG masks in 'masks/' folder",
                    MessageType.None);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Instance Segmentation
            EditorGUILayout.LabelField("Instance Segmentation", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _config.generateInstanceSegmentation = EditorGUILayout.Toggle(
                "Generate Instance Segmentation",
                _config.generateInstanceSegmentation);

            if (_config.generateInstanceSegmentation)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Each object instance gets a unique RGB color.\n" +
                    "Output: PNG masks in 'instance_segmentation/' folder",
                    MessageType.None);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Semantic Segmentation
            EditorGUILayout.LabelField("Semantic Segmentation", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _config.generateSemanticSegmentation = EditorGUILayout.Toggle(
                "Generate Semantic Segmentation",
                _config.generateSemanticSegmentation);

            if (_config.generateSemanticSegmentation)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "All objects of the same class share one color.\n" +
                    "• Colors generated using golden ratio for distinction\n" +
                    "• Same class = same color across all instances\n" +
                    "• Output: PNG masks in 'semantic_segmentation/' folder",
                    MessageType.None);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            // Panoptic Segmentation
            EditorGUILayout.LabelField("Panoptic Segmentation", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _config.generatePanopticSegmentation = EditorGUILayout.Toggle(
                "Generate Panoptic Segmentation",
                _config.generatePanopticSegmentation);

            if (_config.generatePanopticSegmentation)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Combines instance and semantic segmentation.\n" +
                    "• Base color from class (like semantic)\n" +
                    "• Slight brightness variation per instance\n" +
                    "• Balances class grouping with instance distinction\n" +
                    "• Output: PNG masks in 'panoptic_segmentation/' folder",
                    MessageType.None);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Depth Map
            EditorGUILayout.LabelField("Depth Map", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _config.generateDepthMap = EditorGUILayout.Toggle(
                "Generate Depth Maps",
                _config.generateDepthMap);

            if (_config.generateDepthMap)
            {
                EditorGUI.indentLevel++;
                _config.depthMaxDistance = EditorGUILayout.Slider(
                    "Max Distance (auto-calculated)",
                    _config.depthMaxDistance,
                    0.1f,
                    1000f);
                _config.depthLinear = EditorGUILayout.Toggle(
                    "Linear Depth",
                    _config.depthLinear);
                _config.maskDepthToObjects = EditorGUILayout.Toggle(
                    "Mask to Objects Only",
                    _config.maskDepthToObjects);

                EditorGUILayout.HelpBox(
                    "Grayscale depth images:\n" +
                    "• Near objects = Bright/White\n" +
                    "• Far objects = Dark/Black\n" +
                    (_config.maskDepthToObjects ? "• Background = Pure Black (masked)\n" : "• Background = Depth gradient\n") +
                    "• Auto-calculates optimal range from spawned objects\n" +
                    $"• Fallback max distance: {_config.depthMaxDistance:F1} units\n" +
                    "• Output: PNG in 'depth/' folder",
                    MessageType.None);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // Summary
            _showSegmentationInfo = EditorGUILayout.Foldout(_showSegmentationInfo, "Output Summary");
            if (_showSegmentationInfo)
            {
                EditorGUILayout.BeginVertical("box");
                int enabledCount = 0;
                if (_config.generateInstanceSegmentation) enabledCount++;
                if (_config.generateSemanticSegmentation) enabledCount++;
                if (_config.generatePanopticSegmentation) enabledCount++;
                if (_config.generateDepthMap) enabledCount++;

                EditorGUILayout.LabelField($"Enabled outputs: {enabledCount}/4");

                if (enabledCount > 0)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Will generate:", EditorStyles.miniBoldLabel);
                    if (_config.generateInstanceSegmentation)
                        EditorGUILayout.LabelField("  • Instance segmentation (unique per object)");
                    if (_config.generateSemanticSegmentation)
                        EditorGUILayout.LabelField("  • Semantic segmentation (same per class)");
                    if (_config.generatePanopticSegmentation)
                        EditorGUILayout.LabelField("  • Panoptic segmentation (class + instance)");
                    if (_config.generateDepthMap)
                        EditorGUILayout.LabelField("  • Depth maps");
                }
                else
                {
                    EditorGUILayout.HelpBox("No additional outputs enabled. Only RGB images and bounding box annotations will be generated.", MessageType.Info);
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(10);

            // Shader Requirements
            EditorGUILayout.LabelField("Shader Requirements", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            bool instanceShaderFound = Shader.Find("Hidden/SyntheticDatasetGenerator/InstanceColor") != null;
            bool depthShaderFound = Shader.Find("Hidden/SyntheticDatasetGenerator/DepthFromCamera") != null;

            DrawCheck("Instance Color Shader", instanceShaderFound);
            DrawCheck("Depth From Camera Shader", depthShaderFound);

            if (!instanceShaderFound)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Missing instance shader! Ensure this file exists:\n" +
                    "• Assets/SyntheticDataset/Shaders/InstanceColor.shader\n\n" +
                    "This shader is used for instance, semantic, AND panoptic segmentation.",
                    MessageType.Error);
            }

            if (!depthShaderFound && _config.generateDepthMap)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "Missing depth shader! Ensure this file exists:\n" +
                    "• Assets/SyntheticDataset/Shaders/DepthFromCamera.shader",
                    MessageType.Error);
            }

            if (instanceShaderFound)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "✓ Segmentation ready\n" +
                    "• Uses GL immediate mode rendering\n" +
                    "• Instance: Unique colors per object\n" +
                    "• Semantic: Same color per class\n" +
                    "• Panoptic: Class color + instance variation",
                    MessageType.Info);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region === GENERATE TAB ===
        void DrawGenerateTab()
        {
            EditorGUILayout.LabelField("Checklist", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            bool hasCamera = _camera || Camera.main;
            bool hasClasses = _config.generatorMode == GeneratorMode.SingleObject360
                ? _singleObjectConfig.targetObject != null
                : _classes.Count > 0 && _classes.Any(c => c.prefabs.Any(p => p));
            bool hasOutput = !string.IsNullOrEmpty(_config.outputPath);

            DrawCheck("Camera", hasCamera);
            DrawCheck(_config.generatorMode == GeneratorMode.SingleObject360 ? "Target Object" : "Classes & Prefabs", hasClasses);
            DrawCheck("Output Path", hasOutput);

            if (_augmentation.randomizeDepthOfField)
            {
                bool hasDOF = _isDOFSupported &&
                              _augmentation.postProcessVolume != null &&
                              _augmentation.postProcessVolume.profile != null;
                DrawCheck("DOF Volume", hasDOF);
            }

            // Segmentation & Depth shader checks
            if (_config.generateInstanceSegmentation)
            {
                bool shaderOk = Shader.Find("Hidden/SyntheticDatasetGenerator/InstanceColor") != null;
                DrawCheck("Instance Color Shader", shaderOk);
            }

            if (_config.generateDepthMap)
            {
                bool depthShaderOk = Shader.Find("Hidden/SyntheticDatasetGenerator/DepthFromCamera") != null;
                DrawCheck("Depth Shader", depthShaderOk);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"Mode: {_config.generatorMode}");
            EditorGUILayout.LabelField($"Format: {_config.annotationFormat}");
            EditorGUILayout.LabelField($"Resolution: {_config.imageWidth}×{_config.imageHeight}");
            EditorGUILayout.LabelField($"Pipeline: {_detectedPipeline}");
            EditorGUILayout.LabelField(_config.generatorMode == GeneratorMode.SingleObject360
                ? $"Images: ~{_singleObjectConfig.CalculateTotalImages()} (from angle coverage)"
                : $"Images: {_config.totalImages}");

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Generator", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _activeGenerator = (MultiFormatDatasetGenerator)EditorGUILayout.ObjectField(
                "Generator", _activeGenerator, typeof(MultiFormatDatasetGenerator), true);

            if (!_activeGenerator)
            {
                if (GUILayout.Button("Create Generator in Scene", GUILayout.Height(30)))
                {
                    var go = new GameObject(_config.generatorMode == GeneratorMode.SingleObject360
                        ? "360Generator" : "DatasetGenerator");
                    _activeGenerator = go.AddComponent<MultiFormatDatasetGenerator>();
                    ApplySettings();
                    Selection.activeGameObject = go;
                    SetStatus("Generator created!", MessageType.Info);
                }
            }
            else
            {
                if (GUILayout.Button("Update Settings"))
                {
                    ApplySettings();
                    SetStatus("Updated!", MessageType.Info);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginVertical("box");

            bool canGo = hasCamera && hasClasses && hasOutput && Application.isPlaying && _activeGenerator;

            GUI.enabled = canGo && !(_activeGenerator?.isGenerating ?? false);
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            if (GUILayout.Button("▶ START GENERATION", GUILayout.Height(40)))
            {
                _activeGenerator.StartGeneration();
                SetStatus("Started...", MessageType.Info);
            }
            GUI.backgroundColor = Color.white;

            GUI.enabled = _activeGenerator?.isGenerating ?? false;
            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.2f);
            if (GUILayout.Button("■ STOP", GUILayout.Height(25)))
            {
                _activeGenerator.StopGeneration();
                SetStatus("Stopped", MessageType.Warning);
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to generate", MessageType.Warning);
                if (GUILayout.Button("Enter Play Mode", GUILayout.Height(25)))
                    EditorApplication.isPlaying = true;
            }

            if (_activeGenerator?.isGenerating ?? false)
            {
                int total = _config.generatorMode == GeneratorMode.SingleObject360
                    ? _singleObjectConfig.CalculateTotalImages()
                    : _config.totalImages;
                float p = (float)_activeGenerator.currentImageIndex / total;
                EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true)), p,
                    $"{_activeGenerator.currentImageIndex}/{total}");
            }
            EditorGUILayout.EndVertical();

            if (!string.IsNullOrEmpty(_config.outputPath))
            {
                EditorGUILayout.Space(5);
                if (GUILayout.Button("Open Output Folder"))
                    if (Directory.Exists(_config.outputPath))
                        EditorUtility.RevealInFinder(_config.outputPath);
            }
        }

        void DrawCheck(string label, bool ok)
        {
            EditorGUILayout.BeginHorizontal();
            var style = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = ok ? Color.green : Color.red },
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField(ok ? "✓" : "✗", style, GUILayout.Width(18));
            EditorGUILayout.LabelField(label);
            EditorGUILayout.EndHorizontal();
        }

        void ApplySettings()
        {
            if (!_activeGenerator) return;
            _activeGenerator.config = _config;
            _activeGenerator.classes = new List<ObjectClass>(_classes);
            _activeGenerator.augmentation = _augmentation;
            _activeGenerator.singleObjectConfig = _singleObjectConfig;
            _activeGenerator.captureCamera = _camera ? _camera : Camera.main;
            _activeGenerator.mainLight = _mainLight ? _mainLight : FindAnyObjectByType<Light>();
            _activeGenerator.spawnAreaCenter = _spawnCenter;
            _activeGenerator.spawnAreaSize = _spawnSize;
            _activeGenerator.minObjectSeparation = _minSeparation;
            _activeGenerator.randomizeRotation = _randomRotation;
            _activeGenerator.randomizeScale = _randomScale;
            _activeGenerator.scaleRange = _scaleRange;
            EditorUtility.SetDirty(_activeGenerator);
        }
        #endregion

        #region === STATUS ===
        void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.HelpBox(_status, _statusType);
            }
        }

        void SetStatus(string msg, MessageType type)
        {
            _status = msg;
            _statusType = type;
            Repaint();
        }
        #endregion
    }
}
