using System.Collections.Generic;
using System.Drawing;

public enum ContentType { Text, Image, Video }

public class ContentSlide
{
    public ContentType Type { get; set; }
    public string Content { get; set; }   // text body OR relative file path
    public int DurationMs { get; set; }
}

public class Figure
{
    public int SymbolId { get; set; }
    public string Name { get; set; }
    public string Period { get; set; }
    public string ShortDescription { get; set; }
    public Color AccentColor { get; set; }
    public string MarkerImagePath { get; set; }
    public float FacingAngleOffset { get; set; }
    public List<ContentSlide> SoloSlides { get; set; }
    public List<SceneObject> SceneObjects { get; set; }
}

public class SceneObject
{
    public string Name { get; set; }
    public string ImagePath { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public List<ContentSlide> StorySlides { get; set; }
}

public class Relationship
{
    public int SymbolIdA { get; set; }
    public int SymbolIdB { get; set; }
    public string ConnectionTitle { get; set; }
    public List<ContentSlide> Slides { get; set; }
}

public static class MuseumData
{
    public static readonly Dictionary<int, Figure> Figures;
    public static readonly List<Relationship> Relationships;

    static MuseumData()
    {
        // Figure entries

        Figures = new Dictionary<int, Figure>
        {
            // Cleopatra VII (ID 1)
            { 1, new Figure
            {
                SymbolId = 1,
                Name = "Cleopatra VII",
                Period = "69 BC - 30 BC",
                ShortDescription = "Last Pharaoh of Ancient Egypt",
                AccentColor = Color.FromArgb(212, 175, 55),
                MarkerImagePath = "content/figures/0_cleopatra/marker_cleopatra.png",
                SceneObjects = new List<SceneObject>
                {
                    new SceneObject
                    {
                        Name = "Royal Cobra Crown",
                        ImagePath = "content/objects/0_cleopatra/royal_cobra_crown.png",
                        X = 0.18f,
                        Y = 0.30f,
                        StorySlides = new List<ContentSlide>
                        {
                            new ContentSlide { Type = ContentType.Image,
                                Content = "content/objects/0_cleopatra/royal_cobra_crown.png",
                                DurationMs = 6000 },
                            new ContentSlide { Type = ContentType.Text,
                                Content = "The uraeus cobra on Cleopatra's crown symbolized divine kingship and protection. By presenting herself with this iconography, Cleopatra linked her rule to ancient Egyptian royal legitimacy, not only to her Greek Ptolemaic heritage.",
                                DurationMs = 8500 },
                        }
                    },
                    new SceneObject
                    {
                        Name = "Alexandrian Coin",
                        ImagePath = "content/objects/0_cleopatra/alexandrian_coin.png",
                        X = 0.78f,
                        Y = 0.33f,
                        StorySlides = new List<ContentSlide>
                        {
                            new ContentSlide { Type = ContentType.Image,
                                Content = "content/objects/0_cleopatra/alexandrian_coin.png",
                                DurationMs = 6000 },
                            new ContentSlide { Type = ContentType.Text,
                                Content = "Coins minted in Alexandria carried Cleopatra's portrait and political messages. Through coinage, she projected authority across the Mediterranean, shaping how allies and rivals in Rome perceived Egypt's final pharaoh.",
                                DurationMs = 8500 },
                        }
                    },
                    new SceneObject
                    {
                        Name = "Nile Barge",
                        ImagePath = "content/objects/0_cleopatra/nile_barge.png",
                        X = 0.52f,
                        Y = 0.74f,
                        StorySlides = new List<ContentSlide>
                        {
                            new ContentSlide { Type = ContentType.Image,
                                Content = "content/objects/0_cleopatra/nile_barge.png",
                                DurationMs = 6000 },
                            new ContentSlide { Type = ContentType.Text,
                                Content = "Ancient sources describe Cleopatra's ceremonial barge as a floating stage of power. Her theatrical Nile appearances fused diplomacy, religion, and spectacle, reinforcing her image as a sovereign equal to Rome's strongest leaders.",
                                DurationMs = 9000 },
                        }
                    }
                },
                SoloSlides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/0_cleopatra/marker_cleopatra.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Cleopatra VII ruled as the last pharaoh of Ptolemaic Egypt from 51 to 30 BC.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Early Life and Rise: Born around 69 BC to Ptolemy XII Auletes, she ascended after his death, co-ruling with brothers Ptolemy XIII and XIV amid civil strife.",
                        DurationMs = 7000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Allied with Julius Caesar in 48 BC, she defeated Ptolemy XIII with his aid and bore son Caesarion. Fluent in nine languages, she pursued scholarly interests and used dramatic flair like the carpet entrance.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Reign and Conflicts: Partnered with Mark Antony after Caesar, bearing three children while ruling eastern territories granted by him. Their forces lost at the Battle of Actium in 31 BC to Octavian.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "She blended charm, strategy, and Egyptian traditions to maintain power amid Roman pressures.",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Death and Legacy: Facing capture, she died by suicide via asp bite in 30 BC, ending independent Egypt as a Roman province. Roman propaganda portrayed her as a seductress, overshadowing her political savvy.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Her Greco-Egyptian rule fused cultures, leaving enduring legends.",
                        DurationMs = 5000 },
                }
            }},

