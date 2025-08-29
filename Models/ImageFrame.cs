// Models/ImageFrame.cs
using System;

namespace Test.Models
{
    public class ImageFrame
    {
        public string Path { get; set; }
        public string FileName { get; set; }
        public DateTime Timestamp { get; set; }
        public int FrameNumber { get; set; }
        public string ThumbnailPath { get; set; }
    }
}
