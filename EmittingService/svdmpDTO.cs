using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace EmittingService
{
    public class LeadPositionDTO
    {
        public string Type { get; set; }
        public float Angle { get; set; }
        public SizeF Size { get; set; }
    }
    public class LeadDTO
    {
        public int number { get; set; }
        public int numOfGrids { get; set; }
        public float pitch { get; set; }
        public float groupPitch { get; set; }
        public PointF Vector { get; set; }
        public float angle { get; set; }
        public LeadPositionDTO Position { get; set; }
    }

    public class BoardInfoDTO
    {
        public string refDes { get; set; }
        public string boardName { get; set; }
        public Point coordinates { get; set; }
        public int rotation { get; set; }
        public int boardmatrix { get; set; }
        public int boardId { get; set; }
    }

    public class svdmpDTO
    {
        public MemoryStream picture { get; set; }
        public Size size_nm { get; set; }
        public string hashkey { get; set; }
        public string componentType { get; set; }
        public string packageCase { get { return ""; } }
        public string packaging { get { return ""; } }
        public PointF position_mm { get; set; }
        public SizeF scale_mm_px { get; set; }
        public Size body_nm { get; set; }
        public double angle_rad { get; set; }
        public double orientation_rad { get; set; }
        public int binning { get; set; }
        public bool isDumped { get; set; }
        public string timestamp { get; set; }
        public LeadDTO[] leads { get; set; }
        public string DipfType { get; set; }
        public string SourceFolder { get; set; }
        public string svdmp { get; set; }
        public BoardInfoDTO boardInfo { get; set; }
    }
}