            // Nefertiti (ID 2)
            { 2, new Figure
            {
                SymbolId = 2,
                Name = "Nefertiti",
                Period = "c. 1370 BC - 1330 BC",
                ShortDescription = "Great Royal Wife of Akhenaten",
                AccentColor = Color.FromArgb(100, 190, 230),
                MarkerImagePath = "content/figures/1_nefertiti/marker_nefertiti.png",
                SoloSlides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/1_nefertiti/marker_nefertiti.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Nefertiti was Great Royal Wife of Akhenaten (c. 1370–1330 BC), 18th Dynasty.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Early Life: Origins obscure, she married Amenhotep IV early in his reign. Bore six daughters including Meritaten and Ankhesenpaaten (later Tutankhamun's wife Ankhesenamun).",
                        DurationMs = 7000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Played key role promoting Atenism; her 1912 bust ensures iconic beauty legacy.",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Role in Power: Equaled Akhenaten in early Amarna art, smiting enemies and offering like a pharaoh. Family moved to Akhetaten where she dominated temples and tombs.",
                        DurationMs = 7000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Visibility declined after mourning daughter Meketaten in year 12.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Later Theories and Legacy: Possibly ruled as pharaoh Neferneferuaten after Akhenaten, before Tutankhamun. Disappearance around year 16 fuels debates: death, disgrace, or coregency.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Her Atenism radically shifted religion, later reversed by successors.",
                        DurationMs = 6000 },
                }
            }},

