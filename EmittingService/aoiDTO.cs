using System.IO;

namespace EmittingService
{
    public class aoiDTO
    {
        public MemoryStream picture { get; set; }
        public MemoryStream csv { get; set; }
        public string timestamp { get; set; }
        public string pictureName { get; set; }
        public string pictureDirectory { get; set; }
    }
}
