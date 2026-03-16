# Smart Grand Egyptian Museum — Content Assets

This folder holds all the media assets shown during the interactive table experience.
Place this entire `content/` folder next to `TuioDemo.exe` in the build output
(`bin/Debug/content/` or `bin/Release/content/`).

---

## Folder Structure

```
content/
  figures/
    0_cleopatra/        ← images for Cleopatra solo slideshow
    1_nefertiti/        ← images for Nefertiti solo slideshow
    2_tutankhamun/      ← images for Tutankhamun solo slideshow
    3_ramesses/         ← images for Ramesses II solo slideshow
    4_hatshepsut/       ← images for Hatshepsut solo slideshow
    5_akhenaten/        ← images for Akhenaten solo slideshow

  relationships/
    0_1_cleopatra_nefertiti/
    0_3_cleopatra_ramesses/
    1_2_nefertiti_tutankhamun/
    1_5_nefertiti_akhenaten/
    2_5_tutankhamun_akhenaten/
    3_2_ramesses_tutankhamun/
    4_3_hatshepsut_ramesses/
    5_4_akhenaten_hatshepsut/
```

---

## Supported Formats

| Type  | Extensions                      | Notes                                     |
| ----- | ------------------------------- | ----------------------------------------- |
| Image | `.jpg`, `.jpeg`, `.png`, `.bmp` | Landscape images work best (16:9 or 4:3)  |
| Video | _(filename only — see note)_    | Shown as a placeholder; see Video section |

Images that are missing from disk are silently replaced with a placeholder frame —
the slideshow continues normally.

---

## Required Image Files

### Cleopatra (ID 0) — `content/figures/0_cleopatra/`

| Filename            | Description                     |
| ------------------- | ------------------------------- |
| `portrait.jpg`      | Bust or portrait of Cleopatra   |
| `alexandria.jpg`    | Alexandria / Pharos lighthouse  |
| `rome_alliance.jpg` | Roman-Egyptian alliance imagery |

### Nefertiti (ID 1) — `content/figures/1_nefertiti/`

| Filename          | Description                        |
| ----------------- | ---------------------------------- |
| `bust.jpg`        | The famous Nefertiti bust (Berlin) |
| `amarna_city.jpg` | City of Amarna / Akhetaten         |

### Tutankhamun (ID 2) — `content/figures/2_tutankhamun/`

| Filename             | Description                        |
| -------------------- | ---------------------------------- |
| `golden_mask.jpg`    | The golden death mask              |
| `tomb_discovery.jpg` | Howard Carter / tomb entrance 1922 |

### Ramesses II (ID 3) — `content/figures/3_ramesses/`

| Filename            | Description                    |
| ------------------- | ------------------------------ |
| `abu_simbel.jpg`    | Abu Simbel temples             |
| `battle_kadesh.jpg` | Relief of the Battle of Kadesh |

### Hatshepsut (ID 4) — `content/figures/4_hatshepsut/`

| Filename              | Description                    |
| --------------------- | ------------------------------ |
| `temple.jpg`          | Deir el-Bahari mortuary temple |
| `punt_expedition.jpg` | Punt expedition reliefs        |

### Akhenaten (ID 5) — `content/figures/5_akhenaten/`

| Filename              | Description                    |
| --------------------- | ------------------------------ |
| `colossal_statue.jpg` | Colossal statue at Karnak      |
| `amarna_art.jpg`      | Amarna period naturalistic art |

---

## Required Relationship Images

### Nefertiti + Akhenaten — `content/relationships/1_5_nefertiti_akhenaten/`

| Filename                  | Description                |
| ------------------------- | -------------------------- |
| `royal_couple_relief.jpg` | Relief of the royal couple |
| `family_scene.jpg`        | Amarna family scene        |

### Tutankhamun + Akhenaten — `content/relationships/2_5_tutankhamun_akhenaten/`

| Filename                | Description                      |
| ----------------------- | -------------------------------- |
| `amarna_succession.jpg` | Amarna period succession         |
| `restoration_stele.jpg` | Restoration stele of Tutankhamun |

### Nefertiti + Tutankhamun — `content/relationships/1_2_nefertiti_tutankhamun/`

| Filename               | Description                   |
| ---------------------- | ----------------------------- |
| `amarna_interlude.jpg` | Amarna interlude / succession |

### Cleopatra + Nefertiti — `content/relationships/0_1_cleopatra_nefertiti/`

| Filename         | Description                           |
| ---------------- | ------------------------------------- |
| `two_queens.jpg` | Composite / comparison of both queens |
| `legacy_art.jpg` | Their legacy in art                   |

### All other relationship folders

Add any `.jpg` or `.png` images; filenames must match the paths in `FigureData.cs`.

---

## Adding Videos

The app renders a video slide as a golden play-button placeholder with the filename shown.
To actually play videos you can extend `TuioDemo.cs`:

- Add a `System.Windows.Forms.Panel` hosting `AxWindowsMediaPlayer`
- Show/hide it when `ContentType.Video` slides are active

---

## Image Recommendations

- **Resolution**: 1920×1080 px or higher
- **Format**: JPEG for photos, PNG for graphics with transparency
- **License**: Ensure you have rights to use all images in the museum
- Public domain sources: Wikimedia Commons, Metropolitan Museum Open Access,
  British Museum Collection Online

---

## TUIO Marker → Figure Mapping

| Marker ID | Figure Name   | Accent Colour      |
| --------- | ------------- | ------------------ |
| 0         | Cleopatra VII | Gold #D4AF37       |
| 1         | Nefertiti     | Sky Blue #64BEE6   |
| 2         | Tutankhamun   | Amber #DAA520      |
| 3         | Ramesses II   | Crimson #D25032    |
| 4         | Hatshepsut    | Warm Brown #AF8246 |
| 5         | Akhenaten     | Orange #FFA032     |

Print the corresponding fiducial marker from the reacTIVision symbols sheet and
attach it under each physical figure with the marker's **north (top) edge
aligned with the figure's facing direction**. If the figure ends up rotated
relative to the ideal, adjust `FacingAngleOffset` (radians) in `FigureData.cs`
for that figure.
