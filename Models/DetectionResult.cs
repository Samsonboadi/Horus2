// Models/DetectionResult.cs
namespace Test.Models
{
    public class DetectionResult
    {
        public string ObjectName { get; set; }
        public double Confidence { get; set; }
        public BoundingBox BoundingBox { get; set; }
        public string ModelUsed { get; set; }
        public double ProcessingTime { get; set; }
    }

    public class BoundingBox
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}