# Interfaz Unity AN5

A Unity-based operator interface for the **FR5v6 6-DOF robot arm**, developed at the Universidad del Cauca (GIA research group). It connects to a live robot over ROS2/TCP and provides real-time joint and Cartesian control, trajectory management, and a 3D visualization that mirrors the robot's actual pose.

## Features

### Panel Principal — joint & Cartesian control
- **6-axis joint control**: individual sliders and +/− buttons for each joint (BASE, SHOULDER, ELBOW, WRIST 1, WRIST 2, WRIST 3), with live degree readouts.
- **Cartesian position display**: reads X, Y, Z, Rx, Ry, Rz from the robot via ROS2 and shows them in real time.
- **End-effector tracking**: the world position of `j6_link` is computed and displayed continuously.

### Panel Trayectorias — trajectory recording & playback
- **Point queue**: capture the current joint configuration as a waypoint, build a sequence, preview each point's Cartesian equivalent (via inverse kinematics), and send the full queue to the robot as a spline trajectory.
- **File-based execution**: load a plain-text trajectory file (one command per line) and execute it with pause/stop/progress controls.
- **Export**: save the current queue to a timestamped `.txt` log file.

### Panel Monitoreo — live 3D visualization
- The **FR5v6 URDF model** is animated in real time from incoming joint position data, giving a live 3D view of the robot's pose.
- **Multi-camera**: three cameras cover different angles; switch between them with keys 1, 2, 3.
- **Orbit and pan**: WASD + mouse drag to orbit the view, arrow keys + right-click drag to pan, scroll wheel or slider to zoom.

### Persistent UI
- **Status pills** in the header show ROS2 connection and robot connection state at a glance.
- **Screen recorder**: captures the Game View to a Motion-JPEG AVI file (saved to `recordings/`).

### Architecture
- Communicates with the robot controller over **ROS2 TCP** using the Unity Robotics ROS-TCP-Connector package.
- The URDF model was imported with the Unity Robotics URDF Importer.
- All runtime scripts are pure C# targeting .NET Standard; no native platform code except the OS file-picker dialogs.

## Requirements

### All platforms

- Unity 6 (or the version used to open this project)
- ROS2 (Humble or newer) running on the robot or a connected machine
- The ROS-TCP endpoint running before launching the interface:
  ```
  ros2 run ros_tcp_endpoint default_server_endpoint
  ```

### Linux

**Vulkan drivers** (required for 3D rendering):

```bash
# Ubuntu/Debian — Mesa (AMD/Intel)
sudo apt install libvulkan1 mesa-vulkan-drivers

# Ubuntu/Debian — NVIDIA proprietary
sudo apt install nvidia-driver-<version>   # e.g. nvidia-driver-535
```

**File dialog tool** (required for the trajectory file picker in the Trayectorias panel):

```bash
# GNOME / Ubuntu / most distros
sudo apt install zenity

# KDE Plasma
sudo apt install kdialog
```

> If neither tool is installed, the file picker silently fails. As a fallback, type the full file path directly into the filename input field before pressing CARGAR.

**ROS2** (Ubuntu 22.04 example):

```bash
sudo apt install ros-humble-desktop
sudo apt install ros-humble-ros-tcp-endpoint   # or build from source
```

### Windows

No extra dependencies beyond Unity. The trajectory file picker uses the built-in PowerShell + WinForms dialog.

### macOS

No extra dependencies. The file picker uses `osascript`.

### Git LFS

Meshes, textures, and other large binaries in this project are tracked with **Git
LFS** (see `.gitattributes` at the repo root and here). Install it **before**
cloning, otherwise those assets download as small text pointer files instead of
real content — Unity will then fail to import them and flood the Console with
unrelated-looking errors (this is exactly what happened porting this project to a
new machine without `git-lfs` set up):

```bash
sudo apt install git-lfs   # or your OS's installer
git lfs install
git clone git@github.com:MooZ91/Project_AN5.git
```

Already cloned without it? Run `git lfs install && git lfs pull` from the repo root
to fetch the real files.

As a safety net, `Assets/Editor/GitLfsCheck.cs` runs automatically the first time
the project is opened in this Editor session: it scans for LFS-tracked files that
are still pointer text, warns in the Console, and offers to run `git lfs pull` for
you. Re-run it manually anytime via **Tools > Git LFS > Check for missing LFS
files**.

## Getting started

1. Clone the repository and open it in Unity.
2. Start the ROS-TCP endpoint on the robot side.
3. Enter Play mode — the interface connects automatically to the configured IP and port.

## Project structure

```
Assets/
  Scripts/       Runtime C# scripts
  Editor/        Editor-only tools (not included in builds)
  Scenes/        Unity scenes
  Plugins/       NuGet DLLs (SignalR, Roslyn, etc.)
ProjectSettings/ Unity project settings
```
