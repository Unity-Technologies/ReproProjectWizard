# Repro Project Wizard #

In order to submit bugs to Unity we request a project that reproduces the problem is included with the bug report. Often, creating a small cutdown project that shows the issue can take a lot of work. This tool is meant to simplify that process by quickly creating a new project that contains a small subset of the assets in your full project.

## Quick Start ##

* Download the Repro Wizard package and import it into your project.
* After the package imports and the code finishes compiling, go to Window->Repro Project Wizard. You should see the following window open:

![repro1.png](https://bitbucket.org/repo/xL7eqo/images/1954899552-repro1.png)

* Add a scene that shows the issue you want to include in your Repro Project to the Asset list at the top of the window.
* Select a path for the new project in the Project Path field.
* If image quality isn’t relevant to your issue, you can choose to downscale textures to reduce the new project size.
* Hit Create Project.


## Detailed Documentation ##

### Implementation ###
The tool works by building a list of files that need to be copied into the new project. Certain files always have to be copied for any Repro Project to work. In addition, there are a set of assets that are only necessary for the specific issue you are trying to reproduce. The tool will automatically include the dependencies of any asset you include, which means you often only have to include a single scene file to create your repro project.

A typical workflow would be to create a simple scene in your current project that illustrates the issue you are having, then add that single scene to the Repro Wizard. This will then create a new project containing only the assets needed by your simple scene. If it is not possible to create a simplified scene that reproduces your issue, you can just include a scene from your current project. This will still generally result in a much smaller Repro Project than your current full project.

All files with these extensions are copied into the new project:

* `ProjectSettings/*.asset`
* `*.cs`
* `*.dll`
* `*.cginc`
* `*.rsp`

All files in the Assets and Common Files list are used to create a list of dependencies. Each file in that list is checked to see if it depends on other assets, after which those assets are also added to the list of files to copy.

### Common Files ###
The Assets list is used to add files that are specific to the particular issue you are trying to reproduce. The Common Files list is used for files that are always necessary for any repro project to work. For example, if you load some assets directly from code that are used by global managers you would add those files here. You also have to include any assets referenced on the Graphics Settings panel for the project. 

The current state of the Repro Wizard window is saved to disk in the ReproProjectSettings file. Once you know which files need to be included in the Common Files list you can quickly reuse the Repro Wizard window by only changing which files are included in the Assets list.
### Wizard Window ###

![repro2.png](https://bitbucket.org/repo/xL7eqo/images/4201538918-repro2.png)

**Assets to Copy**
Contains a list of all assets to copy to the Repro Project along with their dependencies. Each entry has four modes: Scene, Prefab, Asset or Wildcard.

In Asset mode you can simply drag any assets you want copied into the object field, or use the standard Unity asset browser. Scene and Prefab modes are similar, except that the type of object allowed is filtered for scene files and prefabs respectively.

In Wildcard mode you can simply type a path to an asset into the text box, or use the file browser button ‘...’ to pick a file from the project. Adding a directory to the list will automatically include all files in that directory and its subdirectory. A wildcard will include all files matching that pattern in the folder and its subdirectories. For Example, `Assets/*.png` would recursively copy all png files into the new project. `Assets/Levels/MyFirstLevel/*.fbx` would copy all fbx under `Assets/Levels/MyFirstLevel`. In general though, it is much simpler to just create a scene that shows your issue and then let Unity work out which assets are referenced by the scene.

The ‘-’ button after each entry in the list can be used to remove that entry.

**Project Path**
The directory that the new project will be created in.

**Common Files**
You can add files here in the same way as Assets to Copy. Typically, these are assets that are required by all Repro Projects and can be set once the first time you create a Repro Project.

**Open Project After Export**
It can take a few minutes to copy all the assets in a larger Repro Project. This check box is a convenience feature to make Unity open the newly created project after it finishes copying files. This means you can leave the Repro Wizard running unattended, and it will have opened and imported the new project when you return.

**Texture Size**
Using this option you can automatically downscale all the textures in the newly created project. This is a great way to reduce the total size of the new project, at the cost of a slower export. NOTE: this does not affect the textures in your source project, the scaling is only applied to the texture that is copied to the new project.

**Create Project**
Hit this button to start the process of creating a new project.



## Known Issues ##

* The tool doesn’t include assets assigned to settings files. These need to be added to the Common Files list manually. Graphics Settings has been hard coded to copy SRP and Custom Shader resources. 
* Dependencies of asset bundles are not tracked currently.

