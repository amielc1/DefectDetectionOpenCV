namespace NdtImageProcessor.Models;

/// <summary>
/// Represents a detected defect in the image.
/// </summary>
public class DefectItem
{
    public int Id { get; }
    public double Area { get; }
    public string Status { get; }

    public DefectItem(int id, double area, string status)
    {
        Id = id;
        Area = area;
        Status = status;
    }
}

/// <summary>
/// Result of the image analysis process.
/// </summary>
public class AnalysisResult
{
    public OpenCvSharp.Mat DisplayImage { get; }
    public System.Collections.Generic.List<DefectItem> Defects { get; }

    public AnalysisResult(OpenCvSharp.Mat displayImage, System.Collections.Generic.List<DefectItem> defects)
    {
        DisplayImage = displayImage;
        Defects = defects;
    }
}
