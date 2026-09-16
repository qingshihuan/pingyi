# Image-processing regression test runtime

The standalone Core test host now directly exercises SkiaSharp image decoding and resizing. On Linux it uses SkiaSharp.NativeAssets.Linux.NoDependencies 3.119.4, matching the existing SkiaSharp 3.119.4 infrastructure reference. Source: https://github.com/mono/SkiaSharp (MIT and upstream third-party notices).

This private test-project dependency is not referenced by PingYi.App, does not replace its Avalonia/Skia native assets, and is not copied into application release packages by this change. Production dependencies and license collection are unchanged.
