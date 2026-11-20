# ImageChecker

ImageChecker is a planned Windows utility for renaming, converting, resizing, and checking images from the File Explorer right-click menu. The menu will work with a single selected image or a batch.

## Planned features

- **Rename with today's date.** Add the local date to the original filename, such as `2026-11-20_beach.jpg`.
- **Rename with a daily counter.** Choose a number-only name like `004.jpg` or include the date as in `2026-11-20_004.jpg`. Single-image and batch renames share a counter. ImageChecker saves it across restarts, starts at `001` each day, and advances it after a successful rename.
- **Convert formats.** Export selected images as PNG, JPG, or WebP.
- **Resize by dimensions.** Fit images within a chosen width and height while preserving their aspect ratio.
- **Resize to a file size limit.** Set a maximum size per image, such as 500 KB, and preview the resulting dimensions and file size.
- **Find duplicates.** Compare selected images with images in their containing folders and review matches side by side. Moving selected copies to the Recycle Bin requires explicit confirmation that lists the affected files.

Renaming, conversion, and resizing will show a preview and flag filename conflicts before applying changes. Conversion and resizing will create new copies by default.
