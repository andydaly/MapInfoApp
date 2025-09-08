using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MapInfoApp.Models;

namespace MapInfoApp;

public partial class MainPage : ContentPage
{
    private readonly IrelandMapViewModel _vm = new();
    private static readonly HttpClient http = new();
    private readonly TimeSpan _debounce = TimeSpan.FromMilliseconds(200);
    private CancellationTokenSource _viewportCts;
    private const int MaxPinsOnScreen = 35;

    private string _driveFileId = String.Empty;

    public MainPage()
    {
        InitializeComponent();

        BindingContext = _vm;
        _vm.OverlayVisibleChanged = visible => InfoOverlay.IsVisible = visible;

        var center = new Location(53.4, -8.0);
        Map.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(250)));

        Map.MapClicked += (_, __) => _vm.OverlayVisibleChanged(false);

#if ANDROID
        try
        {
            var ctx = Platform.AppContext;
            int appNameId = Resource.String.app_name;
            if (appNameId != 0)
                Title = ctx.GetString(appNameId);
            else
                Title = "Ireland Map";

            int driveId = Resource.String.drive_file_id;
            if (driveId != 0)
                _driveFileId = ctx.GetString(driveId);
        }
        catch
        {
            Title = "Ireland Map";
        }
#else
        Title = "Ireland Map";
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var kmlUrl = $"https://drive.google.com/uc?export=download&id={_driveFileId}";
        await LoadPinsFromKmlUrlAsync(kmlUrl);
    }

    private async Task LoadPinsFromKmlUrlAsync(string url)
    {
        try
        {
            using var resp = await http.GetAsync(url);
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync();
            await ParseKmlStreamAndAddPinsAsync(stream);
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(
                () => DisplayAlert("KML Download Error", ex.Message, "OK"));
        }
    }

    private async Task ParseKmlStreamAndAddPinsAsync(Stream kmlStream)
    {
        var doc = XDocument.Load(kmlStream);
        XNamespace k = "http://www.opengis.net/kml/2.2";

        var places = new List<Place>();

        foreach (var pm in doc.Descendants(k + "Placemark"))
        {
            var name = (string)pm.Element(k + "name") ?? string.Empty;
            var rawDesc = (string)pm.Element(k + "description") ?? string.Empty;

            var pointCoords = pm.Descendants(k + "Point")
                                .Elements(k + "coordinates")
                                .Select(x => (x.Value ?? "").Trim())
                                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(pointCoords)) continue;

            var parts = pointCoords.Split(',');
            if (parts.Length < 2) continue;

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;

            var (cleanDesc, insta, tiktok) = ExtractLinksAndCleanDescription(rawDesc);

            places.Add(new Place(
                Name: string.IsNullOrWhiteSpace(name) ? "Untitled" : name,
                RawDescription: rawDesc,
                CleanDescription: cleanDesc,
                Description: string.Empty,
                InstagramUrl: insta,
                TikTokUrl: tiktok,
                Lat: lat,
                Lng: lng
            ));
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _vm.Places.Clear();
            foreach (var p in places)
                _vm.Places.Add(p);
        });

        StartViewportPinUpdater();
    }

    private void StartViewportPinUpdater()
    {
        _ = UpdatePinsForViewportAsync();

        Map.PropertyChanged -= Map_PropertyChanged;
        Map.PropertyChanged += Map_PropertyChanged;
    }

    private void Map_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Map.VisibleRegion))
            return;

        _viewportCts?.Cancel();
        _viewportCts = new CancellationTokenSource();
        var token = _viewportCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounce, token);
                if (!token.IsCancellationRequested)
                    await UpdatePinsForViewportAsync(token);
            }
            catch (TaskCanceledException) { }
        });
    }

    private async Task UpdatePinsForViewportAsync(CancellationToken ct = default)
    {
        var region = Map.VisibleRegion;
        if (region is null) return;

        double latHalf = region.LatitudeDegrees / 2.0;
        double lngHalf = region.LongitudeDegrees / 2.0;
        double minLat = region.Center.Latitude - latHalf;
        double maxLat = region.Center.Latitude + latHalf;
        double minLng = region.Center.Longitude - lngHalf;
        double maxLng = region.Center.Longitude + lngHalf;

        var inView = _vm.Places.Where(p => p.Lat >= minLat && p.Lat <= maxLat && p.Lng >= minLng && p.Lng <= maxLng).ToList();

        var selected = SelectMostSeparated(inView, MaxPinsOnScreen: MaxPinsOnScreen, region.Center);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Map.Pins.Clear();
            foreach (var p in selected)
            {
                var pin = new Pin
                {
                    Label = p.Name,
                    Address = p.Description,
                    Location = new Location(p.Lat, p.Lng),
                    Type = PinType.Place
                };
                pin.MarkerClicked += OnPinMarkerClicked;
                Map.Pins.Add(pin);
            }
        });
    }

    private static List<Place> SelectMostSeparated(
        List<Place> candidates, int MaxPinsOnScreen, Location center)
    {
        if (candidates.Count <= MaxPinsOnScreen) return candidates;

        static double Dist(Place a, Place b) =>
            DistanceBetween(a.Lat, a.Lng, b.Lat, b.Lng);

        var seed = candidates
            .OrderByDescending(p => DistanceBetween(p.Lat, p.Lng, center.Latitude, center.Longitude)).First();

        var selected = new List<Place>(MaxPinsOnScreen) { seed };

        var nearestDist = new Dictionary<Place, double>(candidates.Count);
        foreach (var c in candidates)
            nearestDist[c] = Dist(c, seed);

        while (selected.Count < MaxPinsOnScreen)
        {
            Place next = null!;
            double best = double.NegativeInfinity;

            foreach (var c in candidates)
            {
                if (ReferenceEquals(c, seed) || selected.Contains(c)) continue;

                double d = nearestDist[c];
                if (d > best)
                {
                    best = d;
                    next = c;
                }
            }

            if (next is null) break; 

            selected.Add(next);

            foreach (var c in candidates)
            {
                if (selected.Contains(c)) continue;
                double d = Dist(c, next);
                if (d < nearestDist[c]) nearestDist[c] = d;
            }
        }

        return selected;
    }

    private static double DistanceBetween(double lat1, double lng1, double lat2, double lng2)
    {
        double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLng = (lng2 - lng1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) *
                   Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2.0 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }


    private static (string CleanDescription, string InstagramUrl, string TikTokUrl) ExtractLinksAndCleanDescription(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (string.Empty, string.Empty, string.Empty);

        string text = Regex.Replace(raw, "<.*?>", string.Empty);

        var instaMatch = Regex.Match(raw, @"https?://(?:www\.)?instagram\.com/[^\s""'<>]+", RegexOptions.IgnoreCase);
        string insta = instaMatch.Success ? instaMatch.Value : string.Empty;

        var tiktokMatch = Regex.Match(raw, @"https?://(?:www\.)?tiktok\.com/[^\s""'<>]+", RegexOptions.IgnoreCase);
        string tiktok = tiktokMatch.Success ? tiktokMatch.Value : string.Empty;

        foreach (var link in new[] { insta, tiktok })
        {
            if (!string.IsNullOrEmpty(link))
                text = text.Replace(link, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        text = Regex.Replace(text, @"\s{2,}", " ").Trim();

        return (text, insta, tiktok);
    }

    private async void OnPinMarkerClicked(object sender, PinClickedEventArgs e)
    {
        var pin = (Pin)sender;

        var place = _vm.Places.FirstOrDefault(pl =>
            Math.Abs(pl.Lat - pin.Location.Latitude) < 1e-6 &&
            Math.Abs(pl.Lng - pin.Location.Longitude) < 1e-6);

        if (place is not null)
        {
            try
            {
                place.StreetViewImageUrl = await TryGetStreetViewImageUrlAsync(place.Lat, place.Lng);
            }
            catch
            {
                place.StreetViewImageUrl = string.Empty;
            }

            _vm.SelectedPlace = place;

            e.HideInfoWindow = true;  
            _vm.OverlayVisibleChanged(true);
        }
    }

    private async void OnInstagramClicked(object sender, EventArgs e)
    {
        var url = _vm.SelectedPlace?.InstagramUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try { await Launcher.OpenAsync(new Uri(url)); }
            catch (Exception ex)
            {
                await DisplayAlert("Instagram", $"Could not open link.\n{ex.Message}", "OK");
            }
        }
    }

    private async void OnTikTokClicked(object sender, EventArgs e)
    {
        var url = _vm.SelectedPlace?.TikTokUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            try { await Launcher.OpenAsync(new Uri(url)); }
            catch (Exception ex)
            {
                await DisplayAlert("TikTok", $"Could not open link.\n{ex.Message}", "OK");
            }
        }
    }

    private async void OnGoogleMapsClicked(object sender, EventArgs e)
    {
        var place = _vm.SelectedPlace;
        if (place == null) return;

        try
        {
            var url = $"https://www.google.com/maps/search/?api=1&query={place.Lat.ToString(CultureInfo.InvariantCulture)},{place.Lng.ToString(CultureInfo.InvariantCulture)}";
            await Launcher.OpenAsync(new Uri(url));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Google Maps", $"Could not open link.\n{ex.Message}", "OK");
        }
    }

    private static string GetGoogleApiKey()
    {
#if ANDROID
        try
        {
            var ctx = Platform.AppContext;
            int id = Resource.String.google_maps_api_key;
            if (id != 0)
                return ctx.GetString(id);
        }
        catch { }
#endif
        return "";
    }

    private async Task<string> TryGetStreetViewImageUrlAsync(double lat, double lng, CancellationToken ct = default)
    {
        var key = GetGoogleApiKey();
        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var metaUrl = $"https://maps.googleapis.com/maps/api/streetview/metadata?location={lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}&radius=50&source=outdoor&key={Uri.EscapeDataString(key)}";
        using var metaResp = await http.GetAsync(metaUrl, ct);
        if (!metaResp.IsSuccessStatusCode) return string.Empty;

        var metaJson = await metaResp.Content.ReadAsStringAsync(ct);
        if (!metaJson.Contains("\"status\" : \"OK\"", StringComparison.OrdinalIgnoreCase) &&
            !metaJson.Contains("\"status\":\"OK\"", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var imgUrl =
            $"https://maps.googleapis.com/maps/api/streetview" +
            $"?size=640x300" +
            $"&location={lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}" +
            $"&fov=80&pitch=0&source=outdoor" +
            $"&key={Uri.EscapeDataString(key)}";

        return imgUrl;
    }
}
