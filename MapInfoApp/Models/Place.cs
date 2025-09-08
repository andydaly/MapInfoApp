
namespace MapInfoApp.Models
{
    public record Place(
    string Name,
    string RawDescription,
    string CleanDescription,
    string Description,      
    string InstagramUrl,
    string TikTokUrl,
    double Lat,
    double Lng)
    {
        public string StreetViewImageUrl { get; set; } = string.Empty;
        public bool HasStreetView => !string.IsNullOrWhiteSpace(StreetViewImageUrl);
        public bool HasInstagram => !string.IsNullOrWhiteSpace(InstagramUrl);
        public bool HasTikTok => !string.IsNullOrWhiteSpace(TikTokUrl);
        public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    }
}
