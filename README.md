MultipleInstantPipes: High-Performance Procedural Pipe Routing for Unity

MultipleInstantPipes is a high-performance Unity asset for procedurally generating complex 3D pipe networks. Based on user-defined start points, endpoints, and environmental obstacles, it automatically calculates optimal routing paths.

The core pathfinding logic is handled by a high-performance DLL written in C++, ensuring fast and efficient computation even in large-scale, complex environments. This system is ideal for applications in industrial plant simulations, game level design, architectural visualization, and more.

✨ Key Features

    Automated Multi-Pipe Routing: Simultaneously calculates paths for multiple pipes.

    Intelligent Collision Avoidance: Dynamically adjusts pathfinding costs to ensure pipes do not collide with each other, based on a Decomposition Heuristic approach.

    Realistic Piping Constraints:

        Minimum Bend Distance: Prevents overly frequent bends by enforcing a minimum straight distance between elbows.

        Pipe Radius & Clearance: Considers the physical thickness of pipes and an additional safety clearance space in all calculations.

    Target Distance Maintenance: A sophisticated pathfinding goal that encourages pipes to maintain a precise, user-defined distance from obstacles (radius + clearance), rather than simply staying as far away as possible.

    High-Performance C++ Backend: Heavy computations are offloaded to a native C++ DLL, minimizing impact on Unity's performance.

    Customizable Cost Weights: Fine-tune pathfinding behavior by adjusting weights for path length, bends, vertical travel, and target distance adherence.

    Procedural Mesh Generation: Instantly generates render-ready 3D pipe meshes from the calculated paths.

⚙️ System Architecture

The system consists of two primary components that work in tandem:

    Unity C# Frontend (PipeGenerator.cs)

        Acts as the interface within the Unity Editor.

        Collects and manages scene data, including obstacles and pipe start/end points.

        Passes user settings to the C++ DLL and requests pathfinding operations.

        Receives the calculated path data (a list of coordinates) from the DLL and uses it to generate and render the pipe meshes.

    C++ Backend (pathfinder.dll)

        The core engine that performs the actual pathfinding calculations.

        Features a sophisticated A* algorithm inspired by the provided Python analysis.

        Utilizes a pre-calculated Distance Transform map. This allows the engine to instantly know the distance to the nearest obstacle from any point, making the "Target Distance Maintenance" logic extremely efficient.

+---------------------------------+      <-- DllImport -->      +--------------------------+
|      Unity Engine (C#)          |                             |   Pathfinding DLL (C++)  |
|---------------------------------|                             |--------------------------|
| - PipeGenerator.cs              |      1. Request Pathfinding   | - A* Algorithm           |
| - Scene Data Management         |     (Pass Parameters)       | - Distance Transform Map |
| - Pipe Mesh Generation          |---------------------------> | - Collision Cost Logic   |
|                                 |      2. Return Path Result    | - Optimal Path Calculation|
|                                 |      (Coordinate Array)     |                          |
+---------------------------------+      <---------------------------+--------------------------+

📚 How to Use

1. Installation

    Clone or download this GitHub repository and import it into your Unity project.

    Build the C++ project to produce pathfinder.dll. Place the generated DLL file inside the Assets/Plugins folder in your Unity project. (Create the folder if it doesn't exist).

2. Basic Setup

    Create an empty GameObject in your Unity scene.

    Add the PipeGenerator.cs script as a component to this GameObject.

    Configure the properties in the PipeGenerator component via the Inspector window.

        Grid Settings: Define the bounds and resolution of the pathfinding grid.

        Pathfinding Parameters: Set the cost weights (w_path, w_bend, w_target_distance, etc.) to influence pathfinding behavior.

        Pipes List: Manage the list of pipes you want to generate. You can set the start/end points and radius for each pipe here.

3. Usage from Scripts

The PipeGenerator.cs provides public methods that can be called from other scripts.
C#

// Get a reference to the PipeGenerator component.
PipeGenerator pipeGenerator = FindObjectOfType<PipeGenerator>();

// 1. Initialize the pathfinding system.
// This should be called once, e.g., at the start of the scene.
// It builds the grid and pre-computes the obstacle distance transform.
pipeGenerator.InitializePathfinding();

// 2. Add a new pipe to be routed.
pipeGenerator.AddPipe(startPosition, endPosition, radius);

// 3. Calculate paths for all pipes and generate the meshes.
// This function iteratively calls the C++ DLL to find paths and resolve collisions.
pipeGenerator.GenerateAllPipes();

🔧 Building the C++ DLL

If you need to modify and recompile the pathfinder.cpp source file:

    Environment: You will need a C++ development environment, such as Visual Studio on Windows.

    Compilation: Compile the pathfinder.cpp file as a 64-bit dynamic-link library (DLL).

        From a Visual Studio Developer Command Prompt, you can use a command like this:
    Bash

    cl.exe /LD pathfinder.cpp

    Deployment: Copy the resulting pathfinder.dll file to the Assets/Plugins folder in your Unity project.

📄 License

This project is licensed under the MIT License.


InstantPipes


https://github.com/leth4/InstantPipes



PipeWiringOptimization

https://github.com/yeyeleijun/PipeWiringOptimization
