namespace ThermalSight;



internal sealed class GenerationProfile
{
    public int Gen;
    public string Label;
    public string Summary;

    public int SensorLines;          
    public float OpticalBlur;        
    public float HorizontalSmear;    
    public float TemporalNoise;      
    public float ScanBanding;        
    public float BandingDrift;       
    public float FixedPattern;       
    public float ColumnPattern;      
    public int DeadPixels;           
    public float TemporalFilter;     
    public float Denoise;            
    public float LocalContrast;      
    public float AgcSpeed;           
    public float FrameRate;          
    public float NucInterval;        
    public float NucDuration;        
    public float Misregistration;    
    public float Vignette;           
    public float Equalization;       
    public float Halo;               
    public float Gamma;

    public static readonly GenerationProfile[] All =
    {
        new()
        {
            Gen = 1, Label = "Gen 1 - early scanning",
            Summary = "Scanning detector. Soft image with visible grain and faint scan banding. Small details merge and low-contrast terrain is lost.",
            SensorLines = 210, OpticalBlur = 1.25f, HorizontalSmear = 0.9f, TemporalNoise = 0.014f,
            ScanBanding = 0.009f, BandingDrift = 0.35f, FixedPattern = 0.0025f, ColumnPattern = 0f, DeadPixels = 0,
            TemporalFilter = 0f, Denoise = 0f, LocalContrast = 0f, AgcSpeed = 1.2f, FrameRate = 25f,
            NucInterval = 0f, NucDuration = 0f, Misregistration = 0f, Vignette = 0.14f, Gamma = 1.0f, Equalization = 0.1f, Halo = 0.35f,
        },
        new()
        {
            Gen = 2, Label = "Gen 2 - improved scanning",
            Summary = "Improved scanning array. Steadier outlines and clearer temperature transitions with less noise. Faint thin banding and slight non-uniformity remain.",
            SensorLines = 320, OpticalBlur = 0.95f, HorizontalSmear = 0.35f, TemporalNoise = 0.0075f,
            ScanBanding = 0.0035f, BandingDrift = 0.2f, FixedPattern = 0.0028f, ColumnPattern = 0f, DeadPixels = 0,
            TemporalFilter = 0.1f, Denoise = 0.08f, LocalContrast = 0.12f, AgcSpeed = 1.6f, FrameRate = 30f,
            NucInterval = 0f, NucDuration = 0f, Misregistration = 0f, Vignette = 0.09f, Gamma = 1.0f, Equalization = 0.25f, Halo = 0.25f,
        },
        new()
        {
            Gen = 3, Label = "Gen 3 - staring focal-plane array",
            Summary = "2D staring array. Detailed image with stable contours. Noise reduction can smooth fine texture; rare defective pixels and short calibration (NUC) freezes are possible.",
            SensorLines = 480, OpticalBlur = 0.75f, HorizontalSmear = 0f, TemporalNoise = 0.0038f,
            ScanBanding = 0f, BandingDrift = 0f, FixedPattern = 0.002f, ColumnPattern = 0.0012f, DeadPixels = 5,
            TemporalFilter = 0.25f, Denoise = 0.3f, LocalContrast = 0.25f, AgcSpeed = 2.2f, FrameRate = 30f,
            NucInterval = 150f, NucDuration = 0.35f, Misregistration = 0f, Vignette = 0.05f, Gamma = 1.0f, Equalization = 0.35f, Halo = 0.12f,
        },
        new()
        {
            Gen = 4, Label = "Gen 4 - multispectral (conceptual)",
            Summary = "Conceptual multispectral system. High detail and better separation of low-contrast areas. Still cannot see through armour or walls; brief channel misregistration during fast slewing.",
            SensorLines = 600, OpticalBlur = 0.6f, HorizontalSmear = 0f, TemporalNoise = 0.0022f,
            ScanBanding = 0f, BandingDrift = 0f, FixedPattern = 0.001f, ColumnPattern = 0.0005f, DeadPixels = 2,
            TemporalFilter = 0.18f, Denoise = 0.2f, LocalContrast = 0.35f, AgcSpeed = 2.8f, FrameRate = 50f,
            NucInterval = 300f, NucDuration = 0.25f, Misregistration = 1.4f, Vignette = 0.03f, Gamma = 1.0f, Equalization = 0.4f, Halo = 0.06f,
        },
    };

    public static GenerationProfile Get(int gen) => All[System.Math.Clamp(gen, 1, 4) - 1];
}
