/*
 * FigureData.cs
 * Smart Grand Egyptian Museum — HCI Interactive Table
 *
 * Contains all data definitions for Egyptian figures, their solo content,
 * and the historical relationships between pairs of figures.
 *
 * TUIO Marker Assignments:
 *   ID 0 → Circular Menu Controller
 *   ID 1 → Cleopatra VII
 *   ID 2 → Nefertiti
 *   ID 3 → Tutankhamun
 *   ID 4 → Ramesses II
 *   ID 5 → Hatshepsut
 *   ID 6 → Akhenaten
 */

using System.Collections.Generic;
using System.Drawing;

// ─────────────────────────────────────────────────────────────────────────────
//  Content Types
// ─────────────────────────────────────────────────────────────────────────────

public enum ContentType { Text, Image, Video }

/// <summary>
/// A single slide in a slideshow — either a text paragraph, an image, or a video.
/// Content is a file path (relative to executable) for Image/Video, or
/// a plain string for Text.
/// </summary>
public class ContentSlide
{
    public ContentType Type       { get; set; }
    public string      Content    { get; set; }   // text body OR relative file path
    public int         DurationMs { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Figure Definition
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Defines an Egyptian figure mapped to a specific TUIO marker symbol ID.
/// </summary>
public class FigureDef
{
    public int              SymbolId         { get; set; }
    public string           Name             { get; set; }
    public string           Period           { get; set; }
    public string           ShortDescription { get; set; }
    public Color            AccentColor      { get; set; }
    public string           MarkerImagePath  { get; set; }

    /// <summary>
    /// How much to add to the raw TUIO angle (radians) when computing facing direction.
    /// Adjust per figure if the physical marker is rotated relative to the figure's face.
    /// </summary>
    public float FacingAngleOffset { get; set; }

    /// <summary>Slides shown when this figure is alone on the table.</summary>
    public List<ContentSlide> SoloSlides { get; set; }

    /// <summary>
    /// Optional interactive objects shown in single-figure mode.
    /// If the user rotates the figure to face one object for enough time,
    /// that object's StorySlides are started.
    /// </summary>
    public List<SceneObjectDef> SceneObjects { get; set; }
}

/// <summary>
/// A static object rendered in single-figure mode for directional selection.
/// X/Y are normalized table coordinates in range 0..1.
/// </summary>
public class SceneObjectDef
{
    public string Name { get; set; }
    public string ImagePath { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public List<ContentSlide> StorySlides { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Relationship Definition
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Defines the content shown when two specific figures face each other.
/// SymbolIdA / SymbolIdB order does not matter — matching is symmetric.
/// </summary>
public class RelationshipDef
{
    public int              SymbolIdA       { get; set; }
    public int              SymbolIdB       { get; set; }
    public string           ConnectionTitle { get; set; }
    public List<ContentSlide> Slides        { get; set; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Museum Data Registry
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Central registry for all figures and relationships.
/// Images should be placed under  content/figures/[id_name]/  and
/// content/relationships/[idA]_[idB_name]/  relative to the executable.
/// </summary>
public static class MuseumData
{
    public static readonly Dictionary<int, FigureDef>   Figures;
    public static readonly List<RelationshipDef>         Relationships;

    static MuseumData()
    {
        // ── FIGURES ──────────────────────────────────────────────────────────

        Figures = new Dictionary<int, FigureDef>
        {
            // ── 1: Cleopatra VII ─────────────────────────────────────────────
            { 1, new FigureDef
            {
                SymbolId         = 1,
                Name             = "Cleopatra VII",
                Period           = "69 BC – 30 BC",
                ShortDescription = "Last Pharaoh of Ancient Egypt",
                AccentColor      = Color.FromArgb(212, 175, 55),
                MarkerImagePath  = "content/figures/0_cleopatra/marker_cleopatra.png",
                SceneObjects     = new List<SceneObjectDef>
                {
                    new SceneObjectDef
                    {
                        Name      = "Royal Cobra Crown",
                        ImagePath = "content/objects/0_cleopatra/royal_cobra_crown.png",
                        X         = 0.18f,
                        Y         = 0.30f,
                        StorySlides = new List<ContentSlide>
                        {
                            new ContentSlide { Type = ContentType.Image,
                                Content    = "content/objects/0_cleopatra/royal_cobra_crown.png",
                                DurationMs = 6000 },
                            new ContentSlide { Type = ContentType.Text,
                                Content    = "The uraeus cobra on Cleopatra's crown symbolized divine kingship and protection. By presenting herself with this iconography, Cleopatra linked her rule to ancient Egyptian royal legitimacy, not only to her Greek Ptolemaic heritage.",
                                DurationMs = 8500 },
                        }
                    },
                    new SceneObjectDef
                    {
                        Name      = "Alexandrian Coin",
                        ImagePath = "content/objects/0_cleopatra/alexandrian_coin.png",
                        X         = 0.78f,
                        Y         = 0.33f,
                        StorySlides = new List<ContentSlide>
                        {
                            new ContentSlide { Type = ContentType.Image,
                                Content    = "content/objects/0_cleopatra/alexandrian_coin.png",
                                DurationMs = 6000 },
                            new ContentSlide { Type = ContentType.Text,
                                Content    = "Coins minted in Alexandria carried Cleopatra's portrait and political messages. Through coinage, she projected authority across the Mediterranean, shaping how allies and rivals in Rome perceived Egypt's final pharaoh.",
                                DurationMs = 8500 },
                        }
                    },
                    new SceneObjectDef
                    {
                        Name      = "Nile Barge",
                        ImagePath = "content/objects/0_cleopatra/nile_barge.png",
                        X         = 0.52f,
                        Y         = 0.74f,
                        StorySlides = new List<ContentSlide>
                        {
                            new ContentSlide { Type = ContentType.Image,
                                Content    = "content/objects/0_cleopatra/nile_barge.png",
                                DurationMs = 6000 },
                            new ContentSlide { Type = ContentType.Text,
                                Content    = "Ancient sources describe Cleopatra's ceremonial barge as a floating stage of power. Her theatrical Nile appearances fused diplomacy, religion, and spectacle, reinforcing her image as a sovereign equal to Rome's strongest leaders.",
                                DurationMs = 9000 },
                        }
                    }
                },
                SoloSlides       = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/0_cleopatra/marker_cleopatra.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Cleopatra VII Philopator was the last active ruler of the Ptolemaic Kingdom of Egypt, reigning from 51 BC until her death in 30 BC. She was the first Ptolemaic ruler to learn the Egyptian language.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "This marker image represents Cleopatra as an enduring icon of royal authority. Cleopatra ruled from Alexandria and was renowned for her intellect, multilingualism (she spoke nine languages), and political mastery.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "She formed powerful alliances with Julius Caesar and later Mark Antony, attempting to restore Ptolemaic power. After the defeat at the Battle of Actium in 31 BC, she took her own life — ending the last independent Pharaonic dynasty.",
                        DurationMs = 9000 },
                }
            }},

            // ── 2: Nefertiti ─────────────────────────────────────────────────
            { 2, new FigureDef
            {
                SymbolId         = 2,
                Name             = "Nefertiti",
                Period           = "c. 1370 BC – 1330 BC",
                ShortDescription = "Great Royal Wife of Akhenaten",
                AccentColor      = Color.FromArgb(100, 190, 230),
                MarkerImagePath  = "content/figures/1_nefertiti/marker_nefertiti.png",
                SoloSlides       = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/1_nefertiti/marker_nefertiti.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Nefertiti was the Great Royal Wife of Pharaoh Akhenaten. Together they established the monotheistic cult of Aten — the sun disk — as Egypt's sole official religion, a dramatic break from thousands of years of tradition.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "This marker image uses Nefertiti's iconic profile and headdress, inspired by the famous limestone bust discovered in 1912 at Amarna. Her name in Egyptian means \"The Beautiful One Has Come.\"",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Some Egyptologists argue that after Akhenaten's death, Nefertiti herself ruled as Pharaoh under the name Neferneferuaten, before Tutankhamun ascended to the throne.",
                        DurationMs = 8000 },
                }
            }},

            // ── 3: Tutankhamun ───────────────────────────────────────────────
            { 3, new FigureDef
            {
                SymbolId         = 3,
                Name             = "Tutankhamun",
                Period           = "c. 1341 BC – 1323 BC",
                ShortDescription = "The Boy King",
                AccentColor      = Color.FromArgb(218, 165, 32),
                SoloSlides       = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Tutankhamun became Pharaoh at approximately 8–9 years of age. He reigned for roughly a decade before dying at around 18. His relatively short, unremarkable reign would have been forgotten — had his tomb not survived nearly intact for 3,000 years.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "In November 1922, British archaeologist Howard Carter discovered Tutankhamun's sealed tomb in the Valley of the Kings. It contained over 5,000 artefacts, including the iconic solid gold death mask now on display at the Grand Egyptian Museum.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Tutankhamun reversed his father Akhenaten's revolutionary religion, restoring the traditional Egyptian gods — especially Amun — and moving the capital back to Memphis. He changed his own name from Tutankhaten to Tutankhamun to reflect this.",
                        DurationMs = 9000 },
                }
            }},

            // ── 4: Ramesses II ───────────────────────────────────────────────
            { 4, new FigureDef
            {
                SymbolId         = 4,
                Name             = "Ramesses II",
                Period           = "c. 1303 BC – 1213 BC",
                ShortDescription = "Ramesses the Great",
                AccentColor      = Color.FromArgb(210, 80, 50),
                MarkerImagePath  = "content/figures/3_ramesses/marker_ramesses_ii.png",
                SoloSlides       = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/3_ramesses/marker_ramesses_ii.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Ramesses II — known as Ramesses the Great — is often regarded as the most powerful pharaoh of the Egyptian Empire. He reigned for 66 years and fathered over 100 children, outliving many of his heirs.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "This marker image reflects Ramesses II's monumental legacy. He led the Egyptian army at the Battle of Kadesh against the Hittite Empire — the earliest recorded major battle in detail. Although the battle was inconclusive, Ramesses portrayed it as a great victory in temple reliefs across Egypt.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Ramesses II signed the first known international peace treaty in history — the Treaty of Kadesh with the Hittite King Hattusili III around 1259 BC. A copy is displayed at the United Nations headquarters in New York.",
                        DurationMs = 9000 },
                }
            }},

            // ── 5: Hatshepsut ────────────────────────────────────────────────
            { 5, new FigureDef
            {
                SymbolId         = 5,
                Name             = "Hatshepsut",
                Period           = "c. 1507 BC – 1458 BC",
                ShortDescription = "Egypt's Longest-Reigning Female Pharaoh",
                AccentColor      = Color.FromArgb(175, 130, 70),
                MarkerImagePath  = "content/figures/4_hatshepsut/marker_hatshepsut.png",
                SoloSlides       = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/4_hatshepsut/marker_hatshepsut.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Hatshepsut was the fifth Pharaoh of the 18th Dynasty and one of the most successful rulers of ancient Egypt. She reigned for approximately 22 years — longer than any other female ruler of an indigenous Egyptian dynasty.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "This marker image highlights Hatshepsut's royal form and authority. She launched a famous trading expedition to the Land of Punt, returning with gold, ivory, myrrh trees, and exotic animals, then commemorated this voyage in reliefs at Deir el-Bahari.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Hatshepsut often had herself depicted as male in statues and inscriptions — wearing a false beard and male regalia — to legitimise her rule in a culture that expected a male pharaoh. After her death, her successor Thutmose III had her images systematically erased.",
                        DurationMs = 10000 },
                }
            }},

            // ── 6: Akhenaten ─────────────────────────────────────────────────
            { 6, new FigureDef
            {
                SymbolId         = 6,
                Name             = "Akhenaten",
                Period           = "c. 1380 BC – 1336 BC",
                ShortDescription = "The Heretic Pharaoh",
                AccentColor      = Color.FromArgb(255, 160, 50),
                SoloSlides       = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Akhenaten — born Amenhotep IV — overturned thousands of years of Egyptian polytheism. He declared the sun disk Aten to be the sole true god, closed the temples of other gods, and redirected all religious and state resources to Aten worship.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "He built a brand new capital city, Akhetaten (modern Amarna), in the desert. His reign produced a distinctive artistic style that depicted the royal family with elongated heads, wide hips, and in intimate domestic scenes — a radical departure from traditional Egyptian art.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "After his death, subsequent pharaohs — including his own son Tutankhamun — dismantled everything Akhenaten had built. His name was struck from official records; he was referred to only as \"the enemy\" or \"the criminal of Amarna.\"",
                        DurationMs = 9000 },
                }
            }},
        };

        // ── RELATIONSHIPS ─────────────────────────────────────────────────────

        Relationships = new List<RelationshipDef>
        {
            // ── Nefertiti + Akhenaten  (Royal Couple) ────────────────────────
            new RelationshipDef
            {
                SymbolIdA       = 2,
                SymbolIdB       = 6,
                ConnectionTitle = "Husband & Wife — The Revolutionary Royal Couple",
                Slides          = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/1_nefertiti/marker_nefertiti.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Nefertiti and Akhenaten were one of history's most famous royal couples. Together they co-ruled Egypt, dismantled its ancient religion, founded a new capital city at Amarna, and replaced millennia of polytheism with the monotheistic worship of Aten.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Nefertiti bore Akhenaten six daughters. Their family was depicted in Amarna art in unusually tender, intimate scenes — a complete break from the formal, rigid conventions of earlier Egyptian art. Egyptologists believe Nefertiti wielded extraordinary political power, possibly equal to Akhenaten's own.",
                        DurationMs = 10000 },
                }
            },

            // ── Tutankhamun + Akhenaten  (Father & Son) ──────────────────────
            new RelationshipDef
            {
                SymbolIdA       = 3,
                SymbolIdB       = 6,
                ConnectionTitle = "Father & Son — A Legacy Reversed",
                Slides          = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Tutankhamun was born as \"Tutankhaten\" — \"Living Image of Aten\" — the son of Akhenaten. He inherited a kingdom in religious turmoil. Under the guidance of his advisors, the young Pharaoh reversed every reform his father had introduced.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Tutankhamun changed his name from Tutankhaten to Tutankhamun, restored the old gods, reopened the temples, moved the capital back to Memphis, and began the systematic erasure of his own father's name and image from Egyptian history.",
                        DurationMs = 9000 },
                }
            },

            // ── Nefertiti + Tutankhamun  (Stepmother & Stepson) ──────────────
            new RelationshipDef
            {
                SymbolIdA       = 2,
                SymbolIdB       = 3,
                ConnectionTitle = "Stepmother & Stepson — The Amarna Succession",
                Slides          = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/1_nefertiti/marker_nefertiti.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Nefertiti was the stepmother of Tutankhamun. She was the principal wife of his father Akhenaten, though Tutankhamun was born of another, lesser wife. When Akhenaten died, the succession was turbulent and brief.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Some Egyptologists believe that in the years between Akhenaten's death and Tutankhamun's coronation, Nefertiti herself ruled as co-regent or sole Pharaoh under the name Neferneferuaten. If true, she is one of very few female rulers of ancient Egypt.",
                        DurationMs = 10000 },
                }
            },

            // ── Cleopatra + Nefertiti  (Queens of Egypt) ─────────────────────
            new RelationshipDef
            {
                SymbolIdA       = 1,
                SymbolIdB       = 2,
                ConnectionTitle = "Queens of Egypt — Icons Across the Ages",
                Slides          = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/0_cleopatra/marker_cleopatra.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Separated by more than 1,300 years, Cleopatra and Nefertiti are two of the most iconic women in all of human history. Nefertiti wielded religious and political power in 14th-century BC Egypt; Cleopatra commanded an empire in the 1st century BC.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/1_nefertiti/marker_nefertiti.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Both women broke the conventions of their time. Nefertiti was depicted in art as an equal to her husband — a rarity in ancient Egypt. Cleopatra was the sole sovereign ruler, allied with the two most powerful men in Rome. Both became global symbols of feminine power.",
                        DurationMs = 10000 },
                }
            },

            // ── Cleopatra + Ramesses II  (Last and Greatest) ─────────────────
            new RelationshipDef
            {
                SymbolIdA       = 1,
                SymbolIdB       = 4,
                ConnectionTitle = "Egypt's Most Celebrated Rulers — 1,200 Years Apart",
                Slides          = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/0_cleopatra/marker_cleopatra.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Ramesses II ruled during Egypt's New Kingdom golden age in the 13th century BC. Cleopatra VII ruled more than 1,200 years later, in the 1st century BC. By Cleopatra's time, the Abu Simbel temples that Ramesses had built were already ancient monuments.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/3_ramesses/marker_ramesses_ii.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Both are considered among the greatest rulers in Egypt's 3,000-year history. Ramesses defined Egypt's imperial power at its height; Cleopatra held the kingdom together in its final century with intelligence, diplomacy, and sheer will.",
                        DurationMs = 9000 },
                }
            },

            // ── Hatshepsut + Ramesses II  (Great Builder Pharaohs) ───────────
            new RelationshipDef
            {
                SymbolIdA       = 5,
                SymbolIdB       = 4,
                ConnectionTitle = "Egypt's Greatest Builder Pharaohs",
                Slides          = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/4_hatshepsut/marker_hatshepsut.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Hatshepsut and Ramesses II — separated by two centuries — share a legacy of extraordinary construction. Hatshepsut built the stunning three-tiered mortuary temple at Deir el-Bahari. Ramesses II carved the colossal Abu Simbel temples directly into a cliff face.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/3_ramesses/marker_ramesses_ii.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Both were also masters of self-promotion. Hatshepsut covered her temple walls with accounts of her divine birth and the Punt expedition. Ramesses II had his image carved on virtually every major monument in Egypt — including some built by his predecessors.",
                        DurationMs = 10000 },
                }
            },

            // ── Akhenaten + Hatshepsut  (Rebels Against Tradition) ───────────
            new RelationshipDef
            {
                SymbolIdA       = 6,
                SymbolIdB       = 5,
                ConnectionTitle = "Rebels Against Tradition — Rulers Who Defied Convention",
                Slides          = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/4_hatshepsut/marker_hatshepsut.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Hatshepsut and Akhenaten are two of Egypt's most unconventional rulers. Hatshepsut defied gender convention by ruling as a male pharaoh for over 20 years. Akhenaten defied religious convention by abolishing Egypt's entire pantheon in favour of one god.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Both faced posthumous erasure: after Hatshepsut's death, Thutmose III had her name chiselled from monuments; after Akhenaten's death, his name was struck from official records. History tried to forget them both — but failed.",
                        DurationMs = 9000 },
                }
            },

            // ── Ramesses II + Tutankhamun  (Two Kings of the 19th/18th Dynasty)
            new RelationshipDef
            {
                SymbolIdA       = 4,
                SymbolIdB       = 3,
                ConnectionTitle = "New Kingdom Pharaohs — Different Dynasties, One Egypt",
                Slides          = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content    = "content/figures/3_ramesses/marker_ramesses_ii.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Tutankhamun of the 18th Dynasty died young around 1323 BC. Ramesses II of the 19th Dynasty was born just 20 years later, in 1303 BC. The two pharaohs are separated by barely a generation, yet their legacies could not be more different.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content    = "Tutankhamun is remembered for his spectacular tomb and golden treasures — the physical remnants of a brief, troubled reign. Ramesses II is remembered for the sheer scale of his monuments and the 66-year sweep of his rule. One is famous for what survived underground; the other for what towers above the ground.",
                        DurationMs = 10000 },
                }
            },
        };
    }
}
