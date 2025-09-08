using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Maps.Handlers;
#if ANDROID
using MapInfoApp.Platforms.Android;
#endif

namespace MapInfoApp
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()                     
                .UseMauiMaps()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if ANDROID
            MapHandler.Mapper.AppendToMapping("IrelandBounds", (handler, view) =>
            {
                handler.PlatformView?.GetMapAsync(new MapReadyCallback(gmap =>
                {
                    var sw = new global::Android.Gms.Maps.Model.LatLng(51.30, -10.50);
                    var ne = new global::Android.Gms.Maps.Model.LatLng(55.50,  -5.40);
                    var bounds = new global::Android.Gms.Maps.Model.LatLngBounds(sw, ne);

                    gmap.SetLatLngBoundsForCameraTarget(bounds);

                    gmap.SetMinZoomPreference(6.0f);
                }));
            });
#endif

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
