namespace WebWindowUI.Core.Platform;

public enum ClipboardDataType
{ 
    Text,
    Html,
    Url,
    Files,
    Bitmap,
    Custom
}

public abstract record ClipboardData
{
    public ClipboardDataType Type { get; set; }
}

public record ClipboardTextData : ClipboardData
{ 
    public string Text { get; set; }
}

public record ClipboardHtmlData : ClipboardData
{
    public string Html { get; set; }
}

public record ClipboardUrlData : ClipboardData
{
    public Uri Url { get; set; }
}

public record ClipboardFilesData : ClipboardData
{
    public List<string> Files { get; set; }
}

public record ClipboardBitmapData : ClipboardData
{
    public byte[] Bitmap { get; set; }
}

public record ClipboardCustomData : ClipboardData
{
    public object Custom { get; set; }
}

public interface IClipboard
{
    public void RegisterCustomData(string key)
    { 
        
    }

    public void SetClipboardData(ClipboardData data)
    { 
        
    }

    public ClipboardData? GetClipboardData()
    {
        return null;
    }
}
