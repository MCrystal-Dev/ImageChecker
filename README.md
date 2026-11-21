# ImageChecker

ImageChecker adds a right-click menu to Windows File Explorer for working with one image or a batch. It supports PNG, JPG/JPEG, and WebP.

## Get started

1. Download **ImageChecker-win-x64.zip** from the [latest release](https://github.com/MCrystal-Dev/ImageChecker/releases/latest).
2. Extract the entire ZIP to a folder you want to keep, then double-click **Enable Explorer Menu.cmd**. It registers the menu for your Windows account.
3. Right-click one or more images or folders in File Explorer. On Windows 11, choose **Show more options → ImageChecker**. Selecting folders includes PNG, JPG/JPEG, and WebP files directly inside each selected folder. To process images in a subfolder, select that subfolder itself.
4. The images start selected. Click one row, Ctrl-click to toggle individual images, Shift-click to select a range, or use Ctrl+A to select all. **Rename** and **Save copies** apply to the highlighted images. Numbered renames allocate numbers to those images in list order. The preview updates automatically when you change the action or its settings. For duplicate removal, check the copies to recycle and click **Review removal**.

You can also double-click **ImageChecker.exe** and select images from the file picker. The download includes the .NET runtime and runs on x64 Windows 10 and 11. To remove the menu, run **Remove Explorer Menu.cmd**. If you move the extracted folder, run **Enable Explorer Menu.cmd** from its new location.

## Actions

| Action | Result |
| --- | --- |
| Add todays date to image | Adds the local date to each original name, for example `2026-11-20_beach.jpg`. |
| Rename with date and iterating number | Replaces the original name with the local date and the next number, keeping the extension. For example, the eighth image on 21 November 2026 becomes `2026_11_21_008.png` if it is a PNG. The counter is shared across folders and app restarts. It starts at 001 each local day, skips occupied names and records successful renames. |
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
