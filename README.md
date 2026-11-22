# ImageChecker

ImageChecker adds a right-click menu to Windows File Explorer for working with one image or a batch. It supports PNG, JPG/JPEG, and WebP.

## Get started

1. Download **ImageChecker-win-x64.zip** from the [latest release](https://github.com/MCrystal-Dev/ImageChecker/releases/latest).
2. Extract the entire ZIP to a folder you want to keep, then double-click **Enable Explorer Menu.cmd**. It registers the menu for your Windows account.
3. Right-click one or more images or folders in File Explorer. On Windows 11, choose **Show more options → ImageChecker**. Selecting a folder includes PNG, JPG/JPEG and WebP images throughout its subfolders. Overlapping selections include each image once. The folder scan skips inaccessible directories and nested directory links or junctions.
4. The images start selected. Click one row, Ctrl-click to toggle individual images, Shift-click to select a range, or use **Select all** in the bottom bar or Ctrl+A to highlight every row. **Rename**, **Export copies** and **Overwrite** apply to the highlighted images. Numbered renames allocate numbers in list order. The preview updates automatically when you change the action or its settings. Choose **Find duplicates** to review matching pairs.

You can also double-click **ImageChecker.exe** and select images from the file picker. To remove the menu, run **Remove Explorer Menu.cmd**. If you move the extracted folder, run **Enable Explorer Menu.cmd** from its new location.

## Windows compatibility

The ZIP includes the .NET 10 runtime and the x64 image-processing library. Use it on an Intel or AMD x64 PC. ImageChecker is tested locally on Windows 11. For Windows 10, check your edition and version against the [supported .NET 10 Windows releases](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md#windows).

The window fits the monitor's working area and rescales when moved between monitors with different display scales. The layout tests exercise all actions at simulated 100%, 125%, 150% and 200% scaling.

## Actions

| Action | Result |
| --- | --- |
| Add todays date to image | Adds the local date to each original name, for example `2026-11-20_beach.jpg`. |
| Rename with date and iterating number | Replaces the original name with the local date and the next number, keeping the extension. For example, the eighth image on 21 November 2026 becomes `2026_11_21_008.png` if it is a PNG. The counter is shared across folders and app restarts. It starts at 001 each local day, skips occupied names and records successful renames. |
| Change file format | Converts images to PNG, JPG or WebP. Transparent pixels use a white background when converting to JPG. |
| Resize by dimensions | Fits within the chosen width and height in pixels, preserving proportions and avoiding enlargement. |
| Resize to a file size limit | Creates a copy just under the chosen maximum. 1 MB = 1,000 KB and 1 KB = 1,000 bytes. **Convert to** defaults to **Do not convert file format** and also offers JPG, WebP and PNG. The preview shows the actual dimensions and encoded byte count. |
| Find duplicates | Compares the selected images with images in their immediate folders. Exact copies appear first, followed by visually similar pairs. Choose **Keep both** to advance, or choose **Delete Left**, **Delete Right**, or click an image. Confirm the displayed path to move that image to the Recycle Bin. |
| View image metadata | Click an image to see its path, dimensions, format, file timestamps and available embedded fields such as camera model, EXIF dates, XMP fields or PNG text. |
| Add detected date to filename | Prefixes the highlighted filenames, for example `2024-06-15_beach.jpg`. The preview shows the date source. It prefers the recorded capture date, then an embedded digitization or creation date, then a recorded acquisition date. If those are missing it uses the local file creation date, marked as an estimate because copying or downloading can change it. |

**Export copies** keeps the originals and creates a new folder named `ImageChecker Export`, then `ImageChecker Export (2)`, and so on. When you open images from several folders together, the export folder goes beside the first source image in the list. Copies use a suffix such as `-resized`.

For a size limit, ImageChecker keeps the original bytes if the format already matches and the file is below the cap. Otherwise it tries full resolution first, including lossless WebP when selected. If needed it strips metadata and searches JPG or WebP quality settings. When a smaller resolution is necessary, it searches for the largest fitting dimensions while preserving proportions. Simple images can reach full quality well below the cap. JPG uses a white background for transparent pixels.

**Overwrite** permanently replaces the selected originals with the previewed images. Review the paths and click **OK** to proceed or **Cancel** to keep the originals. Format changes keep the base name and use the new format's extension. If that name is already occupied by another file, ImageChecker skips that replacement and reports it.

Image export supports still images. Decoding is limited to 20,000 pixels per side and subject to memory, disk cache and embedded profile limits. Visual matching compares the proportions, colors and transparency of still images to find resized or re-encoded copies. Animated images are compared by exact file contents.

## Build

On Windows x64 with the .NET 10 SDK:

```powershell
dotnet run --project src
dotnet run --project tests -c Release
dotnet run --project tests -c Release -- --layout
dotnet run --project tests -c Release -- --review
.\packaging\Build.ps1
```

The build script produces `dist/ImageChecker-win-x64.zip`. The app uses Windows Forms, Magick.NET and a per-user Explorer drop target that receives the full selection in one invocation. The ZIP includes notices for the bundled components.

The interactive review checks use temporary files and exercise the Windows Recycle Bin. To measure duplicate-scan startup with the same generated 48-file batch, run `dotnet run --project tests -c Release -- --benchmark`.

## License

ImageChecker is distributed under the [MIT license](LICENSE). The bundled .NET and Magick.NET components carry their own licenses and notices in the release ZIP.
