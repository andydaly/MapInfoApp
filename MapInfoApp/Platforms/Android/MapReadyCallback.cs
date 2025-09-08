using System;

namespace MapInfoApp.Platforms.Android;
internal sealed class MapReadyCallback : Java.Lang.Object, global::Android.Gms.Maps.IOnMapReadyCallback
{
    private readonly Action<global::Android.Gms.Maps.GoogleMap> _onReady;
    public MapReadyCallback(Action<global::Android.Gms.Maps.GoogleMap> onReady) => _onReady = onReady;
    public void OnMapReady(global::Android.Gms.Maps.GoogleMap googleMap) => _onReady?.Invoke(googleMap);
}
