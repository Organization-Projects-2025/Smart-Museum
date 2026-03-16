// HandTrackingData.cs
// Data structures for deserializing hand tracking JSON from the Python mediapipe module.
//
// JSON shape (array of hands):
// [
//   {
//     "hand": "Right",
//     "fingers_up": 3,
//     "fingers": { "thumb": true, "index": true, "middle": true, "ring": false, "pinky": false },
//     "palm_position": { "x": 320, "y": 410, "z": -0.0523 }
//   }
// ]

using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace HandTracking
{
    [DataContract]
    public class PalmPosition
    {
        [DataMember(Name = "x")] public int   X { get; set; }
        [DataMember(Name = "y")] public int   Y { get; set; }
        [DataMember(Name = "z")] public float Z { get; set; }

        public override string ToString() => $"({X}, {Y}, z={Z:F4})";
    }

    [DataContract]
    public class FingerState
    {
        [DataMember(Name = "thumb")]  public bool Thumb  { get; set; }
        [DataMember(Name = "index")]  public bool Index  { get; set; }
        [DataMember(Name = "middle")] public bool Middle { get; set; }
        [DataMember(Name = "ring")]   public bool Ring   { get; set; }
        [DataMember(Name = "pinky")]  public bool Pinky  { get; set; }

        public override string ToString() =>
            $"T:{B(Thumb)} I:{B(Index)} M:{B(Middle)} R:{B(Ring)} P:{B(Pinky)}";

        private static char B(bool v) => v ? '1' : '0';
    }

    [DataContract]
    public class HandData
    {
        [DataMember(Name = "hand")]          public string       Hand         { get; set; }
        [DataMember(Name = "fingers_up")]    public int          FingersUp    { get; set; }
        [DataMember(Name = "fingers")]       public FingerState  Fingers      { get; set; }
        [DataMember(Name = "palm_position")] public PalmPosition PalmPosition { get; set; }

        public bool IsRight => Hand == "Right";
        public bool IsLeft  => Hand == "Left";

        public override string ToString() =>
            $"[{Hand}] {FingersUp} fingers | {Fingers} | palm={PalmPosition}";
    }
}
