# ImageChecker

ImageChecker adds a right-click menu to Windows File Explorer for working with one image or a batch. It supports PNG, JPG/JPEG, and WebP.

## Get started

1. Download **ImageChecker-win-x64.zip** from the [latest release](https://github.com/MCrystal-Dev/ImageChecker/releases/latest).
2. Extract the entire ZIP to a folder you want to keep, then double-click **Enable Explorer Menu.cmd**. It registers the menu for your Windows account.
3. Right-click one or more images or folders in File Explorer. On Windows 11, choose **Show more options → ImageChecker**. Selecting folders includes PNG, JPG/JPEG, and WebP files directly inside each selected folder. To process images in a subfolder, select that subfolder itself.
4. Choose an action. Review the preview, then click **Rename**, **Save copies**, or **Review removal**.

You can also double-click **ImageChecker.exe** and select images from the file picker. The download includes the .NET runtime and runs on x64 Windows 10 and 11. To remove the menu, run **Remove Explorer Menu.cmd**. If you move the extracted folder, run **Enable Explorer Menu.cmd** from its new location.

## Actions

| Action | Result |
| --- | --- |
| Rename with today's date | Adds the local date to each original name, for example `2026-11-20_beach.jpg`. |
| Rename with daily counter | Produces `001.jpg`, `002.jpg`, etc. Select **Include today's date** for `2026-11-20_001.jpg`. Both styles share a counter across folders and app restarts. It starts at 001 each local day, skips occupied names and records successful renames. |
| Change file format | Writes PNG, JPG or WebP copies beside the originals. Transparent pixels use a white background when exporting to JPG. |
| Resize by dimensions | Fits within the chosen width and height in pixels, preserving proportions and avoiding enlargement. |
| Resize to a file size limit | Creates a copy at or below the chosen number of KB (1 KB = 1,000 bytes). Shows the resulting dimensions and exact byte count before saving. For larger files it can reduce encoding quality, strip metadata, and shrink the image. |
| Find duplicates | Scans the selected images' immediate folders for byte-identical files matching the selection. Compare images side by side, check individual copies, and review the full paths before moving them to the Recycle Bin. At least one copy in each group must be kept. |

Exported copies use a suffix such as `-resized`. The preview shows the next available name when a destination is occupied. If another program creates the destination after preview, ImageChecker reports the conflict. Image export supports still images. Duplicate matching uses file bytes, so edited or re-encoded versions can appear as separate images.

## Build

On Windows x64 with the .NET 10 SDK:

```powershell
dotnet run --project src
dotnet run --project tests -c Release
.\packaging\Build.ps1
```

The build script produces `dist/ImageChecker-win-x64.zip`. The app uses Windows Forms, Magick.NET and a per-user Explorer drop target that receives the full selection in one invocation. The ZIP includes notices for the bundled components.
