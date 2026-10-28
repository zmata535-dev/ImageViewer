# Clean Image Viewer

A fast, lightweight, and clutter-free Windows desktop image viewer built with C# and WPF. It features a completely distraction-free interface where the client area is 100% dedicated to the image canvas with standard Windows window borders, and all controls and tools are accessible via **Right-Click**.

---

## Key Features

- **Wide Format Support**: Loads and views **SVG** (Scalable Vector Graphics), PNG, JPEG/JPG, BMP, GIF, ICO (multi-resolution / highest detail), TIFF, WebP, WMP, HDP, and all Windows WIC / GDI+ image formats. Vector SVGs are rendered sharp with automatic resolution scaling.
- **Minimalist & Clean Interface**: No permanent menus, ribbon bars, or bottom status bars. Just the clean viewport with a sleek dark background (or toggleable transparency checkerboard) and standard Windows window border.
- **All Menus on Right-Click**: A comprehensive right-click context menu containing File operations, Folder Navigation, Zoom & View controls, Transformations & Edits, and Window Preferences.
- **Smooth Zoom & Pan**:
  - Mouse wheel zoom centered directly at the cursor location.
  - Left-click drag to pan smoothly around the image when zoomed in.
  - Fit to Window (`F` or `0`) and 100% Actual Size (`1` or `Ctrl+0`).
  - Nearest-neighbor rendering at high zoom (>400%) for pixel art inspection.
- **Folder Navigation**:
  - Instant Next (`Right Arrow` / `Space` / `PageDown`) and Previous (`Left Arrow` / `Backspace` / `PageUp`) image switching within the same folder.
  - Uses Windows Explorer natural alphanumeric sort order (`img2` before `img10`).
- **Transformations & Edits**:
  - **Rotate**: 90° Clockwise (`R`), 90° Counter-Clockwise (`Shift+R` / `L`), 180°.
  - **Flip**: Horizontally (`H`), Vertically (`V`).
  - **Invert Colors**: Instant color inversion preserving alpha transparency (`I` / `Ctrl+I`).
  - **Crop Tool**: Interactive cropping box with 8 resize handles, rule-of-thirds grid, dark shade mask, and live pixel dimension badge (`C`). Press `Enter` or double-click to apply, `Esc` to cancel.
  - **Undo**: Single-step and multi-step edit undo (`Ctrl+Z`).
- **File / Image Properties**:
  - Full modal dialog (`Alt+Enter`) displaying Dimensions, Megapixels, Aspect Ratio, File Size, Format, Color Depth / Pixel Format, DPI Resolution, Transparency/Alpha, File Timestamps, and Attributes, with a one-click "Copy Info" button.
- **Save & Save As**:
  - **Save** (`Ctrl+S`): Overwrites the active file cleanly without file lock conflicts.
  - **Save As** (`Ctrl+Shift+S`): Save to PNG, JPG, BMP, TIFF, GIF with standard Save dialog.
- **Subtle HUD Toast**: Non-intrusive floating feedback badge showing zoom levels, rotation, crop sizes, and file position without cluttering the screen.
- **Drag & Drop**: Drag any image file from File Explorer directly onto the app window to view it.

---

## Keyboard Shortcuts Quick Reference

| Action | Shortcut |
| :--- | :--- |
| **Open File** | `Ctrl + O` |
| **Save** | `Ctrl + S` |
| **Save As** | `Ctrl + Shift + S` |
| **Reload Original** | `F5` / `Ctrl + R` |
| **Copy Image to Clipboard** | `Ctrl + C` |
| **Image Properties** | `Alt + Enter` |
| **Next Image** | `Right Arrow` / `Space` / `Page Down` |
| **Previous Image** | `Left Arrow` / `Backspace` / `Page Up` |
| **First Image / Last Image** | `Home` / `End` |
| **Zoom In / Zoom Out** | `Ctrl + Wheel` or `+` / `-` |
| **Fit to Window** | `F` or `0` |
| **Actual Size (100%)** | `1` or `Ctrl + 0` |
| **Pan Image** | Left Mouse Drag |
| **Rotate 90° CW** | `R` |
| **Rotate 90° CCW** | `Shift + R` or `L` |
| **Flip Horizontally** | `H` |
| **Flip Vertically** | `V` |
| **Invert Colors** | `I` |
| **Crop Image Mode** | `C` |
| **Apply Crop** | `Enter` (or double click selection) |
| **Cancel Crop** | `Esc` (or right click) |
| **Undo Edit** | `Ctrl + Z` |
| **Fullscreen** | `F11` (or double click image) |
| **Exit** | `Alt + F4` / `Esc` (when not cropping) |

---

## Building from Source

To compile the standalone `ImageViewer.exe` using the Windows built-in C# compiler (`csc.exe`):

```powershell
powershell -ExecutionPolicy Bypass -File .\Build.ps1
```
