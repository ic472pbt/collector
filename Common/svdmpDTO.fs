namespace Common 
module svdmpDTO =
    type LeadPositionDTO =
        {Type: string; Angle: float32; Size: System.Drawing.SizeF}

    type LeadDTO =
        {
            number: int
            numOfGrids: int
            pitch: float32
            groupPitch: float32
            Vector: System.Drawing.PointF
            angle: float32
            Position: LeadPositionDTO
        }
    
    type BoardInfoDTO =
        {
              refDes: string
              boardName: string
              coordinates: System.Drawing.Point
              rotation: int
              boardmatrix: int
              boardId: int
        }
    
    type svdmpDTO =
        {
              picture : System.IO.MemoryStream
              size_nm : System.Drawing.Size
              hashkey : string
              componentType : string
              position_mm : System.Drawing.PointF
              scale_mm_px : System.Drawing.SizeF
              body_nm : System.Drawing.Size
              angle_rad : float
              orientation_rad : float
              binning : int
              isDumped : bool
              timestamp : string
              leads : LeadDTO[]
              DipfType : string
              SourceFolder : string
              svdmp : string
              boardInfo : BoardInfoDTO option
        }
        member _.packageCase = System.String.Empty
        member _.packaging = System.String.Empty