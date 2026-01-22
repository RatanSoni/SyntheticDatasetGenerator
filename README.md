# Unity Synthetic Dataset Generator

A comprehensive Unity Editor tool for generating synthetic datasets for computer vision and machine learning applications. Supports multiple annotation formats, segmentation masks, depth maps, and extensive augmentation options.

![Unity Version](https://img.shields.io/badge/Unity-2021.3%2B-blue)
![License](https://img.shields.io/badge/License-MIT-green)

## Features

### Annotation Formats
- **YOLO** - Compatible with YOLOv5/v8, Ultralytics
- **COCO** - Compatible with Detectron2, MMDetection
- **Pascal VOC** - Compatible with TensorFlow Object Detection API
- **KITTI** - For autonomous driving applications
- **CreateML** - For Apple ecosystem (iOS/macOS via Core ML)
- **TFRecord** - CSV format for TensorFlow conversion
- **All** - Export all formats simultaneously

### Segmentation Outputs
- **Binary Mask** - Simple foreground/background separation
- **Instance Segmentation** - Unique color per object instance
- **Semantic Segmentation** - Same color per object class
- **Panoptic Segmentation** - Combined instance + semantic masks
- **Depth Maps** - Grayscale depth images with auto-calculated range

### Generation Modes
- **Multi-Object Mode** - Spawn multiple random objects per image with configurable spawn areas
- **Single Object 360°** - Capture one object from all viewing angles for complete coverage

### Augmentations
- Lighting randomization (intensity, color, angle)
- Depth of Field (URP/HDRP)
- Gaussian noise
- Motion blur
- Chromatic aberration
- Color grading (hue, saturation, contrast, exposure)
- Background randomization

## Installation

### Option 1: Unity Package Manager (Git URL)
1. Open Unity Package Manager (`Window > Package Manager`)
2. Click `+` → `Add package from git URL`
3. Enter: `https://github.com/RatanSoni/SyntheticDatasetGenerator.git`

### Option 2: Manual Installation
1. Download or clone this repository
2. Copy the `SyntheticDatasetGenerator` folder into your project's `Assets` folder

### Render Pipeline Setup (Required for DOF)

For **URP** projects, add to `Project Settings > Player > Scripting Define Symbols`:
```
UNITY_PIPELINE_URP
```

For **HDRP** projects, add:
```
UNITY_PIPELINE_HDRP
```

## Quick Start

### 1. Open the Tool
`Tools > Multi-Format Dataset Generator`

### 2. Setup Tab
- Set **Dataset Name** and **Output Path**
- Configure image resolution (presets: 640×480, 1280×720, 1920×1080)
- Select **Annotation Format**
- Configure train/validation/test split ratios

### 3. Classes Tab (Multi-Object Mode)
- Click `+ Add Class` to create object classes
- Assign prefabs by dragging into the drop zone or clicking `+`
- Set spawn count range (min/max per image)
- Configure KITTI type if using KITTI format

### 4. Spawn Tab
- Assign **Camera** (uses Main Camera if not set)
- Assign **Main Light** for lighting randomization
- Define spawn area center and size
- Set minimum object separation distance

### 5. Augmentation Tab
- Enable desired augmentations
- For DOF: Assign a Volume with Depth of Field override

### 6. Segmentation Tab
- Enable desired segmentation outputs
- Configure depth map settings if enabled

### 7. Generate Tab
- Verify checklist (green checkmarks)
- Click `Create Generator in Scene`
- Enter Play Mode
- Click `▶ START GENERATION`

## Folder Structure

```
OutputPath/
├── DatasetName_timestamp/
│   ├── train/
│   │   ├── images/
│   │   ├── labels/
│   │   ├── instance_segmentation/  (if enabled)
│   │   ├── semantic_segmentation/  (if enabled)
│   │   ├── panoptic_segmentation/  (if enabled)
│   │   ├── masks/                  (if enabled)
│   │   └── depth/                  (if enabled)
│   ├── valid/
│   │   └── ...
│   ├── test/
│   │   └── ...
│   ├── annotations/
│   │   ├── instances_coco.json
│   │   ├── annotations_createml.json
│   │   └── annotations_tfrecord.csv
│   ├── visualizations/             (if enabled)
│   ├── data.yaml                   (YOLO config)
│   ├── classes.txt
│   └── dataset_info.txt
```

## 360° Single Object Mode

Perfect for creating datasets of individual objects from all angles:

1. Switch to `Single Object 360°` mode
2. Assign your target object
3. Configure orbit distance and look-at offset
4. Set yaw/pitch angle steps and ranges
5. Optionally enable scale randomization

The tool calculates total images automatically based on angle coverage.

## Requirements

- Unity 2021.3 or higher
- For DOF augmentation: URP or HDRP with appropriate define symbols
- Shaders included in package (auto-detected)

## Shaders

The tool includes custom shaders for:
- `Hidden/SyntheticDatasetGenerator/InstanceColor` - Segmentation rendering
- `Hidden/SyntheticDatasetGenerator/DepthFromCamera` - Depth map generation
- `Hidden/SyntheticDatasetGenerator/ImageAugmentation` - GPU-accelerated augmentations

## License

MIT License - See [LICENSE](LICENSE) for details.

## Contributing

Contributions welcome! Please open an issue or submit a pull request.

## Acknowledgments

Built for synthetic data generation in computer vision research and ML model training.