            // Tutankhamun (ID 3)
            { 3, new Figure
            {
                SymbolId = 3,
                Name = "Tutankhamun",
                Period = "c. 1341 BC - 1323 BC",
                ShortDescription = "The Boy King",
                AccentColor = Color.FromArgb(218, 165, 32),
                SoloSlides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Tutankhamun reigned c. 1333–1324 BC, restoring traditional religion post-Amarna.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Early Reign: Born Tutankhaten (possibly Akhenaten's son), ascended young marrying half-sister Ankhesenpaaten. Abandoned Amarna for Memphis/Thebes, adopting Amun-honoring names.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Restoration Stela documented revival of damaged temples.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Achievements: Rebuilt Karnak sphinx avenue and Luxor colonnade, endowing Amun/Ptah cults. Led successful Nubian/Asian campaigns; Mitanni gifts showed diplomacy.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Deified in life with Nubian temples worshiping him as Amun.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Death and Discovery: Died around age 18 from malaria, leg fracture, or both; no immediate heir. Intact 1922 tomb held 5,000 artifacts, creating 'King Tut' fame.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Successors Ay and Horemheb erased Amarna legacy, usurping his monuments.",
                        DurationMs = 6000 },
                }
            }},

            // Ramesses II (ID 4)
            { 4, new Figure
            {
                SymbolId = 4,
                Name = "Ramesses II",
                Period = "c. 1303 BC - 1213 BC",
                ShortDescription = "Ramesses the Great",
                AccentColor = Color.FromArgb(210, 80, 50),
                MarkerImagePath = "content/figures/3_ramesses/marker_ramesses_ii.png",
                SoloSlides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/3_ramesses/marker_ramesses_ii.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Ramses II, 'the Great,' ruled 1279–1213 BC, Egypt's longest reign at 66 years.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Rise to Power: Seti I's son, early coregent trained for war; built Per-Ramesses Delta capital. Resumed Abydos temple and visited Thebes for Opet festival.",
                        DurationMs = 7000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Fathered 100+ children; Nefertari featured at Abu Simbel.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Military Campaigns: Year 5 Battle of Kadesh vs. Hittites stalemated but became propaganda triumph carved widely. 1258 BC peace treaty—history's first—with Hittite marriages followed 16 war years.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Fought Libyans/Edomites, securing but not expanding empire.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Building Legacy: Commissioned Ramesseum, Abu Simbel colossi, Karnak expansions; statues glorified him. Era prospered; posthumously worshiped as later kings took his name.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Died near 90, mummified; not biblical Exodus pharaoh.",
                        DurationMs = 5000 },
                }
            }},

            // Hatshepsut (ID 5)
            { 5, new Figure
            {
                SymbolId = 5,
                Name = "Hatshepsut",
                Period = "c. 1507 BC - 1458 BC",
                ShortDescription = "Egypt's Longest-Reigning Female Pharaoh",
                AccentColor = Color.FromArgb(175, 130, 70),
                MarkerImagePath = "content/figures/4_hatshepsut/marker_hatshepsut.png",
                SoloSlides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/4_hatshepsut/marker_hatshepsut.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Hatshepsut ruled as powerful 18th Dynasty pharaoh 1479–1458 BC, rare female king.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Ascension: Widow of Thutmose II and aunt/stepmother-regent to Thutmose III, she claimed full pharaoh title. Assumed male regalia like false beard; proclaimed divine Amun birth.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Emphasized prosperous trade over warfare.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Key Expeditions: Punt trade voyage yielded myrrh/incense, depicted on Deir el-Bahri mortuary temple. Built extensively: Karnak/Luxor obelisks, Red Chapel.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Maintained peace, expanding Egyptian influence economically.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Legacy: Thutmose III later erased her cartouches and images after death. Mummy identified 2007; ruled effectively 20+ years.",
                        DurationMs = 7000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Exemplifies successful female pharaonic rule in patriarchy.",
                        DurationMs = 5000 },
                }
            }},

            // Akhenaten (ID 6)
            { 6, new Figure
            {
                SymbolId = 6,
                Name = "Akhenaten",
                Period = "c. 1380 BC - 1336 BC",
                ShortDescription = "The Heretic Pharaoh",
                AccentColor = Color.FromArgb(255, 160, 50),
                SoloSlides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Akhenaten (Amenhotep IV) ruled 1353–1336 BC, Atenism revolution founder.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Religious Revolution: Renamed self, founded Akhetaten (Amarna) capital for exclusive Aten sun disk worship, suppressing Amun. Introduced elongated bodies in intimate family art with Nefertiti.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Erected open-roof Aten temples at Karnak then Amarna.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Family and Rule: Married Nefertiti; six daughters, possible sons like Tutankhamun. Year 16 records show Nefertiti prominent.",
                        DurationMs = 7000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Religious/economic strains marked 17-year reign; died year 17.",
                        DurationMs = 5000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Aftermath: Tutankhamun/Ay reversed reforms; Amarna abandoned, names hacked out. Labeled heretic but early monotheism precursor.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Boundary stelae outlined his visionary city.",
                        DurationMs = 5000 },
                }
            }},
        };

        // Relationship entries

        Relationships = new List<Relationship>
        {
            // Nefertiti and Akhenaten (royal couple)
            new Relationship
            {
                SymbolIdA = 2,
                SymbolIdB = 6,
                ConnectionTitle = "Husband & Wife — The Revolutionary Royal Couple",
                Slides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/1_nefertiti/marker_nefertiti.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Nefertiti and Akhenaten were one of history's most famous royal couples. Together they co-ruled Egypt, dismantled its ancient religion, founded a new capital city at Amarna, and replaced millennia of polytheism with the monotheistic worship of Aten.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Nefertiti bore Akhenaten six daughters. Their family was depicted in Amarna art in unusually tender, intimate scenes — a complete break from the formal, rigid conventions of earlier Egyptian art.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Egyptologists believe Nefertiti wielded extraordinary political power, possibly equal to Akhenaten's own.",
                        DurationMs = 7000 },
                }
            },

            // Tutankhamun and Akhenaten (father and son)
            new Relationship
            {
                SymbolIdA = 3,
                SymbolIdB = 6,
                ConnectionTitle = "Father & Son — A Legacy Reversed",
                Slides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Tutankhamun was born as 'Tutankhaten' — 'Living Image of Aten' — the son of Akhenaten. He inherited a kingdom in religious turmoil.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Under the guidance of his advisors, the young Pharaoh reversed every reform his father had introduced. Tutankhamun changed his name from Tutankhaten to Tutankhamun, restored the old gods, reopened the temples.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "He moved the capital back to Memphis, and began the systematic erasure of his own father's name and image from Egyptian history.",
                        DurationMs = 8000 },
                }
            },

            // Nefertiti and Tutankhamun
            new Relationship
            {
                SymbolIdA = 2,
                SymbolIdB = 3,
                ConnectionTitle = "Family Ties in Amarna Chaos",
                Slides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/1_nefertiti/marker_nefertiti.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Nefertiti was likely Tutankhamun's stepmother, or possibly biological mother based on ongoing DNA debates from mummy analyses. Her daughter Ankhesenamun married Tutankhamun, intertwining Amarna family lines tightly.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "This royal incest preserved purity but led to health issues like Tutankhamun's deformities.",
                        DurationMs = 7000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Nefertiti may have ruled briefly as the mysterious pharaoh Neferneferuaten after Akhenaten's death, bridging to Tutankhamun's ascension. Tutankhamun then restored Amun worship, abandoning Atenism and dismantling Amarna's experiments.",
                        DurationMs = 10000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Her potential interim reign underscores power struggles in the family's turbulent end. These ties encapsulate Amarna Period chaos: religious upheaval followed by reversal.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Tutankhamun's young death at 18 without viable heirs sealed the era. The story reveals fragile dynastic survival amid innovation's fallout.",
                        DurationMs = 8000 },
                }
            },

            // Cleopatra and Nefertiti (queens of Egypt)
            new Relationship
            {
                SymbolIdA = 1,
                SymbolIdB = 2,
                ConnectionTitle = "Powerful Queens Shaping Egypt",
                Slides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/0_cleopatra/marker_cleopatra.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Cleopatra and Nefertiti both exerted extraordinary influence in ancient Egypt's male-dominated hierarchy. Nefertiti stood equal to Akhenaten in Amarna art, co-promoting the radical Aten sun cult that reshaped religion.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Cleopatra, centuries later, forged alliances with Julius Caesar and Mark Antony, using intellect and charm to protect Egypt from Roman absorption.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/1_nefertiti/marker_nefertiti.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Their iconic beauty endures: Nefertiti through her famous Berlin bust discovered in 1912, Cleopatra via legends amplified by Roman foes. Separated by over 1,300 years, they defied societal norms as women in power.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Both leveraged symbolism—art for Nefertiti, dramatic diplomacy for Cleopatra—to cement legacies. Nefertiti's religious innovations paralleled Cleopatra's political maneuvers against empire builders.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "They highlight timeless female agency in pharaonic history. Stories inspire modern views of strong ancient queens.",
                        DurationMs = 7000 },
                }
            },

            // Cleopatra and Ramesses II (famous rulers from different eras)
            new Relationship
            {
                SymbolIdA = 1,
                SymbolIdB = 4,
                ConnectionTitle = "Egypt's Most Celebrated Rulers — 1,200 Years Apart",
                Slides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/0_cleopatra/marker_cleopatra.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Ramesses II ruled during Egypt's New Kingdom golden age in the 13th century BC. Cleopatra VII ruled more than 1,200 years later, in the 1st century BC. By Cleopatra's time, the Abu Simbel temples that Ramesses had built were already ancient monuments.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/3_ramesses/marker_ramesses_ii.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Both are considered among the greatest rulers in Egypt's 3,000-year history. Ramesses defined Egypt's imperial power at its height; Cleopatra held the kingdom together in its final century with intelligence, diplomacy, and sheer will.",
                        DurationMs = 9000 },
                }
            },

            // Cleopatra and Akhenaten (radical reformers)
            new Relationship
            {
                SymbolIdA = 1,
                SymbolIdB = 6,
                ConnectionTitle = "Radical Reformers",
                Slides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/0_cleopatra/marker_cleopatra.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Akhenaten radically imposed Atenism, relocating to Amarna and suppressing old gods; Cleopatra navigated Roman integration while upholding pharaonic divinity. Both defied empires—his against priests, hers against Octavian—using bold visions.",
                        DurationMs = 10000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Separated by dynasties, parallels in innovation abound. Akhenaten's elongated art and family focus echo Cleopatra's theatrical diplomacy like the carpet ruse.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Erasures followed: Amarna abandoned, her image vilified by Romans. Survivals include boundary stelae and legends.",
                        DurationMs = 7000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "They reshaped religion/politics innovatively, leaving controversial yet enduring marks. Heretic and seductress labels hide true impacts. Stories connect ancient reform across millennia.",
                        DurationMs = 9000 },
                }
            },

            // Hatshepsut and Ramesses II (builder pharaohs)
            new Relationship
            {
                SymbolIdA = 5,
                SymbolIdB = 4,
                ConnectionTitle = "Architects of Grandeur",
                Slides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/4_hatshepsut/marker_hatshepsut.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Ramses II and Hatshepsut prioritized colossal architecture over constant war, leaving iconic legacies. Hatshepsut's Deir el-Bahri temple vividly depicts Punt expeditions; Ramses' Abu Simbel and Ramesseum boast similar spectacle.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Both invested in Karnak expansions: her obelisks, his hypostyle halls. Propaganda defined them—Hatshepsut claimed divine Amun birth, Ramses exaggerated Kadesh as victory.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/3_ramesses/marker_ramesses_ii.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Peaceful prosperity marked Hatshepsut's trade focus and Ramses' treaty era. Structures endured millennia, symbolizing eternal power.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Hatshepsut ruled 22 years effectively before erasure by Thutmose III; Ramses' 66-year reign outlasted all. Rediscovered today, their temples highlight Egypt's building zenith. They prove monuments outlive reigns.",
                        DurationMs = 10000 },
                }
            },

            // Akhenaten and Hatshepsut (broke tradition)
            new Relationship
            {
                SymbolIdA = 6,
                SymbolIdB = 5,
                ConnectionTitle = "Revolutionary Rulers",
                Slides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/4_hatshepsut/marker_hatshepsut.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Hatshepsut and Akhenaten both shattered conventions: she as female pharaoh, he with Aten monotheism. Early Theban builds defined them—her towering obelisks and Red Chapel, his open Aten temples at Karnak.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Both innovated amid 18th Dynasty tensions. Successors rejected them: Thutmose III defaced Hatshepsut, Tutankhamun abandoned Akhenaten's Amarna.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Yet rediscovery restored their stories—Hatshepsut's mummy in 2007, Akhenaten's art revolution. Erasures aimed to uphold tradition.",
                        DurationMs = 8000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Their boldness reshaped Egypt temporarily, influencing views of pharaonic daring. Female rule and religious pivot challenged norms profoundly. Legacies persist in modern fascination.",
                        DurationMs = 9000 },
                }
            },

            // Ramesses II and Tutankhamun (New Kingdom pharaohs)
            new Relationship
            {
                SymbolIdA = 4,
                SymbolIdB = 3,
                ConnectionTitle = "From Boy King to Empire Builder",
                Slides = new List<ContentSlide>
                {
                    new ContentSlide { Type = ContentType.Image,
                        Content = "content/figures/3_ramesses/marker_ramesses_ii.png",
                        DurationMs = 6000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Tutankhamun's short reign stabilized Egypt post-Amarna religious turmoil, much like Ramses II later consolidated after Seti I. Both led military successes against Nubians and Asiatics.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Tutankhamun's campaigns echoed Ramses' famous Kadesh and Libyan battles. They shared diplomatic prowess, with Tutankhamun gaining Mitanni gifts and Ramses sealing history's first peace treaty.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Extensive builders, Tutankhamun rebuilt Karnak's sphinxes and Luxor while Ramses expanded it massively alongside Abu Simbel. Deified in life, Tutankhamun had Nubian cults; Ramses enjoyed posthumous worship.",
                        DurationMs = 10000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Spanning 18th to 19th Dynasties, they exemplify pharaonic recovery and grandeur. Tutankhamun died young, his tomb's 1922 discovery contrasting Ramses' mummified longevity to 90.",
                        DurationMs = 9000 },
                    new ContentSlide { Type = ContentType.Text,
                        Content = "Successors usurped Tutankhamun's works as later kings emulated Ramses. Their arcs from crisis to peak define resilient rule.",
                        DurationMs = 8000 },
                }
            },
        };
    }
}

